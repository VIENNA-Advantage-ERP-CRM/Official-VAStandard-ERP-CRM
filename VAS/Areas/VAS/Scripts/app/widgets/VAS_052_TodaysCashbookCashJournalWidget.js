
/**
 * Today's Cashbook - Cash Journal
 * Purpose - Shows today's cashbook entries from Cash Journal lines.
 */

/**
 * Today's Cashbook - Cash Journal
 * Purpose - Shows today's cashbook entries from Cash Journal lines.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Today's Cashbook                      | VAS_052_TodaysCashbook
 *  2  | Entries                               | VAS_052_Entries
 *  3  | Time                                  | VAS_052_Time
 *  4  | Description                           | VAS_052_Description
 *  5  | Category                              | VAS_052_Category
 *  6  | Cash Type                             | VAS_052_CashType
 *  7  | Posted By                             | VAS_052_PostedBy
 *  8  | In                                    | VAS_052_In
 *  9  | Out                                   | VAS_052_Out
 * 10  | Previous                              | VAS_052_Previous
 * 11  | Next                                  | VAS_052_Next
 * 12  | Of                                    | VAS_052_Of
 * 13  | Showing                               | VAS_052_Showing
 * 14  | Other                                 | VAS_052_Other
 * 15  | System                                | VAS_052_System
 * 16  | No Data                               | VAS_052_NoData
 * 17  | Unable To Load Today's Cashbook       | VAS_052_LoadError
 * 18  | Session Expired                       | VAS_052_SessionExpired
 * 19  | + Entry                               | VAS_052_Entry
 * 20  | Cash Entry                            | VAS_052_CashEntry
 * 21  | Document No                           | VAS_052_DocumentNo
 * 22  | Charge                                | VAS_052_Charge
 * 23  | Cashbook                              | VAS_052_CashBook
 * 24  | Today's Cash Journals in Base Currency| VAS_052_Subtitle
 * 25  | Open Record                           | VAS_052_OpenRecord
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

    VAS.VAS_052_TodaysCashbookCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        var $footer = null;
        var $body = null;
        var $pageInfo = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $pageText = null;
        var resizeObserver = null;

        var pageNo = 1;
        var pageSize = 3;
        /* Row-height guess used only before the first row is rendered; from then
           on the real height is measured (see measureRowHeight). */
        var ROW_HEIGHT_FALLBACK = 42;
        var totalPages = 0;
        var totalRecords = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== key &&
                text !== '[' + key + ']'
                ? text
                : fallback;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);

            return isNaN(numberValue)
                ? 0
                : numberValue;
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

            if (data && data.d) {
                data = data.d;
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

        function getPrecision(data) {
            var stdPrecision =
                Number(
                    data &&
                    data.stdPrecision
                );

            return isNaN(stdPrecision) ||
                stdPrecision < 0
                ? 2
                : stdPrecision;
        }

        function formatCurrencyAmount(value, data) {
            var numericValue =
                safeNumber(value);

            var precision =
                getPrecision(data);

            if (numericValue === 0) {
                return '-';
            }

            /* Line-item amounts keep full precision (never compacted). */
            var amount =
                Math.abs(numericValue)
                    .toLocaleString(
                        window.navigator.language,
                        {
                            minimumFractionDigits:
                                precision,

                            maximumFractionDigits:
                                precision
                        }
                    );

            var sign =
                numericValue < 0
                    ? '-'
                    : '';

            var currency = getCurrencyLabel(data);

            return currency
                ? sign + currency + amount
                : sign + amount;
        }

        /* Prefer the currency symbol; fall back to the ISO code when no
           symbol is available (IQD's ISO renders as the short 'ID'). */
        function getCurrencyLabel(data) {
            var symbol = String(
                (data && data.currencySymbol) || ''
            ).trim();

            if (symbol) {
                return symbol;
            }

            var iso = String(
                (
                    data &&
                    (data.currencyISO || data.currencyISOCode)
                ) || ''
            ).trim();

            return iso.toUpperCase() === 'IQD' ? 'ID' : iso;
        }

        function renderCurrencyAmount(
            $target,
            value,
            data
        ) {
            var numericValue =
                safeNumber(value);

            if (numericValue === 0) {
                $target.text('-').attr('title', '-');
                return;
            }

            var precision =
                getPrecision(data);

            var amount =
                Math.abs(numericValue)
                    .toLocaleString(
                        window.navigator.language,
                        {
                            minimumFractionDigits:
                                precision,

                            maximumFractionDigits:
                                precision
                        }
                    );

            var decimalMatch =
                amount.match(
                    /([.,]\d+)$/
                );

            $target.attr('title', formatCurrencyAmount(numericValue, data));

            $target
                .empty()
                .append(
                    $('<span>', {
                        'class':
                            'VAS-cash-amount-prefix',

                        'text':
                            (
                                numericValue < 0
                                    ? '-'
                                    : ''
                            ) +
                            getCurrencyLabel(data)
                    })
                )
                .append(
                    $('<span>', {
                        'class':
                            'VAS-cash-amount-main',

                        'text':
                            decimalMatch
                                ? amount.substring(
                                    0,
                                    amount.length -
                                    decimalMatch[1].length
                                )
                                : amount
                    })
                )
                .append(
                    $('<span>', {
                        'class':
                            'VAS-cash-amount-decimal',

                        'text':
                            decimalMatch
                                ? decimalMatch[1]
                                : ''
                    })
                );
        }

        /* The framework navigates IN-PLACE (no new window) only when the payload's
           ActionName equals the name of the window currently HOSTING this widget;
           otherwise it opens a new window. Resolve the host window name from the
           listener chain and pass it as ActionName. */
        function hostWindowName() {
            try {
                var listener = $self.listener;

                for (
                    var index = 0;
                    index < 6 && listener;
                    index++
                ) {
                    if (
                        listener.apanel &&
                        listener.apanel.gridWindow &&
                        listener.apanel.gridWindow.getName
                    ) {
                        return listener.apanel.gridWindow.getName();
                    }

                    if (
                        listener.gridWindow &&
                        listener.gridWindow.getName
                    ) {
                        return listener.gridWindow.getName();
                    }

                    listener = listener.listener;
                }
            }
            catch (e) { }

            return '';
        }

        /* Open the cash journal record (C_Cash) behind the clicked document number -
           same contract as the sibling cash-journal widgets (VAS_053 / VAS_069). */
        function zoomToCashJournal(recordId) {
            recordId = safeNumber(recordId);

            if (!recordId) {
                return;
            }

            try {
                $self.widgetFirevalueChanged({
                    'TabWhereClause':
                        'C_Cash.C_Cash_ID=' + recordId,

                    'TabLayout':
                        'Y',   /* 'N' Grid, 'Y' Single, 'C' Card */

                    'TabIndex':
                        '0',

                    'ActionName':
                        hostWindowName() ||
                        'VAS_CashJournal',

                    'ActionType':
                        'W'
                });
            }
            catch (e) { /* zoom is best-effort */ }
        }

        function showBusy(show) {
            if (!$root) {
                return;
            }

            var $busy =
                $root.find(
                    '#VAS_052_cashbook-busy-' +
                    $self.AD_UserHomeWidgetID
                );

            if (show) {
                $busy.addClass(
                    'is-visible'
                );
            }
            else {
                $busy.removeClass(
                    'is-visible'
                );
            }
        }

        function buildLayout() {
            var widgetId =
                $self.AD_UserHomeWidgetID;

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

            $root =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-root',

                    'id':
                        'VAS_052_cashbook-root-' +
                        widgetId
                });

            var $card =
                $('<section>', {
                    'class':
                        'VAS_052_cashbook-card',

                    'aria-label':
                        lbl(
                            'VAS_052_TodaysCashbook',
                            "Today's Cashbook"
                        )
                });

            var $busy =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-busy vis-busyindicatorouterwrap',

                    'id':
                        'VAS_052_cashbook-busy-' +
                        widgetId,

                    'html':
                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        '</div>'
                });

            var $header =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-header'
                });

            var $titleRow =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-title-row'
                });

            var $icon =
                $(
                    '<span class="VAS_052_cashbook-icon">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                    '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"></path>' +
                    '<path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2Z"></path>' +
                    '</svg>' +
                    '</span>'
                );

            var $title =
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-title',

                    'id':
                        'VAS_052_cashbook-title-' +
                        widgetId,

                    'text':
                        lbl(
                            'VAS_052_TodaysCashbook',
                            "Today's Cashbook"
                        )
                });

            /* Subtitle under the title (dashboard-widgets.md > "Widget Header"):
               states that every amount on the card is converted to the accounting
               schema's base currency. */
            var $subtitle =
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-subtitle',

                    'id':
                        'VAS_052_cashbook-subtitle-' +
                        widgetId,

                    'text':
                        lbl(
                            'VAS_052_Subtitle',
                            "Today's Cash Journals in Base Currency"
                        )
                });

            /* Title + subtitle stack beside the icon well. */
            var $labelGroup =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-label-group'
                });

            var $meta =
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-meta',

                    'id':
                        'VAS_052_cashbook-meta-' +
                        widgetId,

                    'text':
                        '0 ' +
                        lbl(
                            'VAS_052_Entries',
                            'Entries'
                        )
                });

            $body =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-body'
                });

            var $table =
                $(
                    '<table class="VAS_052_cashbook-table">' +
                    '<thead>' +
                    '<tr>' +
                    '<th class="VAS_052_cashbook-col-doc-no">' +
                    lbl(
                        'VAS_052_DocumentNo',
                        'Document No'
                    ) +
                    '</th>' +
                    '<th class="VAS_052_cashbook-col-cashbook">' +
                    lbl(
                        'VAS_052_CashBook',
                        'Cashbook'
                    ) +
                    '</th>' +
                    '<th class="VAS_052_cashbook-col-category">' +
                    lbl(
                        'VAS_052_Charge',
                        'Charge'
                    ) +
                    '</th>' +
                    '<th class="VAS_052_cashbook-col-cash-type">' +
                    lbl(
                        'VAS_052_CashType',
                        'Cash Type'
                    ) +
                    '</th>' +
                    '<th class="VAS_052_cashbook-col-amount">' +
                    lbl(
                        'VAS_052_In',
                        'In'
                    ) +
                    '</th>' +
                    '<th class="VAS_052_cashbook-col-amount">' +
                    lbl(
                        'VAS_052_Out',
                        'Out'
                    ) +
                    '</th>' +
                    '<th>' +
                    lbl(
                        'VAS_052_Description',
                        'Description'
                    ) +
                    '</th>' +
                    '</tr>' +
                    '</thead>' +
                    '<tbody id="VAS_052_cashbook-rows-' +
                    widgetId +
                    '"></tbody>' +
                    '</table>'
                );

            $footer =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-footer',

                    'style':
                        'display:none;'
                });

            $pageInfo =
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-footer-info'
                });

            var $pager =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-pager'
                });

            $prevBtn =
                $('<button>', {
                    'type':
                        'button',

                    'class':
                        'VAS_052_cashbook-page-btn VAS_052_cashbook-prev',

                    'aria-label':
                        lbl(
                            'VAS_052_Previous',
                            'Previous'
                        ),

                    'html':
                        chevL
                });

            $pageText =
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-page-text'
                });

            $nextBtn =
                $('<button>', {
                    'type':
                        'button',

                    'class':
                        'VAS_052_cashbook-page-btn VAS_052_cashbook-next',

                    'aria-label':
                        lbl(
                            'VAS_052_Next',
                            'Next'
                        ),

                    'html':
                        chevR
                });

            var $state =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-state',

                    'id':
                        'VAS_052_cashbook-state-' +
                        widgetId
                });

            $prevBtn.on(
                'click',
                function () {
                    if (pageNo <= 1) {
                        return;
                    }

                    pageNo--;

                    loadData();
                }
            );

            $nextBtn.on(
                'click',
                function () {
                    if (
                        totalPages <= 1 ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;

                    loadData();
                }
            );

            $pager
                .append($prevBtn)
                .append($pageText)
                .append($nextBtn);

            $footer
                .append($pageInfo)
                .append($pager);

            $labelGroup
                .append($title)
                .append($subtitle);

            $titleRow
                .append($icon)
                .append($labelGroup);

            $header.append($titleRow).append($meta);
            $body.append($table);

            /* Delegated on the body — the rows are replaced on every page / refresh.
               Enter / Space activate the link too (it is a real anchor, so the browser
               fires a click for Enter; Space is handled explicitly). */
            $body.on(
                'click',
                '.VAS_052_cashbook-doc-link',
                function (event) {
                    event.preventDefault();

                    zoomToCashJournal(
                        $(this).attr('data-cash-id')
                    );
                }
            );

            $body.on(
                'keydown',
                '.VAS_052_cashbook-doc-link',
                function (event) {
                    if (event.key !== ' ') {
                        return;
                    }

                    event.preventDefault();

                    zoomToCashJournal(
                        $(this).attr('data-cash-id')
                    );
                }
            );

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

            var widgetId =
                $self.AD_UserHomeWidgetID;

            totalRecords = 0;
            totalPages = 0;

            $root
                .find(
                    '#VAS_052_cashbook-state-' +
                    widgetId
                )
                .text(
                    message || ''
                )
                .addClass(
                    'is-visible'
                );

            $root
                .find(
                    '#VAS_052_cashbook-rows-' +
                    widgetId
                )
                .empty();

            updatePager();
        }

        function clearState() {
            if (!$root) {
                return;
            }

            var widgetId =
                $self.AD_UserHomeWidgetID;

            $root
                .find(
                    '#VAS_052_cashbook-state-' +
                    widgetId
                )
                .removeClass(
                    'is-visible'
                )
                .text('');
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(
                    totalPages > 1
                        ? (
                            pageNo +
                            ' ' +
                            lbl(
                                'VAS_052_Of',
                                'of'
                            ) +
                            ' ' +
                            totalPages
                        )
                        : ''
                );
            }

            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from =
                        (
                            pageNo - 1
                        ) *
                        pageSize +
                        1;

                    var to =
                        Math.min(
                            pageNo *
                            pageSize,
                            totalRecords
                        );

                    $pageInfo.text(
                        lbl(
                            'VAS_052_Showing',
                            'Showing'
                        ) +
                        ' ' +
                        from +
                        '–' +
                        to +
                        ' ' +
                        lbl(
                            'VAS_052_Of',
                            'of'
                        ) +
                        ' ' +
                        totalRecords
                    );
                }
                else {
                    $pageInfo.text('');
                }
            }

            if ($prevBtn) {
                $prevBtn.prop(
                    'disabled',
                    pageNo <= 1 ||
                    totalPages <= 1
                );
            }

            if ($nextBtn) {
                $nextBtn.prop(
                    'disabled',
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }

            if ($footer) {
                $footer.css(
                    'display',
                    totalPages > 1
                        ? 'flex'
                        : 'none'
                );
            }
        }

        /* Height of one rendered body row. Measured from the DOM so the capacity
           tracks the card's actual font scale (rows are ~34px at the default
           clamp, not the 42px the fallback assumes) — a fixed guess left a row
           or two of unused space on tall widgets. */
        function measureRowHeight() {
            var $sampleRow =
                $body
                    ? $body.find('tbody tr').first()
                    : null;

            var rowHeight =
                ($sampleRow && $sampleRow.length)
                    ? $sampleRow.outerHeight()
                    : 0;

            return rowHeight > 0
                ? rowHeight
                : ROW_HEIGHT_FALLBACK;
        }

        function updateAdaptivePageSize(shouldReload) {
            if (!$body || !$body[0]) {
                return;
            }

            var headerHeight =
                $body.find('thead').outerHeight() || 0;
            var availableHeight =
                Math.max(0, $body[0].clientHeight - headerHeight);
            var nextPageSize =
                Math.max(3, Math.floor(availableHeight / measureRowHeight()));

            if (nextPageSize === pageSize) {
                return;
            }

            var firstVisibleRecord =
                ((pageNo - 1) * pageSize) + 1;

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

            resizeObserver =
                new ResizeObserver(function () {
                    updateAdaptivePageSize(true);
                });

            resizeObserver.observe($body[0]);
        }

        function createEntryRow(
            entry,
            data
        ) {
            var cashTypeText =
                entry.cashTypeName ||
                entry.cashType ||
                entry.cashTypeValue ||
                '';

            var $row =
                $('<tr>');

            var documentNo =
                entry.documentNo ||
                '-';

            var cashJournalId =
                safeNumber(
                    entry.cCashId
                );

            var $documentCell =
                $('<td>', {
                    'class':
                        'VAS_052_cashbook-col-doc-no',

                    'title':
                        documentNo
                }).appendTo($row);

            /* Document No is a hyperlink that opens the cash journal record.
               Rendered as plain text when the row carries no C_Cash_ID (there is
               nothing to open). */
            if (cashJournalId > 0) {
                $('<a>', {
                    'href':
                        '#',

                    'class':
                        'VAS_052_cashbook-doc-link',

                    'role':
                        'link',

                    'data-cash-id':
                        cashJournalId,

                    'text':
                        documentNo,

                    'title':
                        lbl(
                            'VAS_052_OpenRecord',
                            'Open record'
                        ) +
                        ' - ' +
                        documentNo
                }).appendTo($documentCell);
            }
            else {
                $documentCell.text(documentNo);
            }

            var cashBookText =
                entry.cashBookName ||
                '-';

            $('<td>', {
                'class':
                    'VAS_052_cashbook-cashbook-cell ' +
                    'VAS_052_cashbook-col-cashbook',

                'text':
                    cashBookText,

                'title':
                    cashBookText
            }).appendTo($row);

            // Charge and Cash type read as plain text (no chip / pill). The cell
            // itself truncates with an ellipsis, so the full value rides along as
            // the native tooltip.
            var chargeText =
                entry.charge ||
                lbl(
                    'VAS_052_Other',
                    'Other'
                );

            $('<td>', {
                'class':
                    'VAS_052_cashbook-category-cell ' +
                    'VAS_052_cashbook-col-category',

                'text':
                    chargeText,

                'title':
                    chargeText
            }).appendTo($row);

            $('<td>', {
                'class':
                    'VAS_052_cashbook-cash-type-cell ' +
                    'VAS_052_cashbook-col-cash-type',

                'text':
                    cashTypeText ||
                    '-',

                'title':
                    cashTypeText ||
                    '-'
            }).appendTo($row);

            var $cashIn =
                $('<td>', {
                    'class':
                        'VAS_052_cashbook-col-amount ' +
                        'VAS_052_cashbook-in'
                }).appendTo($row);

            var $cashOut =
                $('<td>', {
                    'class':
                        'VAS_052_cashbook-col-amount ' +
                        'VAS_052_cashbook-out'
                }).appendTo($row);

            renderCurrencyAmount(
                $cashIn,
                entry.cashInAmount,
                data
            );

            renderCurrencyAmount(
                $cashOut,
                entry.cashOutAmount,
                data
            );

            $('<td>', {
                'class':
                    'VAS_052_cashbook-desc',

                'text':
                    entry.description ||
                    '',

                'title':
                    entry.description ||
                    ''
            }).appendTo($row);

            return $row;
        }

        function renderData(data) {
            if (
                !$root ||
                isDisposed
            ) {
                return;
            }

            var widgetId =
                $self.AD_UserHomeWidgetID;

            var entries =
                data.entries ||
                data.Entries ||
                [];

            var $rows =
                $root.find(
                    '#VAS_052_cashbook-rows-' +
                    widgetId
                );

            var responsePageNo =
                safeNumber(
                    data.pageNo ||
                    data.PageNo
                );

            var responsePageSize =
                safeNumber(
                    data.pageSize ||
                    data.PageSize
                );

            pageNo =
                responsePageNo > 0
                    ? responsePageNo
                    : pageNo;

            pageSize =
                responsePageSize > 0
                    ? responsePageSize
                    : pageSize;

            totalRecords =
                safeNumber(
                    data.totalRecords ||
                    data.TotalRecords ||
                    data.totalEntries ||
                    data.TotalEntries
                );

            totalPages =
                pageSize > 0
                    ? Math.ceil(
                        totalRecords /
                        pageSize
                    )
                    : 0;

            clearState();

            $root
                .find(
                    '#VAS_052_cashbook-title-' +
                    widgetId
                )
                .text(
                    data.title ||
                    data.Title ||
                    lbl(
                        'VAS_052_TodaysCashbook',
                        "Today's Cashbook"
                    )
                );

            $root
                .find(
                    '#VAS_052_cashbook-subtitle-' +
                    widgetId
                )
                .text(
                    data.subtitle ||
                    data.Subtitle ||
                    lbl(
                        'VAS_052_Subtitle',
                        "Today's Cash Journals in Base Currency"
                    )
                );

            $root
                .find(
                    '#VAS_052_cashbook-meta-' +
                    widgetId
                )
                .text(
                    data.metaText ||
                    data.MetaText ||
                    (
                        totalRecords +
                        ' ' +
                        lbl(
                            'VAS_052_Entries',
                            'entries'
                        )
                    )
                );

            $rows.empty();

            $.each(
                entries,
                function (
                    index,
                    entry
                ) {
                    $rows.append(
                        createEntryRow(
                            entry,
                            data
                        )
                    );
                }
            );

            if (
                data.hasData === false ||
                entries.length === 0
            ) {
                setState('No data');

                return;
            }

            updatePager();

            /* Rows are on screen now, so the capacity can be measured for real.
               updateAdaptivePageSize() no-ops when the size is unchanged, so this
               settles after at most one extra fetch. */
            updateAdaptivePageSize(true);
        }

        function loadData() {
            if (
                !$root ||
                isDisposed
            ) {
                return;
            }

            if (
                ajaxRequest &&
                ajaxRequest.readyState !== 4
            ) {
                ajaxRequest.abort();
            }

            showBusy(true);

            ajaxRequest =
                $.ajax({
                    url:
                        VIS.Application.contextUrl +
                        'VAS/VAS_052_TodaysCashbookCashJournal/GetTodaysCashbook',

                    type:
                        'GET',

                    dataType:
                        'json',

                    cache:
                        false,

                    data: {
                        pageNo:
                            pageNo,

                        pageSize:
                            pageSize
                    },

                    success:
                        function (response) {
                            if (isDisposed) {
                                return;
                            }

                            var data =
                                parseResponse(
                                    response
                                );

                            if (!data) {
                                setState(
                                    lbl(
                                        'VAS_052_LoadError',
                                        "Unable to load today's cashbook"
                                    )
                                );

                                return;
                            }

                            if (
                                data.error ===
                                'VAS_052_SessionExpired'
                            ) {
                                setState(
                                    lbl(
                                        'VAS_052_SessionExpired',
                                        'Session Expired'
                                    )
                                );

                                return;
                            }

                            if (
                                data.success === false ||
                                data.error
                            ) {
                                setState(
                                    data.error ||
                                    lbl(
                                        'VAS_052_LoadError',
                                        "Unable to load today's cashbook"
                                    )
                                );

                                return;
                            }

                            renderData(data);
                        },

                    error:
                        function (
                            xhr,
                            status,
                            errorThrown
                        ) {
                            if (!isDisposed) {
                                if (
                                    xhr &&
                                    xhr.responseText
                                ) {
                                    setState(
                                        xhr.responseText
                                    );
                                }
                                else {
                                    setState(
                                        errorThrown ||
                                        status ||
                                        lbl(
                                            'VAS_052_LoadError',
                                            "Unable to load today's cashbook"
                                        )
                                    );
                                }
                            }
                        },

                    complete:
                        function () {
                            if (
                                !isDisposed &&
                                $root
                            ) {
                                showBusy(false);
                            }
                        }
                });
        }

        this.initalize =
            function () {
                buildLayout();
                setupAdaptivePagination();
                loadData();
            };

        this.refreshWidget =
            function () {
                pageNo = 1;
                loadData();
            };

        /* Re-measure the row capacity when the dashboard resizes the widget —
           the framework's own size hook, in addition to the ResizeObserver. */
        this.handleSizeChange =
            function () {
                if (isDisposed) {
                    return;
                }

                updateAdaptivePageSize(true);
            };

        this.disposeComponent =
            function () {
                isDisposed = true;

                if (
                    ajaxRequest &&
                    ajaxRequest.readyState !== 4
                ) {
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
                $root = null;
                $self = null;
            };

        this.getRoot =
            function () {
                return $root;
            };
    };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    /* Relay a fired value (e.g. zoom TabWhereClause) to the registered host. */
    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
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

        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
        if (this.handleSizeChange) {
            this.handleSizeChange();
        }
    };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
