/**
 * Cash Out by Category - Cash Journal
 * Purpose - Shows today's cash out distribution grouped by cash type.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Cash Out by Category                 | VAS_055_CashOutByCategory
 *  2  | Grouped by cash type for today       | VAS_055_Subtitle
 *  5  | Other                                | VAS_055_Other
 *  6  | Loading                              | VAS_055_Loading
 *  7  | No cash out today                    | VAS_055_NoData
 *  8  | Unable to load cash out by category  | VAS_055_LoadError
 *  9  | Session Expired                      | VAS_055_SessionExpired
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

    VAS.VAS_055_CashByCategoryCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        var $footer = null;
        var $body = null;
        var $pageInfo = null;
        var $pager = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $pageText = null;
        var resizeObserver = null;

        var pageNo = 1;
        var pageSize = 2;
        var totalPages = 0;
        var totalRecords = 0;

        /* Row-height guess used ONLY before the first row is rendered; from then on
           the real height is measured (see measureRowHeight). */
        var ROW_HEIGHT_FALLBACK = 62;
        var MIN_PAGE_SIZE = 2;
        /* Retry budget for a capacity measurement taken before the widget is laid
           out (clientHeight 0). Bounded so a hidden widget can't spin forever. */
        var MAX_CAPACITY_RETRIES = 30;
        var capacityRetries = 0;
        var capacitySyncPending = false;

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

        /* ── Amount formatter ───────────────────────────────────────── */
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
         * Category amounts are aggregates — render a compact, locale-aware
         * magnitude via VIS.Util.formatCompactAmount (K/L/Cr or M/B tiers by
         * base currency). The formatter returns a non-negative magnitude, so we
         * compose the final string ourselves: sign, then currency, then
         * magnitude — a negative renders as e.g. -$200 (leading minus, before
         * the symbol).
         */
        function formatCompactCurrency(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value) || 0;
            var sign = numericValue < 0 ? '-' : '';
            var currency = getCurrencyLabel(currencySymbol, currencyISO);
            var magnitude = VIS.Util.formatCompactAmount(numericValue, currencyISO, getPrecision(precision));

            return sign + currency + magnitude;
        }

        function renderCurrencyAmount($target, value, currencySymbol, currencyISO, precision) {
            $target.text(formatCompactCurrency(value, currencySymbol, currencyISO, precision));
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
                'class': 'VAS_055_cash-category-busy vis-busyindicatorouterwrap',
                'id': 'VAS_055_cash-category-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
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

            var $subtitle = $('<span>', {
                'class': 'VAS_055_cash-category-subtitle',
                'id': 'VAS_055_cash-category-subtitle-' + widgetId,
                'text': lbl('VAS_055_Subtitle', 'Grouped by cash type for today')
            });

            var $titleWrap = $('<div>', {
                'class': 'VAS_055_cash-category-title-wrap'
            });

            $body = $('<div>', {
                'class': 'VAS_055_cash-category-body',
                'id': 'VAS_055_cash-category-body-' + widgetId
            });

            $footer = $('<div>', {
                'class': 'VAS_055_cash-category-footer'
            });

            var $footerText = $('<div>', {
                'class': 'VAS_055_cash-category-footer-text'
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
            $footerText.append($pageInfo);
            $titleWrap.append($title).append($subtitle);
            $titleRow.append($icon).append($titleWrap);
            $header.append($titleRow);
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
            /* The pager stays on screen for a SINGLE page too — it then reads
               "1 of 1" with both arrows disabled, so the control never disappears
               under the user. It is hidden only when there is nothing to page. */
            var pagesShown = Math.max(totalPages, totalRecords > 0 ? 1 : 0);

            if ($pageText) {
                $pageText.text(
                    pagesShown > 0
                        ? (Math.max(1, pageNo) + ' ' + lbl('VAS_Of', 'of') + ' ' + pagesShown)
                        : ''
                );
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
                $pager.css('display', pagesShown > 0 ? 'inline-flex' : 'none');
            }
        }

        /* Height of ONE rendered row, measured from the DOM so the capacity tracks
           the card's actual font scale (a row is ~45px at the default clamp — the
           old fixed 72px divisor over-stated it and left rows' worth of space
           unused). The fallback applies only before the first row exists. */
        function measureRowHeight() {
            var $sampleRow =
                $body
                    ? $body.children('.VAS_055_cash-category-row').first()
                    : null;

            var rowHeight =
                ($sampleRow && $sampleRow.length)
                    ? $sampleRow.outerHeight()
                    : 0;

            return rowHeight > 0 ? rowHeight : ROW_HEIGHT_FALLBACK;
        }

        /* The body is a flex column with a `gap` between rows — it has to be part of
           the fit, otherwise every extra row is short by one gap. */
        function measureRowGap() {
            if (!$body || !$body[0] || !window.getComputedStyle) {
                return 0;
            }

            var style = window.getComputedStyle($body[0]);
            var gap = parseFloat(style.rowGap || style.gap);

            return isNaN(gap) ? 0 : gap;
        }

        /* Re-measure on the next frame — used when the body has no height yet (still
           detached / not laid out), so the page size is not locked to the minimum. */
        function scheduleCapacitySync(shouldReload) {
            if (capacitySyncPending || capacityRetries >= MAX_CAPACITY_RETRIES) {
                return;
            }

            capacityRetries++;
            capacitySyncPending = true;

            var raf =
                window.requestAnimationFrame ||
                function (callback) {
                    return window.setTimeout(callback, 16);
                };

            raf(function () {
                capacitySyncPending = false;

                if (isDisposed) {
                    return;
                }

                updateAdaptivePageSize(shouldReload);
            });
        }

        function updateAdaptivePageSize(shouldReload) {
            if (!$body || !$body[0]) {
                return;
            }

            var availableHeight = $body[0].clientHeight;

            if (availableHeight <= 0) {
                scheduleCapacitySync(shouldReload);
                return;
            }

            capacityRetries = 0;

            var rowHeight = measureRowHeight();
            var gap = measureRowGap();

            /* n rows occupy n*rowHeight + (n-1)*gap, so the fit is
               n = (available + gap) / (rowHeight + gap). */
            var nextPageSize =
                Math.max(
                    MIN_PAGE_SIZE,
                    Math.floor(
                        (availableHeight + gap) /
                        (rowHeight + gap)
                    )
                );

            if (nextPageSize === pageSize) {
                return;
            }

            var firstVisibleRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstVisibleRecord / pageSize));

            if (shouldReload) {
                loadData();
            }
        }

        function setupAdaptivePagination() {
            updateAdaptivePageSize(false);

            window.setTimeout(function () {
                updateAdaptivePageSize(true);
            }, 0);

            if (!window.ResizeObserver || !$body || !$body[0]) {
                return;
            }

            resizeObserver = new ResizeObserver(function () {
                capacityRetries = 0;
                updateAdaptivePageSize(true);
            });

            resizeObserver.observe($body[0]);
        }

        function createRow(item, index, currencySymbol, currencyISO, currencyId, precisionFallback) {
            var percent = Math.max(0, Math.min(100, safeNumber(item.percent)));
            var amount = safeNumber(item.cashOutAmount);
            var precision = item.stdPrecision || item.StdPrecision || precisionFallback;
            var rowCurrencySymbol = firstText(item.currencySymbol, item.CurrencySymbol, item.symbol, item.Symbol, currencySymbol);
            var rowCurrencyISO = firstText(item.currencyISO, item.CurrencyISO, item.currencyISOCode, item.CurrencyISOCode, currencyISO);
            var colorClass = item.colorClass || 'VAS_055_cash-category-bar-' + ((index % 3) + 1);

            var $row = $('<div>', {
                'class': 'VAS_055_cash-category-row',
                'title': (item.name || lbl('VAS_055_Other', 'Other')) + ' · ' + formatCompactCurrency(amount, rowCurrencySymbol, rowCurrencyISO, precision)
            });

            var $rowTop = $('<div>', {
                'class': 'VAS_055_cash-category-row-top'
            });

            var $name = $('<span>', {
                'class': 'VAS_055_cash-category-name',
                'text': item.name || lbl('VAS_055_Other', 'Other'),
                'title': item.name || lbl('VAS_055_Other', 'Other')
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

            renderCurrencyAmount($amount, amount, rowCurrencySymbol, rowCurrencyISO, precision);

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

            /* One metric line: amount, a muted middle-dot, then the share —
               "$1.069B · 82%". */
            var $separator = $('<span>', {
                'class': 'VAS_055_cash-category-sep',
                'aria-hidden': 'true',
                'text': '·'
            });

            $metrics.append($amount).append($separator).append($percent);
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
            var $rows = $root.find('#VAS_055_cash-category-body-' + widgetId);
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
            var title = data.title || lbl('VAS_055_CashOutByCategory', 'Cash Out by Category');
            $root.find('#VAS_055_cash-category-title-' + widgetId).text(title).attr('title', title);

            $rows.empty();

            if (data.hasData === false || items.length === 0) {
                setState(lbl('VAS_055_NoData', 'No cash out today'));
                return;
            }

            $.each(items, function (index, item) {
                $rows.append(createRow(item, index, currencySymbol, currencyISO, currencyId, precision));
            });

            updatePager();

            /* Rows are on screen now, so the capacity can be measured for real
               (row height + gap). updateAdaptivePageSize() no-ops when the size is
               unchanged, so this settles after at most one extra fetch. Without it
               the widget kept the size it guessed while its body was still
               unsized — e.g. 2 rows in a card with room for 4. */
            updateAdaptivePageSize(true);
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
            setupAdaptivePagination();
            loadData();
        };

        this.refreshWidget = function () {
            pageNo = 1;
            loadData();
        };

        /* Re-measure the row capacity when the dashboard resizes the widget — the
           framework's own size hook, in addition to the ResizeObserver. */
        this.handleSizeChange = function () {
            if (isDisposed) {
                return;
            }

            capacityRetries = 0;
            updateAdaptivePageSize(true);
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

            if (resizeObserver) {
                resizeObserver.disconnect();
            }

            ajaxRequest = null;
            resizeObserver = null;
            $body = null;
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

        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_055_CashByCategoryCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
        if (this.handleSizeChange) {
            this.handleSizeChange();
        }
    };

    VAS.VAS_055_CashByCategoryCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
