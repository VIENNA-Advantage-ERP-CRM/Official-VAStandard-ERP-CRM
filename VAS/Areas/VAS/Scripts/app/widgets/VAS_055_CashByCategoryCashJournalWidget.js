/**
 * Cash Out by Category - Cash Journal
 * Purpose - Shows today's cash out distribution grouped by cash type.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Cash Out by Category                 | VAS_055_CashOutByCategory
 *  2  | Today                                | VAS_055_Today
 *  3  | Why                                  | VAS_055_Why
 *  4  | Grouped by cash type for today.      | VAS_055_WhyText
 *  5  | Other                                | VAS_055_Other
 *  6  | Loading                              | VAS_055_Loading
 *  7  | No cash out today                    | VAS_055_NoData
 *  8  | Unable to load cash out by category  | VAS_055_LoadError
 *  9  | Session Expired                      | VAS_055_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_055_CashByCategoryCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        var $footer = null;
        var $pageInfo = null;
        var $pager = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $pageText = null;

        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var totalRecords = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function firstText() {
            for (var i = 0; i < arguments.length; i++) {
                if (arguments[i] !== null && arguments[i] !== undefined) {
                    var value = String(arguments[i]).trim();

                    if (value) {
                        return value;
                    }
                }
            }

            return '';
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, currencyId, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var symbol = firstText(currencySymbol);
            var iso = firstText(currencyISO);
            var id = firstText(currencyId);

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var sign = numericValue < 0 ? '-' : '';

            if (symbol) {
                return sign + symbol + ' ' + amount;
            }

            if (iso) {
                return sign + iso + ' ' + amount;
            }

            return id ? sign + '#' + id + ' ' + amount : sign + amount;
        }

        function renderCurrencyAmount($target, value, currencySymbol, currencyISO, currencyId, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var prefix = firstText(currencySymbol, currencyISO);
            var id = firstText(currencyId);

            if (!prefix && id) {
                prefix = '#' + id;
            }

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            $target.empty()
                .append($('<span>', {
                    'class': 'VAS-cash-amount-prefix',
                    'text': (numericValue < 0 ? '-' : '') + prefix
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

            var $busy = $root.find('#VAS_055_cash-category-busy-' + $self.AD_UserHomeWidgetID);

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
                'class': 'VAS_055_cash-category-root',
                'id': 'VAS_055_cash-category-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_055_cash-category-card',
                'aria-label': lbl('VAS_055_CashOutByCategory', 'Cash Out by Category')
            });

            var $busy = $('<div>', {
                'class': 'VAS_055_cash-category-busy',
                'id': 'VAS_055_cash-category-busy-' + widgetId,
                'text': lbl('VAS_055_Loading', 'Loading')
            });

            var $header = $('<div>', {
                'class': 'VAS_055_cash-category-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_055_cash-category-title-row'
            });

            var $icon = $(
                '<span class="VAS_055_cash-category-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M21 12a9 9 0 1 1-9-9v9Z"></path>' +
                '<path d="M12 3a9 9 0 0 1 9 9h-9Z"></path>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_055_cash-category-title',
                'id': 'VAS_055_cash-category-title-' + widgetId,
                'text': lbl('VAS_055_CashOutByCategory', 'Cash Out by Category')
            });

            var $meta = $('<span>', {
                'class': 'VAS_055_cash-category-meta',
                'id': 'VAS_055_cash-category-meta-' + widgetId,
                'text': lbl('VAS_055_Today', 'Today')
            });

            var $body = $('<div>', {
                'class': 'VAS_055_cash-category-body',
                'id': 'VAS_055_cash-category-body-' + widgetId
            });

            $footer = $('<div>', {
                'class': 'VAS_055_cash-category-footer'
            });

            var $footerText = $('<div>', {
                'class': 'VAS_055_cash-category-footer-text'
            });

            var $whyText = $('<span>', {
                'class': 'VAS_055_cash-category-why-text',
                'id': 'VAS_055_cash-category-why-text-' + widgetId,
                'text': lbl('VAS_055_WhyText', 'Grouped by cash type for today.')
            });

            $pageInfo = $('<span>', {
                'class': 'VAS_055_cash-category-footer-info'
            });

            $pager = $('<div>', {
                'class': 'VAS_055_cash-category-pager',
                'style': 'display:none;'
            });

            $prevBtn = $('<button>', {
                'type': 'button',
                'class': 'VAS_055_cash-category-page-btn VAS_055_cash-category-prev',
                'aria-label': lbl('VAS_Previous', 'Previous'),
                'html': chevL
            });

            $pageText = $('<span>', {
                'class': 'VAS_055_cash-category-page-text'
            });

            $nextBtn = $('<button>', {
                'type': 'button',
                'class': 'VAS_055_cash-category-page-btn VAS_055_cash-category-next',
                'aria-label': lbl('VAS_Next', 'Next'),
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

            var $state = $('<div>', {
                'class': 'VAS_055_cash-category-state',
                'id': 'VAS_055_cash-category-state-' + widgetId
            });

            $pager.append($prevBtn).append($pageText).append($nextBtn);
            $footerText.append($whyText).append($pageInfo);
            $titleRow.append($icon).append($title);
            $header.append($titleRow).append($meta);
            $footer.append($footerText).append($pager);
            $card.append($busy).append($header).append($body).append($footer).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            totalRecords = 0;
            totalPages = 0;

            $root.find('#VAS_055_cash-category-state-' + widgetId).text(message || '').addClass('is-visible');
            $root.find('#VAS_055_cash-category-body-' + widgetId).empty();

            updatePager();
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages) : '');
            }

            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);

                    $pageInfo.text(
                        lbl('VAS_Showing', 'Showing') +
                        ' ' +
                        from +
                        '-' +
                        to +
                        ' ' +
                        lbl('VAS_Of', 'of') +
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

            if ($pager) {
                $pager.css('display', totalPages > 1 ? 'inline-flex' : 'none');
            }
        }

        function createRow(item, index, currencySymbol, currencyISO, currencyId, precisionFallback) {
            var percent = Math.max(0, Math.min(100, safeNumber(item.percent)));
            var amount = safeNumber(item.cashOutAmount);
            var precision = item.stdPrecision || item.StdPrecision || precisionFallback;
            var rowCurrencySymbol = firstText(item.currencySymbol, item.CurrencySymbol, item.symbol, item.Symbol, currencySymbol);
            var rowCurrencyISO = firstText(item.currencyISO, item.CurrencyISO, item.currencyISOCode, item.CurrencyISOCode, currencyISO);
            var rowCurrencyId = firstText(item.cCurrencyId, item.C_Currency_ID, item.CCurrencyId, currencyId);
            var colorClass = item.colorClass || 'VAS_055_cash-category-bar-' + ((index % 3) + 1);

            var $row = $('<div>', {
                'class': 'VAS_055_cash-category-row',
                'title': (item.name || lbl('VAS_055_Other', 'Other')) + ' · ' + formatCurrencyAmount(amount, rowCurrencySymbol, rowCurrencyISO, rowCurrencyId, precision)
            });

            var $rowTop = $('<div>', {
                'class': 'VAS_055_cash-category-row-top'
            });

            var $name = $('<span>', {
                'class': 'VAS_055_cash-category-name',
                'text': item.name || lbl('VAS_055_Other', 'Other')
            });

            var $percent = $('<span>', {
                'class': 'VAS_055_cash-category-percent',
                'text': percent.toLocaleString(window.navigator.language, {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 0
                }) + '%'
            });

            var $metrics = $('<span>', {
                'class': 'VAS_055_cash-category-metrics'
            });

            var $amount = $('<span>', {
                'class': 'VAS_055_cash-category-amount'
            });

            renderCurrencyAmount($amount, amount, rowCurrencySymbol, rowCurrencyISO, rowCurrencyId, precision);

            var $track = $('<div>', {
                'class': 'VAS_055_cash-category-track',
                'role': 'progressbar',
                'aria-valuemin': '0',
                'aria-valuemax': '100',
                'aria-valuenow': percent
            });

            var $bar = $('<div>', {
                'class': 'VAS_055_cash-category-fill ' + colorClass
            }).css('--VAS_055_bar-width', percent + '%');

            $metrics.append($percent).append($amount);
            $rowTop.append($name).append($metrics);
            $track.append($bar);
            $row.append($rowTop).append($track);

            return $row;
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var items = data.items || [];
            var $body = $root.find('#VAS_055_cash-category-body-' + widgetId);
            var currencySymbol = firstText(data.currencySymbol, data.CurrencySymbol, data.symbol, data.Symbol);
            var currencyISO = firstText(data.currencyISO, data.CurrencyISO, data.currencyISOCode, data.CurrencyISOCode);
            var currencyId = firstText(data.cCurrencyId, data.C_Currency_ID, data.CCurrencyId);
            var precision = data.stdPrecision || data.StdPrecision;

            pageNo = safeNumber(data.pageNo) > 0 ? safeNumber(data.pageNo) : pageNo;
            pageSize = safeNumber(data.pageSize) > 0 ? safeNumber(data.pageSize) : pageSize;
            totalRecords = safeNumber(data.totalRecords);
            totalPages = safeNumber(data.totalPages) > 0
                ? safeNumber(data.totalPages)
                : (pageSize > 0 ? Math.ceil(totalRecords / pageSize) : 0);

            $root.find('#VAS_055_cash-category-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_055_cash-category-title-' + widgetId).text(data.title || lbl('VAS_055_CashOutByCategory', 'Cash Out by Category'));
            $root.find('#VAS_055_cash-category-meta-' + widgetId).text(data.metaText || lbl('VAS_055_Today', 'Today'));
            $root.find('#VAS_055_cash-category-why-' + widgetId).text(data.whyLabel || lbl('VAS_055_Why', 'Why'));
            $root.find('#VAS_055_cash-category-why-text-' + widgetId).text(data.whyText || lbl('VAS_055_WhyText', 'Grouped by cash type for today.'));

            $body.empty();

            if (data.hasData === false || items.length === 0) {
                setState(data.noDataText || lbl('VAS_055_NoData', 'No cash out today'));
                return;
            }

            $.each(items, function (index, item) {
                $body.append(createRow(item, index, currencySymbol, currencyISO, currencyId, precision));
            });

            updatePager();
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
                url: VIS.Application.contextUrl + 'VAS/VAS_055_CashByCategoryCashJournal/GetTodayCashOutByCategory',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_055_LoadError', 'Unable to load cash out by category'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_055_LoadError', 'Unable to load cash out by category'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_055_LoadError', 'Unable to load cash out by category'));
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
            $self = null;
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_055_CashByCategoryCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_055_CashByCategoryCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_055_CashByCategoryCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
