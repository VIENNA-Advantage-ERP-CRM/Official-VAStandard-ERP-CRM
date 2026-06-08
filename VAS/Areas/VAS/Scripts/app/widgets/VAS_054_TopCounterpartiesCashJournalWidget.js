/**
 * Top Counterparties - Cash Journal
 * Purpose - Shows top net counterparties from cash journal lines.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Top Counterparties                   | VAS_054_TopCounterparties
 *  2  | Last 30 Days                         | VAS_054_Last30Days
 *  3  | All parties ->                       | VAS_054_AllParties
 *  4  | No counterparties found              | VAS_054_NoData
 *  5  | Unable to load top counterparties    | VAS_054_LoadError
 *  6  | Unknown                              | VAS_054_Unknown
 *  7  | Bank                                 | VAS_054_Bank
 *  8  | Cashbook                             | VAS_054_Cashbook
 *  9  | Customer                             | VAS_054_Customer
 * 10  | Vendor                               | VAS_054_Vendor
 * 11  | Other                                | VAS_054_Other
 * 12  | Loading                              | VAS_054_Loading
 * 13  | Session Expired                      | VAS_054_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_054_TopCounterpartiesCashJournalWidget = function () {
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

        function formatAmount(item) {
            var precision = Number(item && item.stdPrecision);
            var symbol = item && item.currencySymbol ? item.currencySymbol : '';

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            return symbol + safeNumber(item && item.displayAmount).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function showBusy(show) {
            var $busy = $root.find('#VAS_054_counterparties-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $busy.show(); } else { $busy.hide(); }
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_054_counterparties-root',
                'id': 'VAS_054_counterparties-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_054_counterparties-card',
                'aria-label': lbl('VAS_054_TopCounterparties', 'Top Counterparties')
            });

            var $busy = $('<div>', {
                'class': 'VAS_054_counterparties-busy',
                'id': 'VAS_054_counterparties-busy-' + widgetId,
                'text': lbl('VAS_054_Loading', 'Loading')
            }).hide();

            var $header = $('<div>', {
                'class': 'VAS_054_counterparties-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_054_counterparties-title-row'
            });

            var $icon = $(
                '<span class="VAS_054_counterparties-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"></path>' +
                '<circle cx="9" cy="7" r="4"></circle>' +
                '<path d="M22 21v-2a4 4 0 0 0-3-3.87"></path>' +
                '<path d="M16 3.13a4 4 0 0 1 0 7.75"></path>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_054_counterparties-title',
                'id': 'VAS_054_counterparties-title-' + widgetId,
                'text': lbl('VAS_054_TopCounterparties', 'Top Counterparties')
            });

            var $meta = $('<span>', {
                'class': 'VAS_054_counterparties-meta',
                'id': 'VAS_054_counterparties-meta-' + widgetId,
                'text': lbl('VAS_054_Last30Days', 'Last 30 Days')
            });

            //var $action = $('<button>', {
            //    'class': 'VAS_054_counterparties-action',
            //    'type': 'button',
            //    'id': 'VAS_054_counterparties-action-' + widgetId,
            //    'text': lbl('VAS_054_AllParties', 'All parties ->')
            //});

            var $list = $('<div>', {
                'class': 'VAS_054_counterparties-list',
                'id': 'VAS_054_counterparties-list-' + widgetId
            });

            var $state = $('<div>', {
                'class': 'VAS_054_counterparties-state',
                'id': 'VAS_054_counterparties-state-' + widgetId
            }).hide();

            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow);
            $card.append($busy).append($header).append($list).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            $root.find('#VAS_054_counterparties-state-' + widgetId).text(message || '').show();
            $root.find('#VAS_054_counterparties-list-' + widgetId).empty();
        }

        function buildSparkline(trend) {
            var points = trend === 'down'
                ? '2,8 12,11 22,7 32,12 42,8 52,13 62,15'
                : trend === 'flat'
                    ? '2,10 12,10 22,10 32,10 42,10 52,10 62,10'
                    : '2,14 12,11 22,13 32,8 42,10 52,6 62,4';

            return '<svg class="VAS_054_counterparties-sparkline VAS_054_counterparties-sparkline-' + (trend || 'flat') + '" viewBox="0 0 64 20" preserveAspectRatio="none" aria-hidden="true">' +
                '<polyline points="' + points + '"></polyline>' +
                '</svg>';
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var items = data.items || [];
            var $list = $root.find('#VAS_054_counterparties-list-' + widgetId);

            $root.find('#VAS_054_counterparties-state-' + widgetId).hide().text('');
            $root.find('#VAS_054_counterparties-title-' + widgetId).text(data.title || lbl('VAS_054_TopCounterparties', 'Top Counterparties'));
            $root.find('#VAS_054_counterparties-meta-' + widgetId).text(data.metaText || lbl('VAS_054_Last30Days', 'Last 30 Days'));
            $root.find('#VAS_054_counterparties-action-' + widgetId).text(data.actionText || lbl('VAS_054_AllParties', 'All parties ->'));

            $list.empty();

            if (!items.length) {
                setState(data.noDataText || lbl('VAS_054_NoData', 'No counterparties found'));
                return;
            }

            $.each(items, function (index, item) {
                var $row = $('<div>', {
                    'class': 'VAS_054_counterparties-row'
                });

                var $avatar = $('<span>', {
                    'class': 'VAS_054_counterparties-avatar VAS_054_counterparties-avatar-' + (item.typeClass || 'other'),
                    'text': item.initials || '--'
                });

                var $name = $('<div>', {
                    'class': 'VAS_054_counterparties-name',
                    'text': item.name || '-'
                });

                var $chip = $('<span>', {
                    'class': 'VAS_054_counterparties-chip VAS_054_counterparties-chip-' + (item.typeClass || 'other'),
                    'text': item.typeText || ''
                });

                var $count = $('<span>', {
                    'class': 'VAS_054_counterparties-count',
                    'text': safeNumber(item.entryCount).toLocaleString(window.navigator.language)
                });

                var $spark = $('<span>', {
                    'class': 'VAS_054_counterparties-spark-wrap',
                    'html': buildSparkline(item.trend)
                });

                var $amount = $('<span>', {
                    'class': 'VAS_054_counterparties-amount',
                    'text': formatAmount(item)
                });

                $row.append($avatar).append($name).append($chip).append($count).append($spark).append($amount);
                $list.append($row);
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
                url: VIS.Application.contextUrl + 'VAS/VAS_054_TopCounterpartiesCashJournal/GetTopCounterparties',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_054_LoadError', 'Unable to load top counterparties'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_054_LoadError', 'Unable to load top counterparties'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_054_LoadError', 'Unable to load top counterparties'));
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

    VAS.VAS_054_TopCounterpartiesCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_054_TopCounterpartiesCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_054_TopCounterpartiesCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
