/**
 * Moving Analysis Widget (Top Moving / Slow Moving)
 * Widget number 117 - reassign on hand-off.
 * Summary Message Table
 *  # | Current Text                                   | Message Key
 * ---+------------------------------------------------+--------------------------------
 *  1 | Top Moving / Slow Moving                       | VAS_117_Title
 *  2 | Stock velocity, last 30d                       | VAS_117_Sub
 *  3 | Fast                                           | VAS_117_Fast
 *  4 | Slow                                           | VAS_117_Slow
 *  5 | /day                                           | VAS_117_PerDay
 *  6 | /{0}d                                          | VAS_117_PerWindow
 *  7 | turns                                          | VAS_117_Turns
 *  8 | Highest issue activity - fast movers           | VAS_117_FastHelper
 *  9 | No / low issue activity - slow movers          | VAS_117_SlowHelper
 * 10 | No movement data found.                        | VAS_117_NoData
 * 11 | Couldn't load                                  | VAS_CouldntLoad
 * 12 | Velocity                                       | VAS_117_Velocity
 * 13 | Fast mover                                      | VAS_117_FastMover
 * 14 | Slow mover                                      | VAS_117_SlowMover
 * 15 | Product                                        | VAS_117_Product
 * 16 | SKU                                            | VAS_117_SKU
 * 17 | Issue velocity                                 | VAS_117_IssueVelocity
 * 18 | Annual turns                                   | VAS_117_AnnualTurns
 * 19 | On hand                                        | VAS_117_OnHand
 * 20 | Window                                         | VAS_117_Window
 * 21 | Last {0} days                                  | VAS_117_LastNDays
 * 22 | Close                                          | Close
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_117_MovingAnalysisWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-mv-root">');
        var $card;
        var $rows;
        var $empty;
        var $footHelper;
        var $segFast;
        var $segSlow;
        var $busy;
        var request;
        var $modal;
        var $modalTitle;
        var $modalBadge;
        var $modalBody;
        var modalEventNamespace = '.MPCMvModal';
        var eventNamespace = 'MPCMovingAnalysis';
        var state = { mode: 'fast', fast: [], slow: [], fastWindow: 30, slowWindow: 90 };

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function format(key, fallback, token) {
            return label(key, fallback).replace('{0}', token);
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
        }

        function formatTurns(value) {
            // One-decimal turnover ratio with the conventional "x" suffix (e.g. 11.2x).
            return Number(value || 0).toFixed(1) + 'x';
        }

        // Issue-velocity display: fast movers read per day ("42 /day"); slow
        // movers read the raw count over the longer window ("0 /90d").
        function velocityText(row, slow) {
            if (slow) {
                return formatQty(row.issued_slow) + ' ' + format('VAS_117_PerWindow', '/{0}d', state.slowWindow);
            }
            return formatQty(row.per_day) + ' ' + label('VAS_117_PerDay', '/day');
        }

        // Bar magnitude per list: fast bars scale with per-day velocity; slow bars
        // scale with idle on-hand so the biggest dead-stock piles read fullest.
        function barMetric(row, slow) {
            return Number((slow ? row.on_hand : row.per_day) || 0);
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return {
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;'
                }[character];
            });
        }

        function render() {
            var slow = state.mode === 'slow';
            var rows = slow ? state.slow : state.fast;

            $segFast.toggleClass('MPC-mv-active', !slow);
            $segSlow.toggleClass('MPC-mv-active', slow);

            if (!rows.length) {
                $rows.empty().addClass('MPC-mv-hidden');
                $empty.removeClass('MPC-mv-hidden').text(label('VAS_117_NoData', 'No movement data found.'));
                $footHelper.text(slow
                    ? label('VAS_117_SlowHelper', 'No / low issue activity - slow movers')
                    : label('VAS_117_FastHelper', 'Highest issue activity - fast movers'));
                return;
            }

            $empty.addClass('MPC-mv-hidden');
            $rows.removeClass('MPC-mv-hidden');

            var maxMetric = 0;
            rows.forEach(function (row) { maxMetric = Math.max(maxMetric, barMetric(row, slow)); });

            var html = '';
            rows.forEach(function (row, index) {
                var width = maxMetric > 0 ? Math.max(4, Math.round(barMetric(row, slow) / maxMetric * 100)) : 4;
                html +=
                    '<button type="button" class="MPC-mv-row" data-index="' + index + '">' +
                        '<span class="MPC-mv-main">' +
                            '<span class="MPC-mv-name" title="' + escapeHtml(row.product_name) + '">' + escapeHtml(row.product_name) + '</span>' +
                            '<span class="MPC-mv-bar"><span class="MPC-mv-fill ' + (slow ? 'MPC-mv-fill-slow' : 'MPC-mv-fill-fast') + '" style="width:' + width + '%"></span></span>' +
                        '</span>' +
                        '<span class="MPC-mv-r">' +
                            '<span class="MPC-mv-vel ' + (slow ? 'MPC-mv-vel-slow' : 'MPC-mv-vel-fast') + '">' + escapeHtml(velocityText(row, slow)) + '</span>' +
                            '<span class="MPC-mv-turns">' + escapeHtml(formatTurns(slow ? row.turns_slow : row.turns_fast) + ' ' + label('VAS_117_Turns', 'turns')) + '</span>' +
                        '</span>' +
                    '</button>';
            });
            $rows.html(html);

            $footHelper.text(slow
                ? label('VAS_117_SlowHelper', 'No / low issue activity - slow movers')
                : label('VAS_117_FastHelper', 'Highest issue activity - fast movers'));
        }

        function showError() {
            state.fast = [];
            state.slow = [];
            $rows.empty().addClass('MPC-mv-hidden');
            $empty.removeClass('MPC-mv-hidden').text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-mv-busy-hidden', !visible); }
        }

        function loadData() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_117_MovingAnalysisWidget/GetMovingAnalysis',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) {
                        showError();
                        return;
                    }

                    state.fast = result.fast || [];
                    state.slow = result.slow || [];
                    state.fastWindow = Number(result.fast_window_days) || 30;
                    state.slowWindow = Number(result.slow_window_days) || 90;
                    render();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () {
                    setBusy(false);
                }
            });
        }

        function modalIcon(name) {
            var paths = {
                close: '<line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line>'
            };
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || '') + '</svg>';
        }

        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-mv-modal" aria-hidden="true">' +
                    '<div class="MPC-mv-modal-scrim"></div>' +
                    '<div class="MPC-mv-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-mv-modal-head">' +
                            '<span class="MPC-mv-modal-title-wrap">' +
                                '<span class="MPC-mv-modal-title"></span>' +
                                '<span class="MPC-mv-modal-badge"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-mv-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-mv-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-mv-modal-title');
            $modalBadge = $modal.find('.MPC-mv-modal-badge');
            $modalBody = $modal.find('.MPC-mv-modal-body');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-mv-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-mv-modal-close, .MPC-mv-modal-scrim', closeModal);
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-mv-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-mv-body-lock');
        }

        function fieldHtml(labelText, valueText, strong) {
            return '<div class="MPC-mv-field">' +
                '<div class="MPC-mv-field-label">' + escapeHtml(labelText) + '</div>' +
                '<div class="MPC-mv-field-value' + (strong ? ' MPC-mv-strong' : '') + '">' + escapeHtml(valueText || '-') + '</div>' +
            '</div>';
        }

        function openModal(row, slow) {
            createModal();

            $modalTitle.text((row.product_name || '') + ' - ' + label('VAS_117_Velocity', 'Velocity'));
            $modalBadge.html('<span class="MPC-mv-pill ' + (slow ? 'MPC-mv-pill-warn' : 'MPC-mv-pill-ok') + '">' +
                escapeHtml(slow ? label('VAS_117_SlowMover', 'Slow mover') : label('VAS_117_FastMover', 'Fast mover')) + '</span>');

            var uom = row.uom_name || '';
            var onHandText = formatQty(row.on_hand) + (uom ? ' ' + uom : '');
            var windowDays = slow ? state.slowWindow : state.fastWindow;

            $modalBody.html(
                '<div class="MPC-mv-form-grid">' +
                    fieldHtml(label('VAS_117_Product', 'Product'), row.product_name, true) +
                    fieldHtml(label('VAS_117_SKU', 'SKU'), row.sku) +
                    fieldHtml(label('VAS_117_IssueVelocity', 'Issue velocity'), velocityText(row, slow)) +
                    fieldHtml(label('VAS_117_AnnualTurns', 'Annual turns'), formatTurns(slow ? row.turns_slow : row.turns_fast)) +
                    fieldHtml(label('VAS_117_OnHand', 'On hand'), onHandText) +
                    fieldHtml(label('VAS_117_Window', 'Window'), format('VAS_117_LastNDays', 'Last {0} days', windowDays)) +
                '</div>'
            );

            $modal.addClass('MPC-mv-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-mv-body-lock');
            $modal.find('.MPC-mv-modal-close').trigger('focus');
        }

        this.Initalize = function () {
            $card = $(
                '<div class="MPC-mv-card" aria-live="polite">' +
                    '<div class="MPC-mv-head">' +
                        '<span class="MPC-mv-ico" aria-hidden="true">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>' +
                            '</svg>' +
                        '</span>' +
                        '<span class="MPC-mv-titles">' +
                            '<span class="MPC-mv-title"></span>' +
                            '<span class="MPC-mv-sub"></span>' +
                        '</span>' +
                        '<span class="MPC-mv-spacer"></span>' +
                        '<span class="MPC-mv-seg">' +
                            '<button type="button" class="MPC-mv-seg-btn MPC-mv-seg-fast"></button>' +
                            '<button type="button" class="MPC-mv-seg-btn MPC-mv-seg-slow"></button>' +
                        '</span>' +
                    '</div>' +
                    '<div class="MPC-mv-body">' +
                        '<div class="MPC-mv-empty MPC-mv-hidden"></div>' +
                        '<div class="MPC-mv-list"></div>' +
                        '<div class="MPC-mv-foot"><span class="MPC-mv-foot-helper"></span></div>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $card.find('.MPC-mv-title').text(label('VAS_117_Title', 'Top Moving / Slow Moving'));
            $card.find('.MPC-mv-sub').text(label('VAS_117_Sub', 'Stock velocity, last 30d'));
            $segFast = $card.find('.MPC-mv-seg-fast').text(label('VAS_117_Fast', 'Fast'));
            $segSlow = $card.find('.MPC-mv-seg-slow').text(label('VAS_117_Slow', 'Slow'));
            $rows = $card.find('.MPC-mv-list');
            $empty = $card.find('.MPC-mv-empty');
            $footHelper = $card.find('.MPC-mv-foot-helper');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $segFast.on('click.' + eventNamespace, function () {
                if (state.mode === 'fast') { return; }
                state.mode = 'fast';
                render();
            });
            $segSlow.on('click.' + eventNamespace, function () {
                if (state.mode === 'slow') { return; }
                state.mode = 'slow';
                render();
            });

            $root.on('click.' + eventNamespace, '.MPC-mv-row', function () {
                var slow = state.mode === 'slow';
                var rows = slow ? state.slow : state.fast;
                var row = rows[Number($(this).attr('data-index'))];
                if (row) { openModal(row, slow); }
            });

            $root.append($card);
            loadData();
        };

        this.refreshWidget = function () {
            closeModal();
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            closeModal();
            if (request && request.readyState !== 4) { request.abort(); }
            $root.off('.' + eventNamespace);
            if ($segFast) { $segFast.off('.' + eventNamespace); }
            if ($segSlow) { $segSlow.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.fast = [];
            state.slow = [];
        };
    };

    VAS.VAS_117_MovingAnalysisWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_117_MovingAnalysisWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_117_MovingAnalysisWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_117_MovingAnalysisWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
