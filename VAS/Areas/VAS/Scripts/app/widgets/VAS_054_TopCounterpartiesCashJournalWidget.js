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
 *  6  | Previous                             | VAS_054_Previous
 *  7  | Next                                 | VAS_054_Next
 *  8  | Of                                   | VAS_054_Of
 *  9  | Showing                              | VAS_054_Showing
 * 10  | Session Expired                      | VAS_054_SessionExpired
 * 11  | Customer                             | VAS_054_Customer
 * 12  | Vendor                               | VAS_054_Vendor
 * 13  | Bank                                 | VAS_054_Bank
 * 14  | Cashbook                             | VAS_054_Cashbook
 * 15  | Other                                | VAS_054_Other
 * 16  | Unknown                              | VAS_054_Unknown
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_054_TopCounterpartiesCashJournalWidget = function () {
        var $self = this;

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        var $footer = null;
        var $pageInfo = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $pageText = null;

        var pageNo = 1;
        var pageSize = 2;
        var totalPages = 0;
        var totalRecords = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function parseResponse(response) {
            var data = response;

            if (data && data.d) {
                data = data.d;
            }

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e1) {
                    return {
                        success: false,
                        error: data
                    };
                }
            }

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e2) {
                    return {
                        success: false,
                        error: data
                    };
                }
            }

            return data;
        }

        function formatAmount(item) {
            var precision = Number(item && item.stdPrecision);
            var symbol = item && item.currencySymbol ? item.currencySymbol : '';
            var iso = item && item.currencyISO ? item.currencyISO : '';

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var numericValue = safeNumber(item && item.displayAmount);

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            var sign = numericValue < 0 ? '-' : '';

            if (symbol) {
                return sign + symbol + amount;
            }

            return iso ? sign + iso + amount : sign + amount;
        }

        function renderAmount($target, item) {
            var precision = Number(item && item.stdPrecision);
            var symbol = item && item.currencySymbol ? item.currencySymbol : '';
            var iso = item && item.currencyISO ? item.currencyISO : '';

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var numericValue = safeNumber(item && item.displayAmount);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            $target.empty()
                .append($('<span>', {
                    'class': 'VAS-cash-amount-prefix',
                    'text': (numericValue < 0 ? '-' : '') + (symbol || iso)
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-main',
                    'text': decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-decimal',
                    'text': decimalMatch ? decimalMatch[1] : ''
                }));
        }

        function showBusy(show) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var $busy = $root.find('#VAS_054_counterparties-busy-' + widgetId);

            if (show) {
                $busy.addClass('is-visible');
            }
            else {
                $busy.removeClass('is-visible');
            }
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            var chevL =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="15 18 9 12 15 6"></polyline>' +
                '</svg>';

            var chevR =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="9 18 15 12 9 6"></polyline>' +
                '</svg>';

            $root = $('<div>', {
                'class': 'VAS_054_counterparties-root',
                'id': 'VAS_054_counterparties-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_054_counterparties-card',
                'aria-label': lbl('VAS_054_TopCounterparties', 'Top Counterparties')
            });

            var $busy = $('<div>', {
                'class': 'VAS_054_counterparties-busy vis-busyindicatorouterwrap',
                'id': 'VAS_054_counterparties-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_054_counterparties-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_054_counterparties-title-row'
            });

            var $icon = $(
                '<span class="VAS_054_counterparties-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
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

            var $body = $('<div>', {
                'class': 'VAS_054_counterparties-body'
            });

            var $table = $('<table>', {
                'class': 'VAS_054_counterparties-table'
            });

            var $thead = $('<thead>').appendTo($table);
            var $headerRow = $('<tr>').appendTo($thead);

            $('<th>', { 'class': 'VAS_054_counterparties-col-avatar' }).appendTo($headerRow);
            $('<th>', { 'class': 'VAS_054_counterparties-col-party',  'text': lbl('VAS_054_ColParty',  'Party')     }).appendTo($headerRow);
            $('<th>', { 'class': 'VAS_054_counterparties-col-type',   'text': lbl('VAS_054_ColType',   'Type')      }).appendTo($headerRow);
            $('<th>', { 'class': 'VAS_054_counterparties-col-txns',   'text': lbl('VAS_054_ColTxns',   'Txns')      }).appendTo($headerRow);
            $('<th>', { 'class': 'VAS_054_counterparties-col-trend',  'text': lbl('VAS_054_ColTrend',  '30d Trend') }).appendTo($headerRow);
            $('<th>', { 'class': 'VAS_054_counterparties-col-volume', 'text': lbl('VAS_054_ColVolume', 'Volume')    }).appendTo($headerRow);

            var $list = $('<tbody>', {
                'id': 'VAS_054_counterparties-list-' + widgetId
            });

            $table.append($list);
            $body.append($table);

            var $state = $('<div>', {
                'class': 'VAS_054_counterparties-state',
                'id': 'VAS_054_counterparties-state-' + widgetId
            });

            $footer = $('<div>', {
                'class': 'VAS_054_counterparties-footer',
                'style': 'display:none;'
            });

            $pageInfo = $('<span>', {
                'class': 'VAS_054_counterparties-footer-info'
            });

            var $pager = $('<div>', {
                'class': 'VAS_054_counterparties-pager'
            });

            $prevBtn = $('<button>', {
                'type': 'button',
                'class': 'VAS_054_counterparties-page-btn VAS_054_counterparties-prev',
                'aria-label': lbl('VAS_054_Previous', 'Previous'),
                'html': chevL
            });

            $pageText = $('<span>', {
                'class': 'VAS_054_counterparties-page-text'
            });

            $nextBtn = $('<button>', {
                'type': 'button',
                'class': 'VAS_054_counterparties-page-btn VAS_054_counterparties-next',
                'aria-label': lbl('VAS_054_Next', 'Next'),
                'html': chevR
            });

            $prevBtn.on('click', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                loadData();
            });

            $nextBtn.on('click', function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadData();
            });

            $pager.append($prevBtn).append($pageText).append($nextBtn);
            $footer.append($pageInfo).append($pager);

            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow);

            $card
                .append($busy)
                .append($header)
                .append($body)
                .append($footer)
                .append($state);

            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            totalRecords = 0;
            totalPages = 0;

            $root.find('#VAS_054_counterparties-state-' + widgetId)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_054_counterparties-list-' + widgetId).empty();

            updatePager();
        }

        function clearState() {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            $root.find('#VAS_054_counterparties-state-' + widgetId)
                .removeClass('is-visible')
                .text('');
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl('VAS_054_Of', 'of') + ' ' + totalPages) : '');
            }

            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);

                    $pageInfo.text(
                        lbl('VAS_054_Showing', 'Showing') +
                        ' ' +
                        from +
                        '–' +
                        to +
                        ' ' +
                        lbl('VAS_054_Of', 'of') +
                        ' ' +
                        totalRecords
                    );
                }
                else {
                    $pageInfo.text('');
                }
            }

            if ($prevBtn) {
                $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }

            if ($nextBtn) {
                $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }

            if ($footer) {
                $footer.css('display', totalPages > 1 ? 'flex' : 'none');
            }
        }

        function resetHorizontalScroll() {
            if (!$root) {
                return;
            }

            $root.find('.VAS_054_counterparties-body').scrollLeft(0);
        }

        function buildSparkline(trend) {
            var points = trend === 'down'
                ? '2,8 12,11 22,7 32,12 42,8 52,13 62,15'
                : trend === 'flat'
                    ? '2,10 12,10 22,10 32,10 42,10 52,10 62,10'
                    : '2,14 12,11 22,13 32,8 42,10 52,6 62,4';

            return '<svg class="VAS_054_counterparties-sparkline VAS_054_counterparties-sparkline-' + (trend || 'flat') + '" ' +
                'viewBox="0 0 64 20" preserveAspectRatio="none" aria-hidden="true">' +
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

            pageNo = safeNumber(data.pageNo) > 0 ? safeNumber(data.pageNo) : pageNo;
            pageSize = safeNumber(data.pageSize) > 0 ? safeNumber(data.pageSize) : pageSize;
            totalRecords = safeNumber(data.totalRecords || data.totalCounterparties);
            totalPages = pageSize > 0 ? Math.ceil(totalRecords / pageSize) : 0;

            clearState();

            var title = data.title || lbl('VAS_054_TopCounterparties', 'Top Counterparties');
            var metaText = data.metaText || lbl('VAS_054_Last30Days', 'Last 30 Days');
            $root.find('#VAS_054_counterparties-title-' + widgetId)
                .text(title)
                .attr('title', title);

            $root.find('#VAS_054_counterparties-meta-' + widgetId)
                .text(metaText)
                .attr('title', metaText);

            $list.empty();

            if (!items.length) {
                setState('No data');
                return;
            }

            $.each(items, function (index, item) {
                var $row = $('<tr>', {
                    'class': 'VAS_054_counterparties-row'
                });

                var $avatarCell = $('<td>', {
                    'class': 'VAS_054_counterparties-cell-avatar'
                }).append(
                    $('<span>', {
                        'class': 'VAS_054_counterparties-avatar VAS_054_counterparties-avatar-' + (item.typeClass || 'other'),
                        'text': item.initials || '--'
                    })
                );

                var $nameCell = $('<td>', {
                    'class': 'VAS_054_counterparties-cell-party'
                }).append(
                    $('<div>', {
                        'class': 'VAS_054_counterparties-name',
                        'text': item.name || '-',
                        'title': item.name || '-'
                    })
                );

                var $typeCell = $('<td>', {
                    'class': 'VAS_054_counterparties-cell-type'
                }).append(
                    $('<span>', {
                        'class': 'VAS_054_counterparties-chip VAS_054_counterparties-chip-' + (item.typeClass || 'other'),
                        'text': item.typeText || '',
                        'title': item.typeText || ''
                    })
                );

                var $count = $('<td>', {
                    'class': 'VAS_054_counterparties-count',
                    'text': safeNumber(item.entryCount).toLocaleString(window.navigator.language)
                });

                var $spark = $('<td>', {
                    'class': 'VAS_054_counterparties-spark-wrap',
                    'html': buildSparkline(item.trend)
                });

                var $amountCell = $('<td>', {
                    'class': 'VAS_054_counterparties-cell-volume'
                });

                var $amount = $('<span>', {
                    'class': 'VAS_054_counterparties-amount',
                    'title': formatAmount(item)
                });

                renderAmount($amount, item);
                $amountCell.append($amount);

                $row
                    .append($avatarCell)
                    .append($nameCell)
                    .append($typeCell)
                    .append($count)
                    .append($spark)
                    .append($amountCell);

                $list.append($row);
            });

            updatePager();
            resetHorizontalScroll();
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
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = parseResponse(response);

                    if (!data) {
                        setState('Empty response');
                        return;
                    }

                    if (data.error === 'VAS_054_SessionExpired') {
                        setState(lbl('VAS_054_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (data.success === false || data.error) {
                        setState(data.error || lbl('VAS_054_LoadError', 'Unable to load top counterparties'));
                        return;
                    }

                    renderData(data);
                },
                error: function (xhr, status, errorThrown) {
                    if (isDisposed) {
                        return;
                    }

                    var msg = '';

                    if (xhr && xhr.responseText) {
                        msg = xhr.responseText;
                    }
                    else if (errorThrown) {
                        msg = errorThrown;
                    }
                    else {
                        msg = status || lbl('VAS_054_LoadError', 'Unable to load top counterparties');
                    }

                    console.log('VAS_054 AJAX error:', status, errorThrown, xhr);
                    setState(msg);
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
            pageNo = 1;
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

    VAS.VAS_054_TopCounterpartiesCashJournal = VAS.VAS_054_TopCounterpartiesCashJournalWidget;

})(VAS, jQuery);
