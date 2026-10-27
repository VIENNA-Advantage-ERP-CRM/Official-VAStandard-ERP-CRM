/**
 * Below Min Widget (KPI Card)
 * Widget number 111 - reassign on hand-off.
 * Shows the count of items whose on-hand stock is at or below the reorder
 * point (per-warehouse minimum from M_Replenish). Severity: warn.
 * Backend - VAS_111_BelowMinWidget/GetBelowMin
 * Summary Message Table
 *  # | Current Text     | Message Key
 * ---+------------------+------------------------------
 *  1 | Below Min        | VAS_111_BelowMin
 *  2 | Need attention   | VAS_111_NeedAttention
 *  3 | Couldn't load    | VAS_CouldntLoad
 * Detail dialog (click the tile) - list of products below their reorder point:
 *  4 | Below Minimum Products                       | VAS_111_DetailTitle
 *  5 | Warehouse / Product / Attribute              | VAS_111_ColWarehouse / VAS_111_ColProduct / VAS_111_ColAttribute
 *  6 | Min/Max / On Hand Qty.                       | VAS_111_ColMinMax / VAS_111_ColOnHand
 *  7 | No products are below the minimum level.     | VAS_111_NoItems
 *  8 | Close                                        | VAS_ClosePopup
 *  9 | Showing / of / Previous page / Next page     | VAS_Showing / VAS_Of / VAS_PreviousPage / VAS_NextPage
 * Backend - VAS_111_BelowMinWidget/GetBelowMinDetails
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_111_BelowMinWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-below-min-root">');
        var $value;
        var $meta;
        var request;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 0 });
        }

        function loadBelowMin() {
            if (request && request.readyState !== 4) { request.abort(); }
            $value.text('—');

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_111_BelowMinWidget/GetBelowMin',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var count = Number(result.below_min_count || 0);
                        $value.text(formatCount(count));
                        // Warn tone only when items actually need attention.
                        $value.toggleClass('MPC-below-min-warn', count > 0);
                        $meta.text(label('VAS_111_NeedAttention', 'Need attention'));
                        return;
                    }

                    showError();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                }
            });
        }

        function showError() {
            $value.text('—').removeClass('MPC-below-min-warn');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        /* ============ Detail dialog (opened by clicking the tile) ============
           Warehouse / Product / Attribute / Min-Max / On Hand Qty for every product + warehouse that is
           below its reorder point. Paged client-side, fixed row count so the dialog keeps one height. */
        var MODAL_PER_PAGE = 8;
        var modalState = { page: 0, rows: [] };
        var detailRequest;
        var $modal;
        var $modalBody;
        var modalNamespace = '.MPCBelowMinModal';

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
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
                '<div class="MPC-below-min-modal" aria-hidden="true">' +
                    '<div class="MPC-below-min-modal-scrim"></div>' +
                    '<div class="MPC-below-min-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-below-min-modal-head">' +
                            '<span class="MPC-below-min-modal-title"></span>' +
                            '<button type="button" class="MPC-below-min-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-below-min-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modal.find('.MPC-below-min-modal-title').text(label('VAS_111_DetailTitle', 'Below Minimum Products'));
            var closeText = label('VAS_ClosePopup', 'Close');
            $modal.find('.MPC-below-min-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $modalBody = $modal.find('.MPC-below-min-modal-body');
            $('body').append($modal);

            $modal.on('click' + modalNamespace, '.MPC-below-min-modal-close, .MPC-below-min-modal-scrim', closeModal);
            $modal.on('click' + modalNamespace, '.MPC-below-min-mp-prev', function () {
                if (modalState.page > 0) { modalState.page--; renderModalPage(); }
            });
            $modal.on('click' + modalNamespace, '.MPC-below-min-mp-next', function () {
                modalState.page++; renderModalPage();
            });
            $(document).on('keydown' + modalNamespace, function (event) {
                if (event.key === 'Escape' && $modal && $modal.hasClass('MPC-below-min-modal-open')) { closeModal(); }
            });
        }

        function closeModal() {
            if (detailRequest && detailRequest.readyState !== 4) { detailRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-below-min-modal-open').attr('aria-hidden', 'true');
        }

        function openModal() {
            createModal();
            modalState.page = 0;
            modalState.rows = [];
            $modalBody.html('<div class="MPC-below-min-modal-state">…</div>');
            $modal.addClass('MPC-below-min-modal-open').attr('aria-hidden', 'false');
            $modal.find('.MPC-below-min-modal-close').trigger('focus');

            if (detailRequest && detailRequest.readyState !== 4) { detailRequest.abort(); }
            detailRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_111_BelowMinWidget/GetBelowMinDetails',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) {
                        var reason = (result && result.error) ? result.error : label('VAS_CouldntLoad', "Couldn't load");
                        $modalBody.html('<div class="MPC-below-min-modal-state">' + escapeHtml(reason) + '</div>');
                        return;
                    }
                    modalState.rows = result.rows || [];
                    renderModalPage();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        $modalBody.html('<div class="MPC-below-min-modal-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                    }
                }
            });
        }

        function renderModalPage() {
            var rows = modalState.rows;
            var total = rows.length;
            var pages = Math.max(1, Math.ceil(total / MODAL_PER_PAGE));
            if (modalState.page > pages - 1) { modalState.page = pages - 1; }
            if (modalState.page < 0) { modalState.page = 0; }

            var start = modalState.page * MODAL_PER_PAGE;
            var end = Math.min(start + MODAL_PER_PAGE, total);

            // The Attribute column is shown only when at least one listed row has an attribute value.
            var showAttribute = rows.some(function (row) { return row.attribute && String(row.attribute).trim(); });
            var colCount = showAttribute ? 5 : 4;

            var body = '';
            var shown = 0;
            if (!total) {
                body = '<tr><td class="MPC-below-min-td-empty" colspan="' + colCount + '">' + escapeHtml(label('VAS_111_NoItems', 'No products are below the minimum level.')) + '</td></tr>';
                shown = 1;
            } else {
                for (var index = start; index < end; index++) {
                    var row = rows[index];
                    var productText = row.product;
                    body +=
                        '<tr>' +
                            '<td title="' + escapeHtml(row.warehouse) + '">' + escapeHtml(row.warehouse) + '</td>' +
                            '<td class="MPC-below-min-td-strong" title="' + escapeHtml(productText) + '">' + escapeHtml(productText) + '</td>' +
                            (showAttribute ? '<td title="' + escapeHtml(row.attribute) + '">' + escapeHtml(row.attribute || '') + '</td>' : '') +
                            // One value, chosen by the replenish type on the server (minimum or maximum).
                            '<td class="MPC-below-min-td-num">' + escapeHtml(formatQty(row.reorder_point)) + '</td>' +
                            '<td class="MPC-below-min-td-num MPC-below-min-td-low">' + escapeHtml(formatQty(row.on_hand)) + '</td>' +
                        '</tr>';
                    shown++;
                }
            }
            // Blank filler rows keep the dialog one constant height on a short last page.
            var fillerCells = new Array(colCount + 1).join('<td></td>');
            for (var fill = shown; fill < MODAL_PER_PAGE; fill++) {
                body += '<tr class="MPC-below-min-filler">' + fillerCells + '</tr>';
            }

            var table =
                '<table class="MPC-below-min-table">' +
                    '<thead><tr>' +
                        '<th style="width:' + (showAttribute ? '24%' : '30%') + '">' + escapeHtml(label('VAS_111_ColWarehouse', 'Warehouse')) + '</th>' +
                        '<th style="width:' + (showAttribute ? '32%' : '44%') + '">' + escapeHtml(label('VAS_111_ColProduct', 'Product')) + '</th>' +
                        (showAttribute ? '<th style="width:18%">' + escapeHtml(label('VAS_111_ColAttribute', 'Attribute')) + '</th>' : '') +
                        '<th class="MPC-below-min-th-num" style="width:' + (showAttribute ? '13%' : '13%') + '">' + escapeHtml(label('VAS_111_ColMinMax', 'Min/Max')) + '</th>' +
                        '<th class="MPC-below-min-th-num" style="width:' + (showAttribute ? '13%' : '13%') + '">' + escapeHtml(label('VAS_111_ColOnHand', 'On Hand Qty.')) + '</th>' +
                    '</tr></thead>' +
                    '<tbody>' + body + '</tbody>' +
                '</table>';

            // Pager only when there is more than one page.
            var pager = '';
            if (pages > 1) {
                pager =
                    '<div class="MPC-below-min-modal-pager">' +
                        '<span class="MPC-below-min-mp-info">' + escapeHtml(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '-' + end + ' ' + label('VAS_Of', 'of') + ' ' + total) + '</span>' +
                        '<span class="MPC-below-min-mp-ctrl">' +
                            '<button type="button" class="MPC-below-min-mp-btn MPC-below-min-mp-prev" ' + (modalState.page === 0 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '">' + modalIcon('chevronL') + '</button>' +
                            '<span class="MPC-below-min-mp-text">' + (modalState.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages + '</span>' +
                            '<button type="button" class="MPC-below-min-mp-btn MPC-below-min-mp-next" ' + (modalState.page >= pages - 1 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '">' + modalIcon('chevronR') + '</button>' +
                        '</span>' +
                    '</div>';
            }

            $modalBody.html(table + pager);
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-below-min-card" role="button" tabindex="0" aria-live="polite">' +
                    '<div class="MPC-below-min-label"></div>' +
                    '<div class="MPC-below-min-value">—</div>' +
                    '<div class="MPC-below-min-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-below-min-label').text(label('VAS_111_BelowMin', 'Below Min'));
            $value = $card.find('.MPC-below-min-value');
            $meta = $card.find('.MPC-below-min-meta');

            // Clicking (or Enter / Space on) the tile opens the below-minimum product list.
            $card.on('click', openModal);
            $card.on('keydown', function (event) {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); openModal(); }
            });

            $root.append($card);
            loadBelowMin();
        };

        this.refreshWidget = function () {
            loadBelowMin();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            closeModal();
            $(document).off('keydown' + modalNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
        };
    };

    VAS.VAS_111_BelowMinWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_111_BelowMinWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_111_BelowMinWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_111_BelowMinWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
