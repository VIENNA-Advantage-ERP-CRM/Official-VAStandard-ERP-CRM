/**
 * 7-Day Cash Flow - Cash Journal
 * Purpose - Shows cash in vs cash out for the last seven days including today.
 *
 * Labels / Message Keys
 *  1 | 7-Day Cash Flow                 | VAS_051_SevenDayCashFlow
 *  2 | In vs Out                       | VAS_051_InVsOut
 *  3 | In                              | VAS_051_In
 *  4 | Out                             | VAS_051_Out
 *  5 | Loading                         | VAS_051_Loading
 *  6 | No data                         | VAS_051_NoData
 *  7 | Unable to load 7-day cash flow  | VAS_051_LoadError
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_051_SevenDayCashFlowCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function getPrecision() {
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatAmount(value, currencySymbol) {
            return (currencySymbol || '') + safeNumber(value).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getPrecision(),
                maximumFractionDigits: getPrecision()
            });
        }

        function showBusy(show) {
            var $busy = $root.find('#VAS_051_cash-flow-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $busy.show(); } else { $busy.hide(); }
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_051_cash-flow-root',
                'id': 'VAS_051_cash-flow-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_051_cash-flow-card',
                'aria-label': lbl('VAS_051_SevenDayCashFlow', '7-Day Cash Flow')
            });

            var $busy = $('<div>', {
                'class': 'VAS_051_cash-flow-busy',
                'id': 'VAS_051_cash-flow-busy-' + widgetId,
                'text': lbl('VAS_051_Loading', 'Loading')
            }).hide();

            var $header = $('<div>', {
                'class': 'VAS_051_cash-flow-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_051_cash-flow-title-row'
            });

            var $icon = $(
                '<span class="VAS_051_cash-flow-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="23 6 13.5 15.5 8.5 10.5 1 18"></polyline>' +
                '<polyline points="17 6 23 6 23 12"></polyline>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_051_cash-flow-title',
                'id': 'VAS_051_cash-flow-title-' + widgetId,
                'text': lbl('VAS_051_SevenDayCashFlow', '7-Day Cash Flow')
            });

            var $meta = $('<span>', {
                'class': 'VAS_051_cash-flow-meta',
                'id': 'VAS_051_cash-flow-meta-' + widgetId,
                'text': lbl('VAS_051_InVsOut', 'In vs Out')
            });

            var $legend = $('<div>', {
                'class': 'VAS_051_cash-flow-legend'
            });

            var $inLegend = $('<span>', {
                'class': 'VAS_051_cash-flow-legend-item',
                'html': '<span class="VAS_051_cash-flow-dot VAS_051_cash-flow-dot-in"></span><span id="VAS_051_cash-flow-in-label-' + widgetId + '">' + lbl('VAS_051_In', 'In') + '</span>'
            });

            var $outLegend = $('<span>', {
                'class': 'VAS_051_cash-flow-legend-item',
                'html': '<span class="VAS_051_cash-flow-dot VAS_051_cash-flow-dot-out"></span><span id="VAS_051_cash-flow-out-label-' + widgetId + '">' + lbl('VAS_051_Out', 'Out') + '</span>'
            });

            var $body = $('<div>', {
                'class': 'VAS_051_cash-flow-body'
            });

            var $chart = $('<div>', {
                'class': 'VAS_051_cash-flow-chart',
                'id': 'VAS_051_cash-flow-chart-' + widgetId,
                'role': 'img',
                'aria-label': 'Bar chart of cash in vs cash out for the past seven days'
            });

            var $tooltip = $('<div>', {
                'class': 'VAS_051_cash-flow-tooltip',
                'id': 'VAS_051_cash-flow-tooltip-' + widgetId
            }).hide();

            var $state = $('<div>', {
                'class': 'VAS_051_cash-flow-state',
                'id': 'VAS_051_cash-flow-state-' + widgetId
            }).hide();

            $legend.append($inLegend).append($outLegend);
            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow).append($legend);
            $body.append($chart).append($tooltip);
            $card.append($busy).append($header).append($body).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            $root.find('#VAS_051_cash-flow-state-' + widgetId).text(message || '').show();
            $root.find('#VAS_051_cash-flow-chart-' + widgetId).empty();
        }

        function renderChart(days, currencySymbol) {
            var widgetId = $self.AD_UserHomeWidgetID;
            var maxAmount = 0;

            $.each(days, function (index, day) {
                maxAmount = Math.max(maxAmount, safeNumber(day.cashInAmount), safeNumber(day.cashOutAmount));
            });

            maxAmount = maxAmount > 0 ? maxAmount : 1;

            var width = 600;
            var height = 200;
            var chartTop = 18;
            var chartBottom = 166;
            var barMaxHeight = chartBottom - chartTop;
            var groupWidth = width / 7;
            var barWidth = 22;
            var barGap = 6;
            var html = [
                '<svg viewBox="0 0 600 200" preserveAspectRatio="none" aria-hidden="true">'
            ];

            for (var gridIndex = 1; gridIndex <= 4; gridIndex++) {
                var gridY = chartTop + (barMaxHeight / 4) * gridIndex;
                html.push('<line x1="0" y1="' + gridY.toFixed(2) + '" x2="600" y2="' + gridY.toFixed(2) + '" class="VAS_051_cash-flow-grid"></line>');
            }

            $.each(days, function (index, day) {
                var groupX = index * groupWidth;
                var centerX = groupX + (groupWidth / 2);
                var inAmount = safeNumber(day.cashInAmount);
                var outAmount = safeNumber(day.cashOutAmount);
                var inHeight = Math.max(2, (inAmount / maxAmount) * barMaxHeight);
                var outHeight = Math.max(2, (outAmount / maxAmount) * barMaxHeight);
                var inX = centerX - barWidth - (barGap / 2);
                var outX = centerX + (barGap / 2);
                var inY = chartBottom - inHeight;
                var outY = chartBottom - outHeight;
                var label = day.dayLabel || '';
                var date = day.date || '';

                html.push('<g class="VAS_051_cash-flow-day" data-day="' + label + '" data-date="' + date + '" data-in="' + inAmount + '" data-out="' + outAmount + '">');
                html.push('<rect class="VAS_051_cash-flow-bar VAS_051_cash-flow-bar-in" x="' + inX.toFixed(2) + '" y="' + inY.toFixed(2) + '" width="' + barWidth + '" height="' + inHeight.toFixed(2) + '" rx="3"></rect>');
                html.push('<rect class="VAS_051_cash-flow-bar VAS_051_cash-flow-bar-out" x="' + outX.toFixed(2) + '" y="' + outY.toFixed(2) + '" width="' + barWidth + '" height="' + outHeight.toFixed(2) + '" rx="3"></rect>');
                html.push('<text class="VAS_051_cash-flow-axis-label" x="' + centerX.toFixed(2) + '" y="194" text-anchor="middle">' + label + '</text>');
                html.push('</g>');
            });

            html.push('</svg>');

            $root.find('#VAS_051_cash-flow-chart-' + widgetId).html(html.join(''));
            bindTooltip(currencySymbol);
        }

        function bindTooltip(currencySymbol) {
            var widgetId = $self.AD_UserHomeWidgetID;
            var $chart = $root.find('#VAS_051_cash-flow-chart-' + widgetId);
            var $tooltip = $root.find('#VAS_051_cash-flow-tooltip-' + widgetId);

            $chart.find('.VAS_051_cash-flow-day')
                .off('mouseenter.VAS051 mousemove.VAS051 mouseleave.VAS051')
                .on('mouseenter.VAS051 mousemove.VAS051', function (event) {
                    var $day = $(this);
                    var inText = formatAmount($day.data('in'), currencySymbol);
                    var outText = formatAmount($day.data('out'), currencySymbol);
                    var tooltipHtml = '<strong>' + ($day.data('day') || '') + '</strong>' +
                        '<span><i class="VAS_051_cash-flow-dot VAS_051_cash-flow-dot-in"></i>' + lbl('VAS_051_In', 'In') + ': ' + inText + '</span>' +
                        '<span><i class="VAS_051_cash-flow-dot VAS_051_cash-flow-dot-out"></i>' + lbl('VAS_051_Out', 'Out') + ': ' + outText + '</span>';

                    $tooltip.html(tooltipHtml).show();

                    var chartOffset = $chart.offset();
                    var left = event.pageX - chartOffset.left + 12;
                    var top = event.pageY - chartOffset.top - 10;

                    $tooltip.css({
                        left: left + 'px',
                        top: top + 'px'
                    });
                })
                .on('mouseleave.VAS051', function () {
                    $tooltip.hide();
                });
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var days = data.days || [];

            $root.find('#VAS_051_cash-flow-state-' + widgetId).hide().text('');
            $root.find('#VAS_051_cash-flow-title-' + widgetId).text(data.title || lbl('VAS_051_SevenDayCashFlow', '7-Day Cash Flow'));
            $root.find('#VAS_051_cash-flow-meta-' + widgetId).text(data.metaText || lbl('VAS_051_InVsOut', 'In vs Out'));
            $root.find('#VAS_051_cash-flow-in-label-' + widgetId).text(data.inLabel || lbl('VAS_051_In', 'In'));
            $root.find('#VAS_051_cash-flow-out-label-' + widgetId).text(data.outLabel || lbl('VAS_051_Out', 'Out'));

            renderChart(days, data.currencySymbol);

            if (data.hasData === false) {
                $root.find('#VAS_051_cash-flow-state-' + widgetId).text(lbl('VAS_051_NoData', 'No data')).show();
            }
        }

        function loadData() {
            if (!$root || isDisposed) {
                return;
            }

            if (ajaxRequest && ajaxRequest.readyState !== 4) {
                ajaxRequest.abort();
            }

            showBusy(true);

            ajaxRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_051_SevenDayCashFlowCashJournal/GetSevenDayCashFlow',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_051_LoadError', 'Unable to load 7-day cash flow'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_051_LoadError', 'Unable to load 7-day cash flow'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_051_LoadError', 'Unable to load 7-day cash flow'));
                    }
                },
                complete: function () {
                    if (!isDisposed && $root) {
                        showBusy(false);
                    }
                }
            });
        }

        this.initalize = function () {
            buildLayout();
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (ajaxRequest && ajaxRequest.readyState !== 4) {
                ajaxRequest.abort();
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            ajaxRequest = null;
            $root = null;
            $self = null;
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_051_SevenDayCashFlowCashJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        if (!this.AD_UserHomeWidgetID) {
            this.AD_UserHomeWidgetID = windowNo || new Date().getTime();
        }

        this.initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_051_SevenDayCashFlowCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_051_SevenDayCashFlowCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
