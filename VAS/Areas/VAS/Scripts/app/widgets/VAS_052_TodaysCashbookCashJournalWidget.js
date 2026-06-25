
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
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_052_TodaysCashbookCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        var $footer = null;
        var $pageInfo = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $pageText = null;

        var pageNo = 1;
        var pageSize = 3;
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

            var symbol =
                data &&
                data.currencySymbol
                    ? data.currencySymbol
                    : '';

            var iso =
                data &&
                (
                    data.currencyISO ||
                    data.currencyISOCode
                )
                    ? (
                        data.currencyISO ||
                        data.currencyISOCode
                    )
                    : '';

            if (numericValue === 0) {
                return '-';
            }

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

            if (symbol) {
                return sign +
                    symbol +
                    amount;
            }

            return iso
                ? sign + iso + amount
                : sign + amount;
        }

        function getCurrencyLabel(data) {
            if (
                data &&
                data.currencySymbol
            ) {
                return data.currencySymbol;
            }

            return data &&
                (
                    data.currencyISO ||
                    data.currencyISOCode
                )
                ? (
                    data.currencyISO ||
                    data.currencyISOCode
                )
                : '';
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

            var $body =
                $('<div>', {
                    'class':
                        'VAS_052_cashbook-body'
                });

            var $table =
                $(
                    '<table class="VAS_052_cashbook-table">' +
                    '<thead>' +
                
                    '<th class="VAS_052_cashbook-col-doc-no">' +
                    lbl(
                        'VAS_052_DocumentNo',
                        'Document No'
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
                    '<th>' +
                    lbl(
                        'VAS_052_PostedBy',
                        'Posted by'
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

            $titleRow
                .append($icon)
                .append($title)
                .append($meta);

            $header.append($titleRow);
            $body.append($table);

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

        function createEntryRow(
            entry,
            data
        ) {
            var categoryClass =
                entry.categoryClass ||
                'other';

            var cashTypeText =
                entry.cashTypeName ||
                entry.cashType ||
                entry.cashTypeValue ||
                '';

            var $row =
                $('<tr>');

          

            $('<td>', {
                'class':
                    'VAS_052_cashbook-col-doc-no',

                'text':
                    entry.documentNo ||
                    '-',

                'title':
                    entry.documentNo ||
                    '-'
            }).appendTo($row);

            var $categoryCell =
                $('<td>', {
                    'class':
                        'VAS_052_cashbook-category-cell ' +
                        'VAS_052_cashbook-col-category'
                });

            $categoryCell.append(
                $('<span>', {
                    'class':
                        'VAS_052_cashbook-chip ' +
                        'VAS_052_cashbook-chip-' +
                        categoryClass,

                    'text':
                        entry.charge ||
                        lbl(
                            'VAS_052_Other',
                            'Other'
                        ),

                    'title':
                        entry.charge ||
                        lbl(
                            'VAS_052_Other',
                            'Other'
                        )
                })
            );

            $categoryCell.appendTo($row);

            var $cashTypeCell =
                $('<td>', {
                    'class':
                        'VAS_052_cashbook-cash-type-cell ' +
                        'VAS_052_cashbook-col-cash-type'
                });

            if (cashTypeText) {
                $cashTypeCell.append(
                    $('<span>', {
                        'class':
                            'VAS_052_cashbook-chip ' +
                            'VAS_052_cashbook-chip-other ' +
                            'VAS_052_cashbook-cash-type',

                        'text':
                            cashTypeText,

                        'title':
                            cashTypeText
                    })
                );
            }
            else {
                $cashTypeCell.text('-');
            }

            $cashTypeCell.appendTo($row);

            $('<td>', {
                'class':
                    'VAS_052_cashbook-posted',

                'text':
                    entry.postedBy ||
                    lbl(
                        'VAS_052_System',
                        'System'
                    ),

                'title':
                    entry.postedBy ||
                    lbl(
                        'VAS_052_System',
                        'System'
                    )
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
                setState(
                    lbl(
                        'VAS_052_NoData',
                        'No data'
                    )
                );

                return;
            }

            updatePager();
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
                loadData();
            };

        this.refreshWidget =
            function () {
                pageNo = 1;
                loadData();
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

                ajaxRequest = null;
                $root = null;
                $self = null;
            };

        this.getRoot =
            function () {
                return $root;
            };
    };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.frame =
                frame;

            this.windowNo =
                windowNo;

            if (
                frame &&
                frame.widgetInfo
            ) {
                this.AD_UserHomeWidgetID =
                    frame.widgetInfo.AD_UserHomeWidgetID;
            }

            if (!this.AD_UserHomeWidgetID) {
                this.AD_UserHomeWidgetID =
                    windowNo ||
                    new Date().getTime();
            }

            this.initalize();

            if (
                this.frame &&
                this.frame.getContentGrid
            ) {
                this.frame
                    .getContentGrid()
                    .append(
                        this.getRoot()
                    );
            }
        };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_052_TodaysCashbookCashJournalWidget.prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };
})(VAS, jQuery);
