/**
 * Reconciliation status
 * Purpose - Shows the percentage of outgoing AP payments matched to bills and bank reconciliation.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Reconciliation status                | VAS_046_ReconciliationStatus
 *  2  | Matched to bills + bank              | VAS_046_MatchedToBillsBank
 *  3  | Matched                              | VAS_046_Matched
 *  4  | {0} auto-matched                     | VAS_046_PercentageAutoMatched
 *  5  | {0} payments need manual match       | VAS_046_PaymentsNeedManualMatch
 *  6  | Review unmatched                     | VAS_046_ReviewUnmatched
 *  7  | Loading                              | VAS_046_Loading
 *  8  | No Data                              | VAS_046_NoData
 *  9  | Could not load data                  | VAS_ErrorLoading
 * 10  | Matched payments                     | VAS_046_MatchedPayments
 * 11  | Need manual match                    | VAS_046_NeedManualMatch
 * 12  | Total payments                       | VAS_046_TotalPayments
 * 13  | Period                               | VAS_046_Period
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_046_ReconciliationStatusWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-reconciliation-status-root">');
        var $card;
        var $body;
        var $state;
        var $busy;
        var $donut;
        var $progress;
        var $percent;
        var $tooltip;
        var $strong;
        var $subText;
        var isDisposed = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-reconciliation-status-card">');

            var $head = $('<div class="vas-reconciliation-status-head">');
            var $headLeft = $('<div class="vas-reconciliation-status-head-left">');
            var $titleRow = $('<div class="vas-reconciliation-status-title-row">');
            var $iconBox = $('<span class="vas-reconciliation-status-icon-box">');
            var $icon = $(
                '<svg class="vas-reconciliation-status-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<circle cx="12" cy="12" r="6"></circle>' +
                '<circle cx="12" cy="12" r="2"></circle>' +
                '</svg>'
            );

            var $headText = $('<div class="vas-reconciliation-status-head-text">');
            var $title = $('<div class="vas-reconciliation-status-title">').text(lbl('VAS_046_ReconciliationStatus', 'Reconciliation status'));
            var $sub = $('<div class="vas-reconciliation-status-sub">').text(lbl('VAS_046_MatchedToBillsBank', 'Matched to bills + bank'));

            $iconBox.append($icon);
            $headText.append($title).append($sub);
            $titleRow.append($iconBox).append($headText);
            $headLeft.append($titleRow);
            $head.append($headLeft);

            $body = $('<div class="vas-reconciliation-status-body">');

            $donut = $('<div class="vas-reconciliation-status-donut" tabindex="0" role="img">');
            var $svg = $(
                '<svg viewBox="0 0 140 140" aria-hidden="true" focusable="false">' +
                '<circle class="vas-reconciliation-status-track" cx="70" cy="70" r="56"></circle>' +
                '<circle class="vas-reconciliation-status-progress" cx="70" cy="70" r="56" pathLength="100" stroke-dasharray="0 100" stroke-dashoffset="0"></circle>' +
                '</svg>'
            );

            $progress = $svg.find('.vas-reconciliation-status-progress');

            var $center = $('<div class="vas-reconciliation-status-center">');
            $percent = $('<span class="vas-reconciliation-status-percent">');
            var $cap = $('<span class="vas-reconciliation-status-cap">').text(lbl('VAS_046_Matched', 'Matched'));

            $center.append($percent).append($cap);
            $donut.append($svg).append($center);

            var $meta = $('<div class="vas-reconciliation-status-meta">');
            $strong = $('<div class="vas-reconciliation-status-strong">');
            $subText = $('<div class="vas-reconciliation-status-subtext">');

            //var $action = $('<button type="button" class="vas-reconciliation-status-action">');
            //var $actionIcon = $(
            //    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            //    '<line x1="7" y1="17" x2="17" y2="7"></line>' +
            //    '<polyline points="7 7 17 7 17 17"></polyline>' +
            //    '</svg>'
            //);

            //$action.append(document.createTextNode(lbl('VAS_046_ReviewUnmatched', 'Review unmatched'))).append($actionIcon);
            $meta.append($strong).append($subText);

            $body.append($donut).append($meta);
            $state = $('<div class="vas-reconciliation-status-state">');
            $busy = $('<div class="vas-reconciliation-status-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $tooltip = $('<div class="vas-reconciliation-status-tooltip" role="tooltip">');

            $card.append($head).append($body).append($busy).append($state).append($tooltip);
            $root.empty().append($card);

            $donut.on('mouseenter focus', function (event) {
                showTooltip(event);
            });

            $donut.on('mousemove', positionTooltip);

            $donut.on('mouseleave blur', hideTooltip);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_046_ReconciliationStatusWidget/GetReconciliationStatus',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                            return;
                        }
                    }

                    if (!data || data.error) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    if (!isDisposed) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                    }
                },
                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function renderData(data) {
            var percentage = Number(data.matchedPercentage);
            var manualCount = Number(data.manualMatchCount || 0);

            if (isNaN(percentage) || percentage < 0) {
                percentage = 0;
            }

            if (percentage > 100) {
                percentage = 100;
            }

            var percentageText = formatPercentage(percentage);

            showState(false, '');

            $percent.text(percentageText);
            $progress.attr('stroke-dasharray', percentage + ' 100');

            $strong.text(lbl('VAS_046_PercentageAutoMatched', '{0} auto-matched').replace('{0}', percentageText));
            $subText.text(lbl('VAS_046_PaymentsNeedManualMatch', '{0} payments need manual match').replace('{0}', manualCount.toLocaleString(window.navigator.language)));

            renderTooltip(data, percentageText, manualCount);
        }

        function renderTooltip(data, percentageText, manualCount) {
            if (!$tooltip || !$donut) {
                return;
            }

            var matchedCount = Number(data.matchedPayments || 0);
            var totalCount = Number(data.totalPayments || 0);
            var locale = window.navigator.language;
            var period = '';

            if (data.dateFrom && data.dateTo) {
                period = data.dateFrom + ' - ' + data.dateTo;
            }

            $tooltip.empty()
                .append($('<div class="vas-reconciliation-status-tooltip-title">').text(
                    lbl('VAS_046_Matched', 'Matched') + ' · ' + percentageText
                ))
                .append(createTooltipRow(
                    lbl('VAS_046_MatchedPayments', 'Matched payments'),
                    matchedCount.toLocaleString(locale)
                ))
                .append(createTooltipRow(
                    lbl('VAS_046_NeedManualMatch', 'Need manual match'),
                    manualCount.toLocaleString(locale)
                ))
                .append(createTooltipRow(
                    lbl('VAS_046_TotalPayments', 'Total payments'),
                    totalCount.toLocaleString(locale)
                ));

            if (period) {
                $tooltip.append(createTooltipRow(
                    lbl('VAS_046_Period', 'Period'),
                    period
                ));
            }

            $donut.attr('aria-label',
                lbl('VAS_046_Matched', 'Matched') + ' ' + percentageText + '. ' +
                lbl('VAS_046_MatchedPayments', 'Matched payments') + ': ' + matchedCount.toLocaleString(locale) + '. ' +
                lbl('VAS_046_NeedManualMatch', 'Need manual match') + ': ' + manualCount.toLocaleString(locale) + '. ' +
                lbl('VAS_046_TotalPayments', 'Total payments') + ': ' + totalCount.toLocaleString(locale)
            );
        }

        function createTooltipRow(label, value) {
            return $('<div class="vas-reconciliation-status-tooltip-row">')
                .append($('<span>').text(label))
                .append($('<strong>').text(value));
        }

        function showTooltip(event) {
            if (!$tooltip || !$tooltip.children().length) {
                return;
            }

            $tooltip.addClass('is-visible');
            positionTooltip(event);
        }

        function positionTooltip(event) {
            if (!$tooltip || !$card || !$tooltip.hasClass('is-visible')) {
                return;
            }

            var cardRect = $card[0].getBoundingClientRect();
            var tooltipWidth = $tooltip.outerWidth();
            var tooltipHeight = $tooltip.outerHeight();
            var x;
            var y;

            if (event && event.type === 'mousemove') {
                x = event.clientX - cardRect.left + 12;
                y = event.clientY - cardRect.top + 12;
            }
            else {
                var donutRect = $donut[0].getBoundingClientRect();
                x = donutRect.right - cardRect.left + 8;
                y = donutRect.top - cardRect.top + (donutRect.height - tooltipHeight) / 2;
            }

            x = Math.max(8, Math.min(x, cardRect.width - tooltipWidth - 8));
            y = Math.max(8, Math.min(y, cardRect.height - tooltipHeight - 8));

            $tooltip.css({ left: x + 'px', top: y + 'px' });
        }

        function hideTooltip() {
            if ($tooltip) {
                $tooltip.removeClass('is-visible');
            }
        }

        function formatPercentage(value) {
            var numericValue = Number(value || 0);

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }) + '%';
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var stdPrecision = getStdPrecision();

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return stdPrecision;
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state) {
                $state.text(message || '').toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }
        }

        function setNoData() {
            showState(true, lbl('VAS_046_NoData', 'No Data'));
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $root.remove();
            $card = null;
            $body = null;
            $state = null;
            $busy = null;
            $donut = null;
            $progress = null;
            $percent = null;
            $tooltip = null;
            $strong = null;
            $subText = null;
        };
    };

    VAS.VAS_046_ReconciliationStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_046_ReconciliationStatusWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_046_ReconciliationStatusWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_046_ReconciliationStatusWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
