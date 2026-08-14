/**
 * Category Mix Widget
 * Widget number 113 - reassign on hand-off.
 * Ranked share of active items per product category (name, share bar, %,
 * count), paginated; clicking a category opens a drill-in modal listing its
 * items (SKU, Name, On Hand, Stock Value), itself paginated.
 * Backend - VAS_113_CategoryMixWidget/GetCategoryMix, GetCategoryItems
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+--------------------------------
 *  1 | Category Mix                          | VAS_113_CategoryMix
 *  2 | Share of active items - tap to drill in | VAS_113_CategoryMixSub
 *  3 | items                                 | VAS_113_Items
 *  4 | Showing                               | VAS_Showing
 *  5 | of                                    | VAS_Of
 *  6 | No categories found.                  | VAS_113_NoCategories
 *  7 | Couldn't load                         | VAS_CouldntLoad
 *  8 | Category Detail                       | VAS_113_CategoryDetail
 *  9 | Category                              | VAS_113_Category
 * 10 | Active items                          | VAS_113_ActiveItemsLabel
 * 11 | Share                                 | VAS_113_Share
 * 12 | Sample                                | VAS_113_Sample
 * 13 | Items in this category                | VAS_113_ItemsInCategory
 * 14 | SKU                                   | VAS_113_SKU
 * 15 | Name                                  | VAS_113_Name
 * 16 | On Hand                               | VAS_113_OnHand
 * 17 | Stock Value                           | VAS_113_StockValue
 * 18 | Previous page / Next page             | VAS_PreviousPage / VAS_NextPage
 * 19 | Close                                 | Close
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_113_CategoryMixWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-cm-root">');
        var $card;
        var $rows;
        var $empty;
        var $footHelper;
        var $pageText;
        var $prevButton;
        var $nextButton;
        var $footer;
        var $busy;
        var request;
        var itemsRequest;
        var $modal;
        var $modalTitle;
        var $modalBadge;
        var $modalBody;
        var modalEventNamespace = '.MPCCmModal';
        var eventNamespace = 'MPCCategoryMix';

        var LIST_PER_PAGE = 4;
        var MODAL_PER_PAGE = 6;
        // Per-category bar colours, assigned by rank (design palette).
        var PALETTE = ['#0083DA', '#1F83FF', '#20A464', '#5F4AA6', '#D78B10', '#106AB0', '#D14545'];

        var state = { page: 0, categories: [], total: 0 };
        var modalState = { page: 0, category: null, items: [] };
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        // Compact money: Indian-numbering currencies get Lakh/Cr, others K/M/B.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(iso) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0;
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

        function sharePct(category) {
            if (!state.total) { return 0; }
            return category.active_items / state.total * 100;
        }

        function render() {
            var total = state.categories.length;
            if (!total) {
                $rows.empty().addClass('MPC-cm-hidden');
                $footer.addClass('MPC-cm-hidden');
                $empty.removeClass('MPC-cm-hidden').text(label('VAS_113_NoCategories', 'No categories found.'));
                return;
            }

            var pages = Math.ceil(total / LIST_PER_PAGE);
            if (state.page > pages - 1) { state.page = pages - 1; }
            if (state.page < 0) { state.page = 0; }

            var start = state.page * LIST_PER_PAGE;
            var end = Math.min(start + LIST_PER_PAGE, total);

            $empty.addClass('MPC-cm-hidden');
            $rows.removeClass('MPC-cm-hidden');
            $footer.removeClass('MPC-cm-hidden');

            var html = '';
            for (var index = start; index < end; index++) {
                var category = state.categories[index];
                var pct = sharePct(category);
                var pctText = Math.round(pct) + '%';
                var width = Math.max(2, Math.min(100, pct));
                html +=
                    '<button type="button" class="MPC-cm-row" data-index="' + index + '">' +
                        '<span class="MPC-cm-l">' +
                            '<span class="MPC-cm-name" title="' + escapeHtml(category.name) + '">' + escapeHtml(category.name) + '</span>' +
                            '<span class="MPC-cm-bar"><span class="MPC-cm-fill" style="width:' + width + '%;background:' + category.color + '"></span></span>' +
                        '</span>' +
                        '<span class="MPC-cm-r">' +
                            '<span class="MPC-cm-pct">' + escapeHtml(pctText) + '</span>' +
                            '<span class="MPC-cm-cnt">' + escapeHtml(formatQty(category.active_items) + ' ' + label('VAS_113_Items', 'items')) + '</span>' +
                        '</span>' +
                    '</button>';
            }
            $rows.html(html);

            $footHelper.text(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '-' + end + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages);
            $prevButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page >= pages - 1);
        }

        function showError() {
            state.categories = [];
            $rows.empty().addClass('MPC-cm-hidden');
            $footer.addClass('MPC-cm-hidden');
            $empty.removeClass('MPC-cm-hidden').text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-cm-busy-hidden', !visible); }
        }

        function loadCategories() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_113_CategoryMixWidget/GetCategoryMix',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) { showError(); return; }

                    state.page = 0;
                    state.total = Number(result.total_active_items || 0);
                    state.categories = (result.rows || []).map(function (row, index) {
                        row.color = PALETTE[index % PALETTE.length];
                        return row;
                    });
                    render();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () { setBusy(false); }
            });
        }

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
                '<div class="MPC-cm-modal" aria-hidden="true">' +
                    '<div class="MPC-cm-modal-scrim"></div>' +
                    '<div class="MPC-cm-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-cm-modal-head">' +
                            '<span class="MPC-cm-modal-title-wrap">' +
                                '<span class="MPC-cm-modal-title"></span>' +
                                '<span class="MPC-cm-modal-badge"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-cm-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-cm-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-cm-modal-title');
            $modalBadge = $modal.find('.MPC-cm-modal-badge');
            $modalBody = $modal.find('.MPC-cm-modal-body');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-cm-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-cm-modal-close, .MPC-cm-modal-scrim', closeModal);
            $modal.on('click' + modalEventNamespace, '.MPC-cm-mp-prev', function () {
                if (modalState.page > 0) { modalState.page--; renderModalPage(); }
            });
            $modal.on('click' + modalEventNamespace, '.MPC-cm-mp-next', function () {
                modalState.page++; renderModalPage();
            });
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function closeModal() {
            if (itemsRequest && itemsRequest.readyState !== 4) { itemsRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-cm-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-cm-body-lock');
        }

        function openModal(index) {
            var category = state.categories[index];
            if (!category) { return; }

            createModal();
            modalState.page = 0;
            modalState.category = category;
            modalState.items = [];

            $modalTitle.text(category.name + ' - ' + label('VAS_113_CategoryDetail', 'Category Detail'));
            $modalBadge.html('<span class="MPC-cm-pill MPC-cm-pill-info">' +
                escapeHtml(formatQty(category.active_items) + ' ' + label('VAS_113_Items', 'items')) + '</span>');
            $modalBody.html('<div class="MPC-cm-modal-state">…</div>');
            $modal.addClass('MPC-cm-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-cm-body-lock');
            $modal.find('.MPC-cm-modal-close').trigger('focus');

            if (itemsRequest && itemsRequest.readyState !== 4) { itemsRequest.abort(); }
            itemsRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_113_CategoryMixWidget/GetCategoryItems',
                type: 'GET',
                cache: false,
                data: { categoryId: category.category_id },
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) {
                        var reason = (result && result.error) ? result.error : label('VAS_CouldntLoad', "Couldn't load");
                        $modalBody.html('<div class="MPC-cm-modal-state">' + escapeHtml(reason) + '</div>');
                        return;
                    }
                    currencySymbol = result.currency_symbol || '';
                    currencyIso = result.currency_iso || '';
                    stdPrecision = Number(result.std_precision || 0);
                    modalState.items = result.rows || [];
                    try {
                        renderModalPage();
                    } catch (e) {
                        $modalBody.html('<div class="MPC-cm-modal-state">' + escapeHtml('Render error: ' + (e && e.message ? e.message : e)) + '</div>');
                    }
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        $modalBody.html('<div class="MPC-cm-modal-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                    }
                }
            });
        }

        function fieldHtml(labelText, valueText, strong) {
            return '<div class="MPC-cm-field">' +
                '<div class="MPC-cm-field-label">' + escapeHtml(labelText) + '</div>' +
                '<div class="MPC-cm-field-value' + (strong ? ' MPC-cm-strong' : '') + '">' + escapeHtml(valueText || '-') + '</div>' +
            '</div>';
        }

        function renderModalPage() {
            var category = modalState.category;
            if (!category) { return; }

            var items = modalState.items;
            var totalItems = items.length;
            var pages = Math.max(1, Math.ceil(totalItems / MODAL_PER_PAGE));
            if (modalState.page > pages - 1) { modalState.page = pages - 1; }
            if (modalState.page < 0) { modalState.page = 0; }

            var start = modalState.page * MODAL_PER_PAGE;
            var end = Math.min(start + MODAL_PER_PAGE, totalItems);

            var showStockColumns = !totalItems || items.some(function (item) {
                return !item.product_type || item.product_type === 'I';
            });
            var colCount = showStockColumns ? 4 : 2;

            var rowsHtml = '';
            var shownRows = 0;
            if (!totalItems) {
                rowsHtml = '<tr><td class="MPC-cm-td-empty" colspan="' + colCount + '">' + escapeHtml(label('VAS_113_NoCategories', 'No items.')) + '</td></tr>';
                shownRows = 1;
            } else {
                for (var index = start; index < end; index++) {
                    var item = items[index];
                    var isItem = !item.product_type || item.product_type === 'I';
                    rowsHtml +=
                        '<tr>' +
                            '<td class="MPC-cm-td-s">' + escapeHtml(item.sku) + '</td>' +
                            '<td class="MPC-cm-td-s" title="' + escapeHtml(item.name) + '">' + escapeHtml(item.name) + '</td>';
                    if (showStockColumns) {
                        if (isItem) {
                            rowsHtml +=
                                '<td class="MPC-cm-td-r">' + escapeHtml(formatQty(item.on_hand)) + '</td>' +
                                '<td class="MPC-cm-td-r" title="' + escapeHtml(formatFullAmount(item.stock_value)) + '">' + escapeHtml(formatCompactAmount(item.stock_value)) + '</td>';
                        } else {
                            rowsHtml +=
                                '<td class="MPC-cm-td-r">—</td>' +
                                '<td class="MPC-cm-td-r">—</td>';
                        }
                    }
                    rowsHtml += '</tr>';
                    shownRows++;
                }
            }

            for (var fillIndex = shownRows; fillIndex < MODAL_PER_PAGE; fillIndex++) {
                if (showStockColumns) {
                    rowsHtml += '<tr class="MPC-cm-filler"><td>&nbsp;</td><td></td><td></td><td></td></tr>';
                } else {
                    rowsHtml += '<tr class="MPC-cm-filler"><td>&nbsp;</td><td></td></tr>';
                }
            }

            var thead = '<thead><tr>' +
                '<th style="width:' + (showStockColumns ? '22%' : '30%') + '">' + escapeHtml(label('Code', 'Code')) + '</th>' +
                '<th style="width:' + (showStockColumns ? '44%' : '70%') + '">' + escapeHtml(label('VAS_113_Name', 'Name')) + '</th>';
            if (showStockColumns) {
                thead +=
                    '<th class="MPC-cm-th-r" style="width:17%">' + escapeHtml(label('VAS_113_OnHand', 'On Hand')) + '</th>' +
                    '<th class="MPC-cm-th-r" style="width:17%">' + escapeHtml(label('VAS_113_StockValue', 'Stock Value')) + '</th>';
            }
            thead += '</tr></thead>';

            var table =
                '<div class="MPC-cm-group-head">' + escapeHtml(label('VAS_113_ItemsInCategory', 'Items in this category')) + '</div>' +
                '<table class="MPC-cm-mini-table">' +
                    thead +
                    '<tbody>' + rowsHtml + '</tbody>' +
                '</table>';

            var pager = '';
            if (pages > 1) {
                pager =
                    '<div class="MPC-cm-modal-pager">' +
                        '<span class="MPC-cm-mp-info">' + escapeHtml(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '-' + end + ' ' + label('VAS_Of', 'of') + ' ' + totalItems) + '</span>' +
                        '<span class="MPC-cm-mp-ctrl">' +
                            '<button type="button" class="MPC-cm-mp-btn MPC-cm-mp-prev" ' + (modalState.page === 0 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '">' + modalIcon('chevronL') + '</button>' +
                            '<span class="MPC-cm-mp-text">' + (modalState.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages + '</span>' +
                            '<button type="button" class="MPC-cm-mp-btn MPC-cm-mp-next" ' + (modalState.page >= pages - 1 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '">' + modalIcon('chevronR') + '</button>' +
                        '</span>' +
                    '</div>';
            }

            $modalBody.html(table + pager);
        }

        this.Initalize = function () {
            $card = $(
                '<div class="MPC-cm-card" aria-live="polite">' +
                    '<div class="MPC-cm-head">' +
                        '<span class="MPC-cm-ico" aria-hidden="true">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polygon points="12 2 2 7 12 12 22 7 12 2"></polygon>' +
                                '<polyline points="2 17 12 22 22 17"></polyline>' +
                                '<polyline points="2 12 12 17 22 12"></polyline>' +
                            '</svg>' +
                        '</span>' +
                        '<span class="MPC-cm-titles">' +
                            '<span class="MPC-cm-title"></span>' +
                            '<span class="MPC-cm-sub"></span>' +
                        '</span>' +
                    '</div>' +
                    '<div class="MPC-cm-body">' +
                        '<div class="MPC-cm-empty MPC-cm-hidden"></div>' +
                        '<div class="MPC-cm-list"></div>' +
                        '<div class="MPC-cm-foot MPC-cm-hidden">' +
                            '<span class="MPC-cm-foot-helper"></span>' +
                            '<span class="MPC-cm-pager">' +
                                '<button type="button" class="MPC-cm-pgbtn MPC-cm-prev">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                                '</button>' +
                                '<span class="MPC-cm-pgtext"></span>' +
                                '<button type="button" class="MPC-cm-pgbtn MPC-cm-next">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                                '</button>' +
                            '</span>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $card.find('.MPC-cm-title').text(label('VAS_113_CategoryMix', 'Category Mix'));
            $card.find('.MPC-cm-sub').text(label('VAS_113_CategoryMixSub', 'Share of active items - tap to drill in'));
            $rows = $card.find('.MPC-cm-list');
            $empty = $card.find('.MPC-cm-empty');
            $footHelper = $card.find('.MPC-cm-foot-helper');
            $pageText = $card.find('.MPC-cm-pgtext');
            $footer = $card.find('.MPC-cm-foot');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            var previousLabel = label('VAS_PreviousPage', 'Previous page');
            var nextLabel = label('VAS_NextPage', 'Next page');
            $prevButton = $card.find('.MPC-cm-prev').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-cm-next').attr({ 'aria-label': nextLabel, title: nextLabel });

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page === 0) { return; }
                state.page--;
                render();
            });
            $nextButton.on('click.' + eventNamespace, function () {
                state.page++;
                render();
            });
            $root.on('click.' + eventNamespace, '.MPC-cm-row', function () {
                openModal(Number($(this).attr('data-index')));
            });

            $root.append($card);
            loadCategories();
        };

        this.refreshWidget = function () {
            closeModal();
            loadCategories();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (request && request.readyState !== 4) { request.abort(); }
            if (itemsRequest && itemsRequest.readyState !== 4) { itemsRequest.abort(); }
            $root.off('.' + eventNamespace);
            if ($prevButton) { $prevButton.off('.' + eventNamespace); }
            if ($nextButton) { $nextButton.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.categories = [];
            modalState.items = [];
        };
    };

    VAS.VAS_113_CategoryMixWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_113_CategoryMixWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_113_CategoryMixWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_113_CategoryMixWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
