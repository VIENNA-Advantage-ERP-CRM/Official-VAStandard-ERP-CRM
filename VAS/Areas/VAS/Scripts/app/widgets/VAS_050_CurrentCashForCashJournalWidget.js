/**
 * Current Cash — Cash Journal
 * Purpose - Shows the latest ending balance for the selected Cash Book / Drawer.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Current cash                         | VAS_050_CurrentCash
 *  2  | Drawer                               | VAS_050_Drawer
 *  3  | Live                                 | VAS_050_Live
 *  4  | Loading                              | VAS_050_Loading
 *  5  | No data                              | VAS_050_NoData
 *  6  | Unable to load current cash          | VAS_050_LoadError
 *  7  | short of float                       | VAS_050_ShortOfFloat
 *  8  | cash on hand                         | VAS_050_CashOnHand
 *  9  | no cash left                         | VAS_050_NoCashLeft
 * 10  | Session Expired                      | VAS_050_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_050_CurrentCashForCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;
        var selectedCashBookId = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var sign = numericValue < 0 ? '-' : '';

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.addClass('is-visible'); } else { $b.removeClass('is-visible'); }
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function formatSignedAmount(value, currencySymbol, currencyISO, precision, showPositiveSign) {
            var numberValue = safeNumber(value);
            var sign = '';

            if (numberValue < 0) {
                sign = '−';
            } else if (numberValue > 0 && showPositiveSign === true) {
                sign = '+';
            }

            return sign + formatCurrencyAmount(Math.abs(numberValue), currencySymbol, currencyISO, precision);
        }

        function getStatusText(balance) {
            var numberValue = safeNumber(balance);

            if (numberValue < 0) {
                return lbl('VAS_050_ShortOfFloat', 'short of float');
            }

            if (numberValue > 0) {
                return lbl('VAS_050_CashOnHand', 'cash on hand');
            }

            return lbl('VAS_050_NoCashLeft', 'no cash left');
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

        function getTrendIcon(value) {
            var numberValue = safeNumber(value);

            if (numberValue > 0) {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="18 15 12 9 6 15"></polyline></svg>';
            }

            if (numberValue < 0) {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="6 9 12 15 18 9"></polyline></svg>';
            }

            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="6" y1="12" x2="18" y2="12"></line></svg>';
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-root',
                'id': 'VAS_050_current-cash-cash-journal-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_current-cash-cash-journal-card',
                'aria-label': lbl('VAS_050_CurrentCash', 'Current cash')
            });

            var $busy = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-busy',
                'id': 'VAS-gljtm-busy-' + widgetId,
                'text': lbl('VAS_050_Loading', 'Loading')
            });

            var $header = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-row'
            });

            var $title = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-label',
                'id': 'VAS_050_current-cash-title-' + widgetId,
                'text': lbl('VAS_050_CurrentCash', 'Current cash')
            });

            var $filter = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-filter'
            });

            var $select = $('<select>', {
                'class': 'VAS_current-cash-cash-journal-select',
                'id': 'VAS_050_current-cash-cashbook-' + widgetId,
                'aria-label': lbl('VAS_050_Drawer', 'Drawer')
            });

            var $separator = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-filter-separator',
                'text': '·'
            });

            var $live = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-live',
                'text': lbl('VAS_050_Live', 'Live')
            });

            var $value = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-value VAS_current-cash-cash-journal-value-neutral',
                'id': 'VAS_050_current-cash-value-' + widgetId,
                'text': formatCurrencyAmount(0)
            });

            var $footer = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-footer'
            });

            var $delta = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-delta VAS_current-cash-cash-journal-delta-neutral',
                'id': 'VAS_050_current-cash-delta-' + widgetId
            });

            var $deltaText = $('<span>', {
                'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                'text': formatCurrencyAmount(0)
            });

            var $description = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-description',
                'id': 'VAS_050_current-cash-description-' + widgetId,
                'text': getStatusText(0)
            });

            var $state = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-state',
                'id': 'VAS_050_current-cash-state-' + widgetId
            });

            $delta.html(getTrendIcon(0)).append($deltaText);
            $footer.append($delta).append($description);
            $filter.append($select).append($separator).append($live);
            $header.append($title).append($filter);
            $card.append($busy).append($header).append($value).append($footer).append($state);
            $root.append($card);

            $select.on('change', function () {
                selectedCashBookId = Number($(this).val() || 0);
                loadData();
            });
        }

        function renderCashBookOptions(cashBooks) {
            var widgetId = $self.AD_UserHomeWidgetID;
            var $select = $root.find('#VAS_050_current-cash-cashbook-' + widgetId);

            $select.empty();

            if (!cashBooks || !cashBooks.length) {
                $('<option>', {
                    value: '0',
                    text: lbl('VAS_050_Drawer', 'Drawer')
                }).appendTo($select);
                return;
            }

            $.each(cashBooks, function (index, cashBook) {
                $('<option>', {
                    value: cashBook.cCashBookId || cashBook.C_CashBook_ID || 0,
                    text: cashBook.name || lbl('VAS_050_Drawer', 'Drawer')
                }).appendTo($select);
            });

            if (selectedCashBookId > 0) {
                $select.val(String(selectedCashBookId));
            }
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            $root.find('#VAS_050_current-cash-state-' + widgetId)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_050_current-cash-value-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-value-positive VAS_current-cash-cash-journal-value-negative VAS_current-cash-cash-journal-value-neutral')
                .addClass('VAS_current-cash-cash-journal-value-neutral')
                .text('')
                .hide();

            $root.find('#VAS_050_current-cash-delta-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-delta-positive VAS_current-cash-cash-journal-delta-negative VAS_current-cash-cash-journal-delta-neutral')
                .addClass('VAS_current-cash-cash-journal-delta-neutral')
                .html(getTrendIcon(0))
                .append($('<span>', {
                    'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                    'text': ''
                }));

            $root.find('#VAS_050_current-cash-description-' + widgetId)
                .text('');

            $root.find('.VAS_current-cash-cash-journal-footer').hide();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var title = data.cashBookName || data.title || lbl('VAS_050_CurrentCash', 'Current cash');
            var balance = safeNumber(data.mainMetric);
            var footerAmount = data.footerAmount !== null && data.footerAmount !== undefined ? safeNumber(data.footerAmount) : Math.abs(balance);
            var valueClass = getStateClass(balance, 'VAS_current-cash-cash-journal-value');
            var deltaClass = getStateClass(balance, 'VAS_current-cash-cash-journal-delta');

            if (data.cCashBookId) {
                selectedCashBookId = Number(data.cCashBookId);
            }

            renderCashBookOptions(data.cashBooks || []);

            $root.find('#VAS_050_current-cash-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_050_current-cash-title-' + widgetId).text(title);

            $root.find('#VAS_050_current-cash-value-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-value-positive VAS_current-cash-cash-journal-value-negative VAS_current-cash-cash-journal-value-neutral')
                .addClass(valueClass)
                .text(formatSignedAmount(balance, data.currencySymbol, data.currencyISO, data.stdPrecision, false))
                .show();

            $root.find('.VAS_current-cash-cash-journal-footer').show();

            $root.find('#VAS_050_current-cash-delta-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-delta-positive VAS_current-cash-cash-journal-delta-negative VAS_current-cash-cash-journal-delta-neutral')
                .addClass(deltaClass)
                .html(getTrendIcon(balance))
                .append($('<span>', {
                    'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                    'text': formatSignedAmount(balance < 0 ? 0 - footerAmount : footerAmount, data.currencySymbol, data.currencyISO, data.stdPrecision, true)
                }));

            $root.find('#VAS_050_current-cash-description-' + widgetId)
                .text(data.description || getStatusText(balance));
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
                url: VIS.Application.contextUrl + 'VAS/VAS_050_CurrentCashForCashJournal/GetCurrentCash',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    cashBookId: selectedCashBookId || 0
                },
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_050_LoadError', 'Unable to load current cash'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_050_LoadError', 'Unable to load current cash'));
                        return;
                    }

                    if (response.hasData === false) {
                        renderCashBookOptions(response.cashBooks || []);
                        setState(lbl('VAS_050_NoData', 'No data'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_050_LoadError', 'Unable to load current cash'));
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

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
