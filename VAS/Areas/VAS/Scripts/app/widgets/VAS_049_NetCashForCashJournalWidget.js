/**
 * Net Cash — Cash Journal
 * Purpose - Shows today's net cash position from Cash Journal cash in minus cash out.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Net cash                             | VAS_049_NetCash
 *  2  | Today                                | VAS_049_Today
 *  3  | Loading                              | VAS_049_Loading
 *  4  | No data                              | VAS_049_NoData
 *  5  | Unable to load net cash              | VAS_049_LoadError
 *  6  | Positive cash day                    | VAS_049_PositiveCashDay
 *  7  | Negative cash day                    | VAS_049_NegativeCashDay
 *  8  | Neutral cash day                     | VAS_049_NeutralCashDay
 *  9  | Session Expired                      | VAS_049_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_049_NetCashForCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function getCurrencyLabel(currencySymbol, currencyISO) {
            return currencySymbol || currencyISO || '';
        }

        function getAmountParts(value, currencySymbol, currencyISO, precision, signPrefix) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            return {
                prefix: signPrefix + getCurrencyLabel(currencySymbol, currencyISO),
                main: decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount,
                decimal: decimalMatch ? decimalMatch[1] : ''
            };
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.addClass('is-visible'); } else { $b.removeClass('is-visible'); }
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function getSignPrefix(value) {
            var numberValue = safeNumber(value);
            return numberValue > 0 ? '+' : numberValue < 0 ? '−' : '';
        }

        function formatSignedAmount(value, currencySymbol, currencyISO, precision) {
            var parts = getAmountParts(Math.abs(safeNumber(value)), currencySymbol, currencyISO, precision, getSignPrefix(value));
            return parts.prefix + parts.main + parts.decimal;
        }

        function renderSignedAmount($target, value, currencySymbol, currencyISO, precision) {
            var parts = getAmountParts(Math.abs(safeNumber(value)), currencySymbol, currencyISO, precision, getSignPrefix(value));

            $target.empty()
                .append($('<span>', { 'class': 'VAS-cash-amount-prefix', 'text': parts.prefix }))
                .append($('<span>', { 'class': 'VAS-cash-amount-main', 'text': parts.main }))
                .append($('<span>', { 'class': 'VAS-cash-amount-decimal', 'text': parts.decimal }));
        }

        function getStatusText(value) {
            var numberValue = safeNumber(value);

            if (numberValue > 0) {
                return lbl('VAS_049_PositiveCashDay', 'Positive cash day');
            }

            if (numberValue < 0) {
                return lbl('VAS_049_NegativeCashDay', 'Negative cash day');
            }

            return lbl('VAS_049_NeutralCashDay', 'Neutral cash day');
        }

        function getStateClass(value, prefix) {
            var numberValue = safeNumber(value);

            if (numberValue > 0) {
                return prefix + '-positive';
            }

            if (numberValue < 0) {
                return prefix + '-negative';
            }

            return prefix + '-neutral';
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-root',
                'id': 'VAS_049_net-cash-cash-journal-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_net-cash-cash-journal-card',
                'aria-label': lbl('VAS_049_NetCash', 'Net cash')
            });

            var $busy = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-busy vis-busyindicatorouterwrap',
                'id': 'VAS-gljtm-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-row'
            });

            var $title = $('<span>', {
                'class': 'VAS_net-cash-cash-journal-label',
                'id': 'VAS_049_net-cash-title-' + widgetId,
                'text': lbl('VAS_049_NetCash', 'Net cash')
            });

            var $date = $('<span>', {
                'class': 'VAS_net-cash-cash-journal-date',
                'id': 'VAS_049_net-cash-date-' + widgetId,
                'text': lbl('VAS_049_Today', 'Today')
            });

            var $value = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-value VAS_net-cash-cash-journal-value-neutral',
                'id': 'VAS_049_net-cash-value-' + widgetId
            });

            renderSignedAmount($value, 0);

            var $footer = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-footer'
            });

            var $delta = $('<span>', {
                'class': 'VAS_net-cash-cash-journal-delta VAS_net-cash-cash-journal-delta-neutral',
                'id': 'VAS_049_net-cash-delta-' + widgetId
            });

            var $icon = $(
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="18 15 12 9 6 15"></polyline>' +
                '</svg>'
            );

            var $deltaText = $('<span>', {
                'id': 'VAS_049_net-cash-delta-text-' + widgetId,
                'text': formatSignedAmount(0)
            });

            var $description = $('<span>', {
                'class': 'VAS_net-cash-cash-journal-description',
                'id': 'VAS_049_net-cash-description-' + widgetId,
                'text': getStatusText(0)
            });

            var $state = $('<div>', {
                'class': 'VAS_net-cash-cash-journal-state',
                'id': 'VAS_049_net-cash-state-' + widgetId
            });

            $delta.append($icon).append($deltaText);
            $footer.append($delta).append($description);
            $header.append($title).append($date);
            $card.append($busy).append($header).append($value).append($footer).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            $root.find('#VAS_049_net-cash-state-' + widgetId)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_049_net-cash-value-' + widgetId)
                .removeClass('VAS_net-cash-cash-journal-value-positive VAS_net-cash-cash-journal-value-negative VAS_net-cash-cash-journal-value-neutral')
                .addClass('VAS_net-cash-cash-journal-value-neutral')
                .text('')
                .hide();

            $root.find('#VAS_049_net-cash-delta-' + widgetId)
                .removeClass('VAS_net-cash-cash-journal-delta-positive VAS_net-cash-cash-journal-delta-negative VAS_net-cash-cash-journal-delta-neutral')
                .addClass('VAS_net-cash-cash-journal-delta-neutral');

            $root.find('#VAS_049_net-cash-delta-text-' + widgetId)
                .text('');

            $root.find('#VAS_049_net-cash-description-' + widgetId)
                .text('')
                .attr('title', '');

            $root.find('.VAS_net-cash-cash-journal-footer').hide();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var title = data.title || lbl('VAS_049_NetCash', 'Net cash');
            var dateText = data.badgeText || lbl('VAS_049_Today', 'Today');
            var netAmount = safeNumber(data.mainMetric);
            var deltaAmount = safeNumber(data.deltaAmount);
            var valueClass = getStateClass(netAmount, 'VAS_net-cash-cash-journal-value');
            var deltaClass = getStateClass(deltaAmount, 'VAS_net-cash-cash-journal-delta');

            $root.find('#VAS_049_net-cash-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_049_net-cash-title-' + widgetId).text(title).attr('title', title);
            $root.find('#VAS_049_net-cash-date-' + widgetId).text(dateText).attr('title', dateText);

            $root.find('#VAS_049_net-cash-value-' + widgetId)
                .removeClass('VAS_net-cash-cash-journal-value-positive VAS_net-cash-cash-journal-value-negative VAS_net-cash-cash-journal-value-neutral')
                .addClass(valueClass)
                .show();
            renderSignedAmount($root.find('#VAS_049_net-cash-value-' + widgetId), netAmount, data.currencySymbol, data.currencyISO, data.stdPrecision);

            $root.find('.VAS_net-cash-cash-journal-footer').show();

            $root.find('#VAS_049_net-cash-delta-' + widgetId)
                .removeClass('VAS_net-cash-cash-journal-delta-positive VAS_net-cash-cash-journal-delta-negative VAS_net-cash-cash-journal-delta-neutral')
                .addClass(deltaClass);

            $root.find('#VAS_049_net-cash-delta-text-' + widgetId)
                .text(formatSignedAmount(deltaAmount, data.currencySymbol, data.currencyISO, data.stdPrecision));

            var description = data.description || getStatusText(netAmount);
            $root.find('#VAS_049_net-cash-description-' + widgetId)
                .text(description)
                .attr('title', description);
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
                url: VIS.Application.contextUrl + 'VAS/VAS_049_NetCashForCashJournalWidget/GetNetCashForCashJournal',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_049_LoadError', 'Unable to load net cash'));
                        return;
                    }

                    if (response.error === 'VAS_049_SessionExpired') {
                        setState(lbl('VAS_049_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_049_LoadError', 'Unable to load net cash'));
                        return;
                    }

                    if (response.hasData === false) {
                        setState('No data');
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_049_LoadError', 'Unable to load net cash'));
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

    VAS.VAS_049_NetCashForCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_049_NetCashForCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_049_NetCashForCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
