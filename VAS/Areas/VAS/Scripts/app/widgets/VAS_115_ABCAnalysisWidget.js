/**
 * ABC Analysis Widget (warehouse-level Pareto by current stock value)
 * Widget number 115 - reassign on hand-off.
 * Warehouse filter + three class rows (A/B/C) with value-contribution bars and a
 * class-detail modal. Classification (abc_class) comes from SQL; JS never
 * re-classifies. Backend - VAS_115_ABCAnalysisWidget/GetWarehouses, GetABCAnalysis
 * Summary Message Table
 *  # | Current Text                                        | Message Key
 * ---+-----------------------------------------------------+-----------------------------
 *  1 | ABC Analysis                                        | VAS_115_ABCAnalysis
 *  2 | Value contribution by class                         | VAS_115_ABCSub
 *  3 | Warehouse                                           | VAS_115_Warehouse
 *  4 | Class                                               | VAS_115_Class
 *  5 | of SKUs                                             | VAS_115_OfSKUs
 *  6 | value                                               | VAS_115_ValueShare
 *  7 | Items                                               | VAS_115_ItemsTitle
 *  8 | of value                                            | VAS_115_OfValue
 *  9 | Profile                                             | VAS_115_Profile
 * 10 | High value - tight control                          | VAS_115_ProfileA
 * 11 | Moderate value - periodic review                    | VAS_115_ProfileB
 * 12 | Low value - routine or bulk control                 | VAS_115_ProfileC
 * 13 | SKUs                                                | VAS_115_SKUs
 * 14 | Value                                               | VAS_115_ValueLabel
 * 15 | Representative SKUs                                  | VAS_115_RepresentativeSKUs
 * 16 | SKU                                                 | VAS_115_SKU
 * 17 | Name                                                | VAS_115_Name
 * 18 | Stock Value                                         | VAS_115_StockValue
 * 19 | No active warehouse is available.                   | VAS_115_NoWarehouse
 * 20 | No on-hand stock is available for this warehouse.   | VAS_115_NoStock
 * 21 | Current cost is not available for the selected ...  | VAS_115_NoCost
 * 22 | Couldn't load                                       | VAS_CouldntLoad
 * 23 | Retry                                               | VAS_115_Retry
 * 24 | No items.                                           | VAS_115_NoItems
 * 25 | Showing / of                                        | VAS_Showing / VAS_Of
 * 26 | Previous page / Next page                           | VAS_PreviousPage / VAS_NextPage
 * 27 | Close                                               | Close
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_115_ABCAnalysisWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-abc-root">');
        var $card;
        var $select;
        var $body;
        var $busy;
        var warehouseRequest;
        var abcRequest;
        var $modal;
        var $modalTitle;
        var $modalBadge;
        var $modalBody;
        var modalEventNamespace = '.MPCAbcModal';
        var eventNamespace = 'MPCAbcAnalysis';

        var MODAL_PER_PAGE = 5;
        var CLASS_COLOR = { A: '#20a464', B: '#0083da', C: '#d78b10' };
        var CLASS_TONE = { A: 'ok', B: 'info', C: 'warn' };
        var NOMINAL = { A: 80, B: 15, C: 5 };

        var state = { warehouses: [], warehouseId: 0, data: null };
        var modalState = { page: 0, cls: null, items: [] };
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;
        var $lastFocusedRow = null;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        function profileFor(cls) {
            if (cls === 'A') { return label('VAS_115_ProfileA', 'High value - tight control'); }
            if (cls === 'B') { return label('VAS_115_ProfileB', 'Moderate value - periodic review'); }
            return label('VAS_115_ProfileC', 'Low value - routine or bulk control');
        }

        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(iso) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0;
        }

        function currencyLocale(iso) {
            return usesIndianNumbering(iso) ? 'en-IN' : 'en-US';
        }

        function trimZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        function formatCompactAmount(value) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            var body;
            if (usesIndianNumbering(currencyIso)) {
                if (abs >= 10000000) { body = trimZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                else if (abs >= 100000) { body = trimZeros((number / 100000).toFixed(2)) + ' L'; }
                else if (abs >= 1000) { body = trimZeros((number / 1000).toFixed(1)) + 'K'; }
                else { body = number.toLocaleString('en-IN', { maximumFractionDigits: 2 }); }
            } else {
                if (abs >= 1000000000) { body = trimZeros((number / 1000000000).toFixed(1)) + 'B'; }
                else if (abs >= 1000000) { body = trimZeros((number / 1000000).toFixed(1)) + 'M'; }
                else if (abs >= 1000) { body = trimZeros((number / 1000).toFixed(1)) + 'K'; }
                else { body = number.toLocaleString('en-US', { maximumFractionDigits: 2 }); }
            }
            return (currencySymbol || currencyIso) + ' ' + body;
        }

        function formatFullAmount(value) {
            var number = Number(value || 0);
            var formatted = number.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return (currencySymbol || currencyIso) + ' ' + formatted;
        }

        function formatPct(value) {
            return (Math.round(Number(value || 0) * 10) / 10) + '%';
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-abc-busy-hidden', !visible); }
        }

        function messageHtml(text) {
            return '<div class="MPC-abc-msg">' + escapeHtml(text) + '</div>';
        }

        function retryHtml(text) {
            return '<div class="MPC-abc-msg">' + escapeHtml(text) +
                '<button type="button" class="MPC-abc-retry">' + escapeHtml(label('VAS_115_Retry', 'Retry')) + '</button></div>';
        }

        // ---- warehouse dropdown ----
        function loadWarehouses() {
            $select.prop('disabled', true);
            setBusy(true);
            if (warehouseRequest && warehouseRequest.readyState !== 4) { warehouseRequest.abort(); }

            warehouseRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_115_ABCAnalysisWidget/GetWarehouses',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) { showError(); return; }

                    state.warehouses = result.rows || [];
                    if (!state.warehouses.length) {
                        $select.empty().prop('disabled', true);
                        $body.html(messageHtml(label('VAS_115_NoWarehouse', 'No active warehouse is available.')));
                        setBusy(false);
                        return;
                    }

                    var options = '';
                    state.warehouses.forEach(function (warehouse) {
                        options += '<option value="' + escapeHtml(warehouse.warehouse_id) + '">' + escapeHtml(warehouse.warehouse_name) + '</option>';
                    });
                    $select.html(options).prop('disabled', false);

                    // Prefer a context warehouse when one is available, else first.
                    var contextWarehouse = resolveContextWarehouse();
                    state.warehouseId = contextWarehouse || Number(state.warehouses[0].warehouse_id);
                    $select.val(String(state.warehouseId));
                    loadABC();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                }
            });
        }

        function resolveContextWarehouse() {
            var ids = state.warehouses.map(function (w) { return Number(w.warehouse_id); });
            var candidate = 0;
            try {
                if ($self.frame && $self.frame.widgetInfo && $self.frame.widgetInfo.M_Warehouse_ID) {
                    candidate = Number($self.frame.widgetInfo.M_Warehouse_ID);
                }
                if (!candidate && VIS.context && VIS.context.GetContext) {
                    candidate = Number(VIS.context.GetContext('#M_Warehouse_ID'));
                }
            } catch (e) { candidate = 0; }
            return ids.indexOf(candidate) >= 0 ? candidate : 0;
        }

        // ---- ABC data ----
        function loadABC() {
            if (!state.warehouseId) { setBusy(false); return; }
            closeModal();
            setBusy(true);
            $body.html('');
            if (abcRequest && abcRequest.readyState !== 4) { abcRequest.abort(); }

            abcRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_115_ABCAnalysisWidget/GetABCAnalysis',
                type: 'GET',
                cache: false,
                data: { warehouseId: state.warehouseId },
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) { showError(); return; }

                    state.data = result;
                    currencySymbol = result.currency_symbol || '';
                    currencyIso = result.currency_iso || '';
                    stdPrecision = Number(result.std_precision || 0);
                    try { renderClasses(); } catch (e) { showError(); }
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () { setBusy(false); }
            });
        }

        function classByLetter(letter) {
            var classes = (state.data && state.data.classes) || [];
            for (var index = 0; index < classes.length; index++) {
                if (classes[index].cls === letter) { return classes[index]; }
            }
            return { cls: letter, sku_count: 0, sku_pct: 0, stock_value: 0 };
        }

        function renderClasses() {
            var data = state.data;
            var totalSku = Number(data.total_sku_count || 0);
            var totalValue = Number(data.total_stock_value || 0);

            // No positive on-hand stock at all.
            if (totalSku === 0) {
                $body.html(renderRows() + messageHtml(label('VAS_115_NoStock', 'No on-hand stock is available for this warehouse.')));
                return;
            }
            // Stock exists but no cost, so this is not a valid Pareto distribution.
            if (totalValue <= 0) {
                $body.html(messageHtml(label('VAS_115_NoCost', 'Current cost is not available for the selected warehouse stock.')));
                return;
            }
            $body.html(renderRows());
        }

        function renderRows() {
            var html = '<div class="MPC-abc-list">';
            ['A', 'B', 'C'].forEach(function (letter) {
                var cls = classByLetter(letter);
                var color = CLASS_COLOR[letter];
                var nominal = NOMINAL[letter];
                html +=
                    '<button type="button" class="MPC-abc-row" data-cls="' + letter + '">' +
                        '<span class="MPC-abc-badge" style="background:' + color + '">' + letter + '</span>' +
                        '<span class="MPC-abc-main">' +
                            '<span class="MPC-abc-top">' +
                                '<span class="MPC-abc-cls">' + escapeHtml(label('VAS_115_Class', 'Class') + ' ' + letter) +
                                    '<small>' + escapeHtml(formatPct(cls.sku_pct) + ' ' + label('VAS_115_OfSKUs', 'of SKUs') + ' - ' + Number(cls.sku_count || 0)) + '</small>' +
                                '</span>' +
                                '<span class="MPC-abc-val" style="color:' + color + '">' + nominal + '% ' + escapeHtml(label('VAS_115_ValueShare', 'value')) + '</span>' +
                            '</span>' +
                            '<span class="MPC-abc-bar"><span class="MPC-abc-fill" style="width:' + nominal + '%;background:' + color + '"></span></span>' +
                        '</span>' +
                    '</button>';
            });
            html += '</div>';
            return html;
        }

        function showError() {
            state.data = null;
            $body.html(retryHtml(label('VAS_CouldntLoad', "Couldn't load")));
            setBusy(false);
        }

        // ---- modal ----
        function modalIcon(name) {
            var paths = {
                close: '<line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line>',
                chevronL: '<polyline points="15 18 9 12 15 6"></polyline>',
                chevronR: '<polyline points="9 18 15 12 9 6"></polyline>'
            };
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || '') + '</svg>';
        }

        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-abc-modal" aria-hidden="true">' +
                    '<div class="MPC-abc-modal-scrim"></div>' +
                    '<div class="MPC-abc-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-abc-modal-head">' +
                            '<span class="MPC-abc-modal-title-wrap">' +
                                '<span class="MPC-abc-modal-title"></span>' +
                                '<span class="MPC-abc-modal-badge"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-abc-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-abc-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-abc-modal-title');
            $modalBadge = $modal.find('.MPC-abc-modal-badge');
            $modalBody = $modal.find('.MPC-abc-modal-body');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-abc-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-abc-modal-close, .MPC-abc-modal-scrim', closeModal);
            $modal.on('click' + modalEventNamespace, '.MPC-abc-mp-prev', function () {
                if (modalState.page > 0) { modalState.page--; renderModalPage(); }
            });
            $modal.on('click' + modalEventNamespace, '.MPC-abc-mp-next', function () {
                modalState.page++; renderModalPage();
            });
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-abc-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-abc-body-lock');
            if ($lastFocusedRow && $lastFocusedRow.length) { $lastFocusedRow.trigger('focus'); }
        }

        function openModal(letter, $row) {
            if (!state.data) { return; }
            createModal();
            $lastFocusedRow = $row || null;

            var cls = classByLetter(letter);
            modalState.page = 0;
            modalState.cls = letter;
            modalState.items = (state.data.rows || []).filter(function (row) { return row.abc_class === letter; });

            $modalTitle.text(label('VAS_115_Class', 'Class') + ' ' + letter + ' ' + label('VAS_115_ItemsTitle', 'Items'));
            $modalBadge.html('<span class="MPC-abc-pill MPC-abc-pill-' + CLASS_TONE[letter] + '">' +
                escapeHtml(NOMINAL[letter] + '% ' + label('VAS_115_OfValue', 'of value')) + '</span>');

            $modal.addClass('MPC-abc-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-abc-body-lock');
            renderModalPage();
            $modal.find('.MPC-abc-modal-close').trigger('focus');
        }

        function fieldHtml(labelText, valueText, strong) {
            return '<div class="MPC-abc-field">' +
                '<div class="MPC-abc-field-label">' + escapeHtml(labelText) + '</div>' +
                '<div class="MPC-abc-field-value' + (strong ? ' MPC-abc-strong' : '') + '">' + escapeHtml(valueText || '-') + '</div>' +
            '</div>';
        }

        function renderModalPage() {
            var letter = modalState.cls;
            var cls = classByLetter(letter);
            var items = modalState.items;
            var totalItems = items.length;
            var pages = Math.max(1, Math.ceil(totalItems / MODAL_PER_PAGE));
            if (modalState.page > pages - 1) { modalState.page = pages - 1; }
            if (modalState.page < 0) { modalState.page = 0; }

            var start = modalState.page * MODAL_PER_PAGE;
            var end = Math.min(start + MODAL_PER_PAGE, totalItems);

            var facts =
                '<div class="MPC-abc-form-grid">' +
                    fieldHtml(label('VAS_115_Class', 'Class'), letter, true) +
                    fieldHtml(label('VAS_115_Profile', 'Profile'), profileFor(letter)) +
                    fieldHtml(label('VAS_115_SKUs', 'SKUs'), Number(cls.sku_count || 0) + ' (' + formatPct(cls.sku_pct) + ')') +
                    fieldHtml(label('VAS_115_ValueLabel', 'Value'), formatFullAmount(cls.stock_value), true) +
                '</div>';

            var rowsHtml = '';
            var shownRows = 0;
            if (!totalItems) {
                rowsHtml = '<tr><td class="MPC-abc-td-empty" colspan="3">' + escapeHtml(label('VAS_115_NoItems', 'No items.')) + '</td></tr>';
                shownRows = 1;
            } else {
                for (var index = start; index < end; index++) {
                    var item = items[index];
                    rowsHtml +=
                        '<tr>' +
                            '<td class="MPC-abc-td-s">' + escapeHtml(item.sku) + '</td>' +
                            '<td class="MPC-abc-td-s">' + escapeHtml(item.product_name) + '</td>' +
                            '<td class="MPC-abc-td-r" title="' + escapeHtml(formatFullAmount(item.stock_value)) + '">' + escapeHtml(formatCompactAmount(item.stock_value)) + '</td>' +
                        '</tr>';
                    shownRows++;
                }
            }
            // Fixed modal height: pad short pages to MODAL_PER_PAGE rows so a page
            // with fewer lines does not shrink the popup (no scrolling either).
            for (var fillIndex = shownRows; fillIndex < MODAL_PER_PAGE; fillIndex++) {
                rowsHtml += '<tr class="MPC-abc-filler"><td>&nbsp;</td><td></td><td></td></tr>';
            }

            var table =
                '<div class="MPC-abc-group-head">' + escapeHtml(label('VAS_115_RepresentativeSKUs', 'Representative SKUs')) + '</div>' +
                '<table class="MPC-abc-mini-table">' +
                    '<thead><tr>' +
                        '<th>' + escapeHtml(label('VAS_115_SKU', 'SKU')) + '</th>' +
                        '<th>' + escapeHtml(label('VAS_115_Name', 'Name')) + '</th>' +
                        '<th class="MPC-abc-th-r">' + escapeHtml(label('VAS_115_StockValue', 'Stock Value')) + '</th>' +
                    '</tr></thead>' +
                    '<tbody>' + rowsHtml + '</tbody>' +
                '</table>';

            var pager = '';
            if (pages > 1) {
                pager =
                    '<div class="MPC-abc-modal-pager">' +
                        '<span class="MPC-abc-mp-info">' + escapeHtml(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '-' + end + ' ' + label('VAS_Of', 'of') + ' ' + totalItems) + '</span>' +
                        '<span class="MPC-abc-mp-ctrl">' +
                            '<button type="button" class="MPC-abc-mp-btn MPC-abc-mp-prev" ' + (modalState.page === 0 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '">' + modalIcon('chevronL') + '</button>' +
                            '<span class="MPC-abc-mp-text">' + (modalState.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages + '</span>' +
                            '<button type="button" class="MPC-abc-mp-btn MPC-abc-mp-next" ' + (modalState.page >= pages - 1 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '">' + modalIcon('chevronR') + '</button>' +
                        '</span>' +
                    '</div>';
            }

            $modalBody.html(facts + table + pager);
        }

        this.Initalize = function () {
            $card = $(
                '<div class="MPC-abc-card" aria-live="polite">' +
                    '<div class="MPC-abc-head">' +
                        '<span class="MPC-abc-ico" aria-hidden="true">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v10l8 5"></path><circle cx="12" cy="12" r="10"></circle></svg>' +
                        '</span>' +
                        '<span class="MPC-abc-titles">' +
                            '<span class="MPC-abc-title"></span>' +
                            '<span class="MPC-abc-sub"></span>' +
                        '</span>' +
                        '<span class="MPC-abc-spacer"></span>' +
                        '<select class="MPC-abc-select"></select>' +
                    '</div>' +
                    '<div class="MPC-abc-body"></div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $card.find('.MPC-abc-title').text(label('VAS_115_ABCAnalysis', 'ABC Analysis'));
            $card.find('.MPC-abc-sub').text(label('VAS_115_ABCSub', 'Value contribution by class'));
            $select = $card.find('.MPC-abc-select').attr('aria-label', label('VAS_115_Warehouse', 'Warehouse'));
            $body = $card.find('.MPC-abc-body');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $select.on('change.' + eventNamespace, function () {
                var id = Number($(this).val());
                if (!id || id === state.warehouseId) { return; }
                state.warehouseId = id;
                loadABC();
            });

            $root.on('click.' + eventNamespace, '.MPC-abc-row', function () {
                openModal($(this).attr('data-cls'), $(this));
            });
            $root.on('click.' + eventNamespace, '.MPC-abc-retry', function () {
                if (state.warehouseId) { loadABC(); } else { loadWarehouses(); }
            });

            $root.append($card);
            loadWarehouses();
        };

        this.refreshWidget = function () {
            closeModal();
            if (state.warehouseId) { loadABC(); } else { loadWarehouses(); }
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (warehouseRequest && warehouseRequest.readyState !== 4) { warehouseRequest.abort(); }
            if (abcRequest && abcRequest.readyState !== 4) { abcRequest.abort(); }
            $root.off('.' + eventNamespace);
            if ($select) { $select.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.data = null;
        };
    };

    VAS.VAS_115_ABCAnalysisWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_115_ABCAnalysisWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_115_ABCAnalysisWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_115_ABCAnalysisWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
