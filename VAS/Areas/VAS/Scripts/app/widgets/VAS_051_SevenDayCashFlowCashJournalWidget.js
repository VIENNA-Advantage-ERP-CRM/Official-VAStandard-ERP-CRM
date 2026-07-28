/**
 * 7-Day Cash Flow - Cash Journal
 * Purpose - Shows cash in vs cash out for the last 7 days including today.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | 7-Day Cash Flow                      | VAS_051_SevenDayCashFlow
 *  2  | IN VS OUT                            | VAS_051_InVsOut
 *  3  | In                                   | VAS_051_CashIn
 *  4  | Out                                  | VAS_051_CashOut
 *  5  | Loading                              | VAS_051_Loading
 *  6  | No data                              | VAS_051_NoData
 *  7  | Unable to load seven day cash flow   | VAS_051_LoadError
 *  8  | Last 7 days                          | VAS_051_Last7Days
 *  9  | Session Expired                      | VAS_051_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    /**
     * Creates a single ResizeObserver that monitors the dashboard container's
     * width. Whenever the container is resized, it updates the global CSS
     * variable '--dash-inline-size' with the container's current width (in px),
     * so the widget title/header clamp() tracks the dashboard width rather than
     * the viewport. One observer per document is sufficient — every widget reads
     * the same var.
     */
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

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return stdPrecision;
        }

        /* Prefer the currency symbol; fall back to the ISO code when no
           symbol is available (IQD's ISO renders as the short 'ID'). */
        function getCurrencyLabel(currencySymbol, currencyISO) {
            var symbol = String(currencySymbol || '').trim();
            if (symbol) {
                return symbol;
            }

            var iso = String(currencyISO || '').trim();
            return iso.toUpperCase() === 'IQD' ? 'ID' : iso;
        }

        /*
         * Compact, locale-aware magnitude via VIS.Util.formatCompactAmount
         * (K/L/Cr or M/B tiers by base currency). The formatter returns a
         * non-negative magnitude, so we compose the final string ourselves:
         * sign, then currency, then magnitude — a negative renders as e.g.
         * -$200.56 (leading minus, before the symbol).
         */
        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = safeNumber(value);
            var sign = numericValue < 0 ? '-' : '';
            var currency = getCurrencyLabel(currencySymbol, currencyISO);
            var magnitude = VIS.Util.formatCompactAmount(numericValue, currencyISO, getPrecision(precision));

            return sign + currency + magnitude;
        }

        function showBusy(show) {
            if (!$root) {
                return;
            }

            var $busy = $root.find('#VAS_051_cash-flow-busy-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $busy.addClass('is-visible');
            }
            else {
                $busy.removeClass('is-visible');
            }
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
                'class': 'VAS_051_cash-flow-busy vis-busyindicatorouterwrap',
                'id': 'VAS_051_cash-flow-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_051_cash-flow-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_051_cash-flow-title-row'
            });

            var $icon = $(
                '<span class="VAS_051_cash-flow-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M3 17l6-6 4 4 8-8"></path>' +
                '<path d="M14 7h7v7"></path>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_051_cash-flow-title',
                'id': 'VAS_051_cash-flow-title-' + widgetId,
                'text': lbl('VAS_051_SevenDayCashFlow', '7-Day Cash Flow')
            });

            var $legend = $('<div>', {
                'class': 'VAS_051_cash-flow-legend',
                'id': 'VAS_051_cash-flow-legend-' + widgetId
            });

            $legend
                .append($('<span>', {
                    'class': 'VAS_051_cash-flow-legend-item VAS_051_cash-flow-legend-in',
                    'text': lbl('VAS_051_CashIn', 'In')
                }))
                .append($('<span>', {
                    'class': 'VAS_051_cash-flow-legend-item VAS_051_cash-flow-legend-out',
                    'text': lbl('VAS_051_CashOut', 'Out')
                }));

            var $body = $('<div>', {
                'class': 'VAS_051_cash-flow-body VAS_051_cash-flow-seven-body',
                'id': 'VAS_051_cash-flow-body-' + widgetId
            });

            var $state = $('<div>', {
                'class': 'VAS_051_cash-flow-state',
                'id': 'VAS_051_cash-flow-state-' + widgetId
            });

            $titleRow.append($icon).append($title);
            $header.append($titleRow).append($legend);
            $card.append($busy).append($header).append($body).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            $root.find('#VAS_051_cash-flow-state-' + widgetId).text(message || '').addClass('is-visible');
            $root.find('#VAS_051_cash-flow-body-' + widgetId).empty();
        }

        function createDayColumn(item, maxAmount, data) {
            var cashIn = safeNumber(item.cashInAmount);
            var cashOut = safeNumber(item.cashOutAmount);
            var currencySymbol = item.currencySymbol || data.currencySymbol;
            var currencyISO = item.currencyISO || data.currencyISO;
            var precision = item.stdPrecision || data.stdPrecision;

            var inHeight = maxAmount > 0 ? Math.round((cashIn / maxAmount) * 100) : 0;
            var outHeight = maxAmount > 0 ? Math.round((cashOut / maxAmount) * 100) : 0;

            if (cashIn === 0) {
                inHeight = 0;
            }

            if (cashOut === 0) {
                outHeight = 0;
            }

            var tooltip = (item.dayLabel || '') + ' ' + (item.date || '') +
                ' | In: ' + formatCurrencyAmount(cashIn, currencySymbol, currencyISO, precision) +
                ' | Out: ' + formatCurrencyAmount(cashOut, currencySymbol, currencyISO, precision);

            var $column = $('<div>', {
                'class': 'VAS_051_cash-flow-day',
                'title': tooltip
            });

            var $bars = $('<div>', {
                'class': 'VAS_051_cash-flow-bars'
            });

            var $inBar = $('<div>', {
                'class': 'VAS_051_cash-flow-day-bar VAS_051_cash-flow-day-in'
            }).css('height', inHeight + '%');

            var $outBar = $('<div>', {
                'class': 'VAS_051_cash-flow-day-bar VAS_051_cash-flow-day-out'
            }).css('height', outHeight + '%');

            var $label = $('<div>', {
                'class': 'VAS_051_cash-flow-day-label',
                'text': item.dayLabel || '',
                'title': item.dayLabel || ''
            });

            $bars.append($inBar).append($outBar);
            $column.append($bars).append($label);

            return $column;
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var items = data.items || [];
            var $body = $root.find('#VAS_051_cash-flow-body-' + widgetId);

            $root.find('#VAS_051_cash-flow-state-' + widgetId).removeClass('is-visible').text('');
            var title = data.title || lbl('VAS_051_SevenDayCashFlow', '7-Day Cash Flow');
            $root.find('#VAS_051_cash-flow-title-' + widgetId).text(title).attr('title', title);

            $body.empty();

            if (data.hasData === false || items.length === 0) {
                setState('No data');
                return;
            }

            var maxAmount = 0;

            $.each(items, function (index, item) {
                maxAmount = Math.max(
                    maxAmount,
                    safeNumber(item.cashInAmount),
                    safeNumber(item.cashOutAmount)
                );
            });

            $.each(items.slice(0, 7), function (index, item) {
                $body.append(createDayColumn(item, maxAmount, data));
            });
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
                        setState(lbl('VAS_051_LoadError', 'Unable to load seven day cash flow'));
                        return;
                    }

                    if (response.error === 'VAS_051_SessionExpired') {
                        setState(lbl('VAS_051_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_051_LoadError', 'Unable to load seven day cash flow'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_051_LoadError', 'Unable to load seven day cash flow'));
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

        ensureDashInlineSizeVar(this.getRoot());
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
