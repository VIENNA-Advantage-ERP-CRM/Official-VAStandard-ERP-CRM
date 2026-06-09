/**
 * Today Cash Out — Cash Journal
 * Purpose - Shows today's cash disbursement amount from negative Cash Journal lines.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Cash out                             | VAS_048_CashOut
 *  2  | Today                                | VAS_048_Today
 *  3  | Loading                              | VAS_048_Loading
 *  4  | No data                              | VAS_048_NoData
 *  5  | Unable to load cash out              | VAS_048_LoadError
 *  6  | vs 7-day avg                         | VAS_048_VsSevenDayAvg
 *  7  | disbursements                        | VAS_048_Disbursements
 *  8  | Session Expired                      | VAS_048_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_048_TodayCashOutCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.addClass('is-visible'); } else { $b.removeClass('is-visible'); }
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function formatPercent(value) {
            var numberValue = safeNumber(value);
            var sign = numberValue > 0 ? '+' : '';
            return sign + numberValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            }) + '%';
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-root',
                'id': 'VAS_048_today-cash-out-cash-journal-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_today-cash-out-cash-journal-card',
                'aria-label': lbl('VAS_048_CashOut', 'Cash out')
            });

            var $busy = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-busy',
                'id': 'VAS-gljtm-busy-' + widgetId,
                'text': lbl('VAS_048_Loading', 'Loading')
            });

            var $header = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-row'
            });

            var $title = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-label',
                'id': 'VAS_048_today-cash-out-title-' + widgetId,
                'text': lbl('VAS_048_CashOut', 'Cash out')
            });

            var $date = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-date',
                'id': 'VAS_048_today-cash-out-date-' + widgetId,
                'text': lbl('VAS_048_Today', 'Today')
            });

            var $value = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-value',
                'id': 'VAS_048_today-cash-out-value-' + widgetId,
                'text': formatCurrencyAmount(0)
            });

            var $footer = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-footer'
            });

            var $delta = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-delta VAS_today-cash-out-cash-journal-delta-down',
                'id': 'VAS_048_today-cash-out-delta-' + widgetId
            });

            var $icon = $(
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="6 9 12 15 18 9"></polyline>' +
                '</svg>'
            );

            var $deltaText = $('<span>', {
                'id': 'VAS_048_today-cash-out-delta-text-' + widgetId,
                'text': formatPercent(0)
            });

            var $description = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-description',
                'id': 'VAS_048_today-cash-out-description-' + widgetId,
                'text': lbl('VAS_048_VsSevenDayAvg', 'vs 7-day avg') + ' · 0 ' + lbl('VAS_048_Disbursements', 'disbursements')
            });

            var $state = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-state',
                'id': 'VAS_048_today-cash-out-state-' + widgetId
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

            $root.find('#VAS_048_today-cash-out-state-' + $self.AD_UserHomeWidgetID)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_048_today-cash-out-value-' + $self.AD_UserHomeWidgetID)
                .text('')
                .hide();

            $root.find('#VAS_048_today-cash-out-delta-text-' + $self.AD_UserHomeWidgetID)
                .text('');

            $root.find('#VAS_048_today-cash-out-description-' + $self.AD_UserHomeWidgetID)
                .text('');

            $root.find('.VAS_today-cash-out-cash-journal-footer').hide();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var title = data.title || lbl('VAS_048_CashOut', 'Cash out');
            var dateText = data.badgeText || lbl('VAS_048_Today', 'Today');
            var amount = safeNumber(data.mainMetric);
            var deltaPercent = safeNumber(data.deltaPercent);
            var disbursementCount = safeNumber(data.disbursementCount);
            var footerText = lbl('VAS_048_VsSevenDayAvg', 'vs 7-day avg') + ' · ' + disbursementCount.toLocaleString(window.navigator.language) + ' ' + lbl('VAS_048_Disbursements', 'disbursements');

            $root.find('#VAS_048_today-cash-out-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_048_today-cash-out-title-' + widgetId).text(title);
            $root.find('#VAS_048_today-cash-out-date-' + widgetId).text(dateText);
            $root.find('#VAS_048_today-cash-out-value-' + widgetId).text(formatCurrencyAmount(amount, data.currencySymbol, data.currencyISO)).show();
            $root.find('.VAS_today-cash-out-cash-journal-footer').show();
            $root.find('#VAS_048_today-cash-out-delta-text-' + widgetId).text(formatPercent(deltaPercent));
            $root.find('#VAS_048_today-cash-out-description-' + widgetId).text(footerText);

            var $delta = $root.find('#VAS_048_today-cash-out-delta-' + widgetId);
            $delta.removeClass('VAS_today-cash-out-cash-journal-delta-up VAS_today-cash-out-cash-journal-delta-down');

            if (deltaPercent > 0) {
                $delta.addClass('VAS_today-cash-out-cash-journal-delta-up');
            } else {
                $delta.addClass('VAS_today-cash-out-cash-journal-delta-down');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_048_TodayCashOutCashJournal/GetTodayCashOut',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_048_LoadError', 'Unable to load cash out'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_048_LoadError', 'Unable to load cash out'));
                        return;
                    }

                    if (response.hasData === false) {
                        setState(lbl('VAS_048_NoData', 'No data'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_048_LoadError', 'Unable to load cash out'));
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

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
