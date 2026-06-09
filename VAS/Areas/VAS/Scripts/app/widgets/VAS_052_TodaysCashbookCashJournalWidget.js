/**
 * Today's Cashbook - Cash Journal
 * Purpose - Shows today's cashbook entries from Cash Journal lines.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Today's Cashbook                     | VAS_052_TodaysCashbook
 *  2  | entries                              | VAS_052_Entries
 *  3  | + Entry                              | VAS_052_Entry
 *  4  | Time                                 | VAS_052_Time
 *  5  | Description                          | VAS_052_Description
 *  6  | Category                             | VAS_052_Category
 *  7  | Posted by                            | VAS_052_PostedBy
 *  8  | In                                   | VAS_052_In
 *  9  | Out                                  | VAS_052_Out
 * 10  | Other                                | VAS_052_Other
 * 11  | System                               | VAS_052_System
 * 12  | Loading                              | VAS_052_Loading
 * 13  | No data                              | VAS_052_NoData
 * 14  | Unable to load today's cashbook      | VAS_052_LoadError
 * 15  | Cash entry                           | VAS_052_CashEntry
 * 16  | Session Expired                      | VAS_052_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_052_TodaysCashbookCashJournalWidget = function () {
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

        function getPrecision(data) {
            var stdPrecision = Number(data && data.stdPrecision);

            if (isNaN(stdPrecision) && VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatCurrencyAmount(value, data) {
            var numericValue = safeNumber(value);
            var precision = getPrecision(data);
            var symbol = data && data.currencySymbol ? data.currencySymbol : '';

            if (numericValue === 0) {
                return '-';
            }

            return symbol + numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function showBusy(show) {
            var $busy = $root.find('#VAS_052_cashbook-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $busy.addClass('is-visible'); } else { $busy.removeClass('is-visible'); }
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_052_cashbook-root',
                'id': 'VAS_052_cashbook-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_052_cashbook-card',
                'aria-label': lbl('VAS_052_TodaysCashbook', "Today's Cashbook")
            });

            var $busy = $('<div>', {
                'class': 'VAS_052_cashbook-busy',
                'id': 'VAS_052_cashbook-busy-' + widgetId,
                'text': lbl('VAS_052_Loading', 'Loading')
            });

            var $header = $('<div>', {
                'class': 'VAS_052_cashbook-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_052_cashbook-title-row'
            });

            var $icon = $(
                '<span class="VAS_052_cashbook-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"></path>' +
                '<path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2Z"></path>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_052_cashbook-title',
                'id': 'VAS_052_cashbook-title-' + widgetId,
                'text': lbl('VAS_052_TodaysCashbook', "Today's Cashbook")
            });

            var $meta = $('<span>', {
                'class': 'VAS_052_cashbook-meta',
                'id': 'VAS_052_cashbook-meta-' + widgetId,
                'text': '0 ' + lbl('VAS_052_Entries', 'entries')
            });

            //var $action = $('<button>', {
            //    'type': 'button',
            //    'class': 'VAS_052_cashbook-action',
            //    'id': 'VAS_052_cashbook-action-' + widgetId,
            //    'text': lbl('VAS_052_Entry', '+ Entry')
            //});

            var $body = $('<div>', {
                'class': 'VAS_052_cashbook-body'
            });

            var $table = $(
                '<table class="VAS_052_cashbook-table">' +
                '<thead>' +
                '<tr>' +
                '<th class="VAS_052_cashbook-col-time">' + lbl('VAS_052_Time', 'Time') + '</th>' +
                '<th>' + lbl('VAS_052_Description', 'Description') + '</th>' +
                '<th>' + lbl('VAS_052_Category', 'Category') + '</th>' +
                '<th>' + lbl('VAS_052_PostedBy', 'Posted by') + '</th>' +
                '<th class="">' + lbl('VAS_052_In', 'In') + '</th>' +
                '<th class="">' + lbl('VAS_052_Out', 'Out') + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody id="VAS_052_cashbook-rows-' + widgetId + '"></tbody>' +
                '</table>'
            );

            var $state = $('<div>', {
                'class': 'VAS_052_cashbook-state',
                'id': 'VAS_052_cashbook-state-' + widgetId
            });

            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow);
            $body.append($table);
            $card.append($busy).append($header).append($body).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            $root.find('#VAS_052_cashbook-state-' + widgetId).text(message || '').addClass('is-visible');
            $root.find('#VAS_052_cashbook-rows-' + widgetId).empty();
        }

        function createEntryRow(entry, data) {
            var categoryClass = entry.categoryClass || 'other';
            var $row = $('<tr>');

            $('<td>', {
                'class': 'VAS_052_cashbook-col-time',
                'text': entry.timeText || ''
            }).appendTo($row);

            $('<td>', {
                'class': 'VAS_052_cashbook-desc',
                'text': entry.description || ''
            }).appendTo($row);

            $('<td>').append(
                $('<span>', {
                    'class': 'VAS_052_cashbook-chip VAS_052_cashbook-chip-' + categoryClass,
                    'text': entry.category || lbl('VAS_052_Other', 'Other')
                })
            ).appendTo($row);

            $('<td>', {
                'class': 'VAS_052_cashbook-posted',
                'text': entry.postedBy || lbl('VAS_052_System', 'System')
            }).appendTo($row);

            $('<td>', {
                'class': 'VAS_052_cashbook-col-amount VAS_052_cashbook-in',
                'text': formatCurrencyAmount(entry.cashInAmount, data)
            }).appendTo($row);

            $('<td>', {
                'class': 'VAS_052_cashbook-col-amount VAS_052_cashbook-out',
                'text': formatCurrencyAmount(entry.cashOutAmount, data)
            }).appendTo($row);

            return $row;
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var entries = data.entries || [];
            var $rows = $root.find('#VAS_052_cashbook-rows-' + widgetId);

            $root.find('#VAS_052_cashbook-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_052_cashbook-title-' + widgetId).text(data.title || lbl('VAS_052_TodaysCashbook', "Today's Cashbook"));
            $root.find('#VAS_052_cashbook-meta-' + widgetId).text(data.metaText || entries.length + ' ' + lbl('VAS_052_Entries', 'entries'));
            $root.find('#VAS_052_cashbook-action-' + widgetId).text(data.actionText || lbl('VAS_052_Entry', '+ Entry'));

            $rows.empty();

            $.each(entries, function (index, entry) {
                $rows.append(createEntryRow(entry, data));
            });

            if (data.hasData === false || entries.length === 0) {
                setState(lbl('VAS_052_NoData', 'No data'));
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
                url: VIS.Application.contextUrl + 'VAS/VAS_052_TodaysCashbookCashJournal/GetTodaysCashbook',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_052_LoadError', "Unable to load today's cashbook"));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_052_LoadError', "Unable to load today's cashbook"));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_052_LoadError', "Unable to load today's cashbook"));
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

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
