/**
 * Bounced AP Payment Widget
 * Purpose - Shows outgoing AP payments that were bounced or rejected
 * and need re-issue.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+------------------------------
 *  1  | Bounced                                           | VAS_030_MessageBounced
 *  2  | Need re-issue                                     | VAS_030_MessageNeedReissue
 *  3  | Loading                                           | VAS_030_MessageLoading
 *  4  | No Data                                           | VAS_030_MessageNoData
 *  5  | Bounced AP payments                               | VAS_030_MessageBouncedAPPayments
 *  6  | Could not load data                               | VAS_ErrorLoading
 *  7  | Showing                                           | VAS_Showing
 *  8  | of                                                | VAS_Of
 *  9  | Close                                             | VAS_Close
 * 10  | Payment No.                                       | VAS_030_MessagePaymentNo
 * 11  | Date                                              | VAS_Date
 * 12  | Vendor                                            | VAS_Vendor
 * 13  | Bank account                                      | VAS_BankAccount
 * 14  | Payment Currency                                  | VAS_PaymentCurrency
 * 15  | Amount                                            | VAS_Amount
 * 16  | Method                                            | VAS_Method
 * 17  | Status                                            | VAS_Status
 * 18  | Previous                                          | VAS_Previous
 * 19  | Next                                              | VAS_Next
 * ──────────────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_030_BouncedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var self = this;

        var $root = $(
            '<div class="vas-bounced-ap-payment-root">'
        );

        var $card = null;
        var $value = null;
        var $description = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;

        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;

        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var pageNo = 1;
        var pageSize = 10;
        var totalPages = 0;
        var totalRecords = 0;

        var rowsLoading = false;
        var isDisposed = false;

        /**
         * Returns translated message or fallback.
         */
        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== '[' + key + ']'
                ? text
                : fallback;
        }

        /**
         * Widget initialization.
         */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /**
         * Creates the main widget card.
         */
        function createWidget() {
            $card = $(
                '<div class="vas-bounced-ap-payment-card">'
            );

            $card.attr({
                role: 'button',
                tabindex: '0'
            });

            var $header = $(
                '<div class="vas-bounced-ap-payment-header">'
            );

            var $iconBox = $(
                '<div class="vas-bounced-ap-payment-icon-box">'
            );

            var $icon = $(
                '<svg ' +
                'class="vas-bounced-ap-payment-icon" ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'aria-hidden="true" ' +
                'focusable="false">' +
                '<path d="' +
                'M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3' +
                's-1 1-4 1-5-2-8-2-4 1-4 1z' +
                '"></path>' +
                '<line x1="4" y1="22" x2="4" y2="15"></line>' +
                '</svg>'
            );

            var $title = $(
                '<div class="vas-bounced-ap-payment-title">'
            ).text(
                lbl(
                    'VAS_030_MessageBounced',
                    'Bounced'
                )
            );

            var $arrow = $(
                '<span class="vas-bounced-ap-payment-arrow" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 18l6-6-6-6"></path>' +
                '</svg>' +
                '</span>'
            );

            $iconBox.append($icon);

            $header
                .append($iconBox)
                .append($title)
                .append($arrow);

            $body = $(
                '<div class="vas-bounced-ap-payment-body">'
            );

            $value = $(
                '<div class="vas-bounced-ap-payment-value">'
            );

            $body.append($value);

            $footer = $(
                '<div class="vas-bounced-ap-payment-footer">'
            );

            $description = $(
                '<div class="vas-bounced-ap-payment-desc">'
            ).text(
                lbl(
                    'VAS_030_MessageNeedReissue',
                    'Need re-issue'
                )
            );

            $footer.append($description);

            $busy = $(
                '<div class="vas-bounced-ap-payment-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $state = $(
                '<div class="vas-bounced-ap-payment-state-message">'
            );

            $card
                .append($header)
                .append($body)
                .append($footer)
                .append($busy)
                .append($state);

            $root
                .empty()
                .append($card);

            $card
                .off('click.vas030')
                .on(
                    'click.vas030',
                    openDialog
                );

            $card
                .off('keydown.vas030')
                .on(
                    'keydown.vas030',
                    function (e) {
                        if (
                            e.key === 'Enter' ||
                            e.key === ' '
                        ) {
                            openDialog(e);
                        }
                    }
                );

            createDialog();
        }

        /**
         * Loads the widget count.
         */
        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_030_BouncedAPPaymentWidget/' +
                    'GetBouncedAPPayments',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    _: new Date().getTime()
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(response);

                    if (!data) {
                        showState(
                            true,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );

                        return;
                    }

                    if (isErrorResponse(data)) {
                        showState(
                            true,
                            getErrorText(data)
                        );

                        return;
                    }

                    renderData(data);
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (isDisposed) {
                        return;
                    }

                    console.error(
                        'VAS_030 GetBouncedAPPayments error:',
                        {
                            status: status,
                            error: errorThrown,
                            responseText:
                                xhr
                                    ? xhr.responseText
                                    : ''
                        }
                    );

                    showState(
                        true,
                        getAjaxErrorText(xhr)
                    );
                },

                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        /**
         * Handles ASP.NET response formats.
         */
        function normalizeResponse(response) {
            var data = response;

            if (
                data &&
                typeof data === 'object' &&
                Object.prototype.hasOwnProperty.call(
                    data,
                    'd'
                )
            ) {
                data = data.d;
            }

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e) {
                    console.error(
                        'VAS_030 response parsing error:',
                        e,
                        data
                    );

                    return null;
                }
            }

            if (
                data &&
                typeof data === 'object' &&
                Object.prototype.hasOwnProperty.call(
                    data,
                    'd'
                )
            ) {
                data = data.d;

                if (typeof data === 'string') {
                    try {
                        data = JSON.parse(data);
                    }
                    catch (e2) {
                        console.error(
                            'VAS_030 response.d parsing error:',
                            e2,
                            data
                        );

                        return null;
                    }
                }
            }

            return data;
        }

        /**
         * Returns a property supporting camelCase and PascalCase.
         */
        function getResponseValue(
            data,
            camelName,
            pascalName,
            fallback
        ) {
            if (!data) {
                return fallback;
            }

            if (
                typeof data[camelName] !== 'undefined' &&
                data[camelName] !== null
            ) {
                return data[camelName];
            }

            if (
                typeof data[pascalName] !== 'undefined' &&
                data[pascalName] !== null
            ) {
                return data[pascalName];
            }

            return fallback;
        }

        /**
         * Checks whether response contains an error.
         */
        function isErrorResponse(data) {
            var error = getResponseValue(
                data,
                'error',
                'Error',
                false
            );

            return error === true ||
                String(error).toLowerCase() === 'true';
        }

        /**
         * Gets server error text.
         */
        function getErrorText(data) {
            return getResponseValue(
                data,
                'errorText',
                'ErrorText',
                lbl(
                    'VAS_ErrorLoading',
                    'Could not load data'
                )
            );
        }

        /**
         * Gets AJAX error text.
         */
        function getAjaxErrorText(xhr) {
            var fallback = lbl(
                'VAS_ErrorLoading',
                'Could not load data'
            );

            if (!xhr) {
                return fallback;
            }

            if (
                xhr.responseJSON &&
                xhr.responseJSON.errorText
            ) {
                return xhr.responseJSON.errorText;
            }

            if (xhr.responseText) {
                var response =
                    normalizeResponse(
                        xhr.responseText
                    );

                if (response) {
                    return getErrorText(response);
                }
            }

            return fallback;
        }

        /**
         * Escapes HTML values.
         */
        function escapeHtml(value) {
            return String(
                value == null
                    ? ''
                    : value
            )
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        /**
         * Renders widget count.
         */
        function renderData(data) {
            var count = Number(
                getResponseValue(
                    data,
                    'value',
                    'Value',
                    NaN
                )
            );

            if (isNaN(count)) {
                count = Number(
                    getResponseValue(
                        data,
                        'bouncedPaymentCount',
                        'BouncedPaymentCount',
                        0
                    )
                );
            }

            if (isNaN(count) || count <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            $value.text(
                formatCount(count)
            );

            var description =
                getResponseValue(
                    data,
                    'description',
                    'Description',
                    ''
                );

            if (
                $description &&
                description
            ) {
                $description.text(description);
            }
        }

        /**
         * Formats widget count.
         */
        function formatCount(value) {
            return Number(
                value || 0
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 0
                }
            );
        }

        /**
         * Shows or hides widget loading.
         */
        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass(
                    'is-visible',
                    !!show
                );
            }
        }

        /**
         * Shows widget state message.
         */
        function showState(show, message) {
            if ($state) {
                $state
                    .text(message || '')
                    .toggleClass(
                        'is-visible',
                        !!show
                    );
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($footer) {
                $footer.toggle(!show);
            }
        }

        /**
         * Shows no data state.
         */
        function setNoData() {
            showState(
                true,
                lbl(
                    'VAS_030_MessageNoData',
                    'No Data'
                )
            );
        }

        /**
         * Opens details dialog.
         */
        function openDialog(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (
                !$dialog ||
                !$dialog.length ||
                isDisposed
            ) {
                return;
            }

            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            rowsLoading = false;

            if ($dialogTbody) {
                $dialogTbody.empty();
            }

            $dialog
                .show()
                .attr(
                    'aria-hidden',
                    'false'
                );

            $('body').addClass(
                'vas-bounced-ap-payment-body-lock'
            );

            updatePager();
            loadRows();
        }

        /**
         * Closes details dialog.
         */
        function closeDialog(e) {
            if (e) {
                e.preventDefault();
                e.stopPropagation();
            }

            if (
                !$dialog ||
                !$dialog.length
            ) {
                return;
            }

            $dialog
                .hide()
                .attr(
                    'aria-hidden',
                    'true'
                );

            $('body').removeClass(
                'vas-bounced-ap-payment-body-lock'
            );

            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            rowsLoading = false;

            updatePager();
        }

        /**
         * Loads popup rows.
         */
        function loadRows() {
            if (
                isDisposed ||
                !$dialogTbody ||
                !$dialogTbody.length ||
                rowsLoading
            ) {
                return;
            }

            pageNo = Number(pageNo);
            pageSize = Number(pageSize);

            if (
                isNaN(pageNo) ||
                pageNo < 1
            ) {
                pageNo = 1;
            }

            if (
                isNaN(pageSize) ||
                pageSize < 1
            ) {
                pageSize = 10;
            }

            rowsLoading = true;

            renderLoadingRow();
            showDialogBusy(true);
            updatePager();

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_030_BouncedAPPaymentWidget/' +
                    'GetBouncedAPPaymentRows',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    pageNo: pageNo,
                    pageSize: pageSize,
                    _: new Date().getTime()
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(response);

                    console.log(
                        'VAS_030 GetBouncedAPPaymentRows response:',
                        data
                    );

                    if (!data) {
                        totalRecords = 0;
                        totalPages = 0;

                        renderErrorRow(
                            'Invalid response from server'
                        );

                        return;
                    }

                    if (isErrorResponse(data)) {
                        totalRecords = 0;
                        totalPages = 0;

                        var serverError =
                            getErrorText(data);

                        console.error(
                            'VAS_030 rows server error:',
                            serverError,
                            data
                        );

                        renderErrorRow(
                            serverError
                        );

                        return;
                    }

                    var rows =
                        getResponseValue(
                            data,
                            'rows',
                            'Rows',
                            []
                        );

                    if (!Array.isArray(rows)) {
                        rows = [];
                    }

                    totalRecords = Number(
                        getResponseValue(
                            data,
                            'totalRecords',
                            'TotalRecords',
                            rows.length
                        )
                    );

                    totalPages = Number(
                        getResponseValue(
                            data,
                            'totalPages',
                            'TotalPages',
                            0
                        )
                    );

                    var returnedPageNo =
                        Number(
                            getResponseValue(
                                data,
                                'pageNo',
                                'PageNo',
                                pageNo
                            )
                        );

                    if (
                        !isNaN(returnedPageNo) &&
                        returnedPageNo > 0
                    ) {
                        pageNo =
                            returnedPageNo;
                    }

                    if (
                        isNaN(totalRecords) ||
                        totalRecords < 0
                    ) {
                        totalRecords =
                            rows.length;
                    }

                    if (
                        isNaN(totalPages) ||
                        totalPages < 0
                    ) {
                        totalPages = 0;
                    }

                    if (
                        totalPages === 0 &&
                        totalRecords > 0
                    ) {
                        totalPages =
                            Math.ceil(
                                totalRecords /
                                pageSize
                            );
                    }

                    /*
                     * If current page exceeds last page,
                     * automatically request the last page.
                     */
                    if (
                        totalPages > 0 &&
                        pageNo > totalPages
                    ) {
                        pageNo = totalPages;
                        rowsLoading = false;
                        loadRows();
                        return;
                    }

                    renderRows(rows);
                    updatePager();
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (isDisposed) {
                        return;
                    }

                    totalRecords = 0;
                    totalPages = 0;

                    var message =
                        getAjaxErrorText(xhr);

                    console.error(
                        'VAS_030 GetBouncedAPPaymentRows AJAX error:',
                        {
                            status: status,
                            error: errorThrown,
                            responseText:
                                xhr
                                    ? xhr.responseText
                                    : ''
                        }
                    );

                    renderErrorRow(message);
                },

                complete: function () {
                    rowsLoading = false;

                    showDialogBusy(false);
                    updatePager();
                }
            });
        }

        /**
         * Renders loading row.
         */
        function renderLoadingRow() {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.html(
                '<tr>' +
                '<td ' +
                'class="vas-bounced-ap-payment-dialog-empty" ' +
                'colspan="8">' +
                escapeHtml(
                    lbl(
                        'VAS_030_MessageLoading',
                        'Loading'
                    )
                ) +
                '</td>' +
                '</tr>'
            );
        }

        /**
         * Renders error row.
         */
        function renderErrorRow(message) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.html(
                '<tr>' +
                '<td ' +
                'class="vas-bounced-ap-payment-dialog-empty" ' +
                'colspan="8">' +
                escapeHtml(
                    message ||
                    lbl(
                        'VAS_ErrorLoading',
                        'Could not load data'
                    )
                ) +
                '</td>' +
                '</tr>'
            );
        }

        /**
         * Renders popup rows.
         */
        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (
                !Array.isArray(rows) ||
                rows.length === 0
            ) {
                $dialogTbody.html(
                    '<tr>' +
                    '<td ' +
                    'class="vas-bounced-ap-payment-dialog-empty" ' +
                    'colspan="8">' +
                    escapeHtml(
                        lbl(
                            'VAS_030_MessageNoData',
                            'No Data'
                        )
                    ) +
                    '</td>' +
                    '</tr>'
                );

                return;
            }

            for (
                var i = 0;
                i < rows.length;
                i++
            ) {
                var row = rows[i] || {};

                var paymentNo =
                    getResponseValue(
                        row,
                        'paymentNo',
                        'PaymentNo',
                        ''
                    );

                if (!paymentNo) {
                    paymentNo =
                        getResponseValue(
                            row,
                            'documentNo',
                            'DocumentNo',
                            ''
                        );
                }

                var paymentDate =
                    getResponseValue(
                        row,
                        'paymentDate',
                        'PaymentDate',
                        ''
                    );

                if (!paymentDate) {
                    paymentDate =
                        getResponseValue(
                            row,
                            'date',
                            'Date',
                            ''
                        );
                }

                var vendorName =
                    getResponseValue(
                        row,
                        'vendorName',
                        'VendorName',
                        ''
                    );

                if (!vendorName) {
                    vendorName =
                        getResponseValue(
                            row,
                            'supplier',
                            'Supplier',
                            ''
                        );
                }

                var bankName =
                    getResponseValue(
                        row,
                        'bankName',
                        'BankName',
                        ''
                    );

                var accountNo =
                    getResponseValue(
                        row,
                        'accountNo',
                        'AccountNo',
                        ''
                    );

                var currency =
                    getResponseValue(
                        row,
                        'currency',
                        'Currency',
                        ''
                    );

                if (!currency) {
                    currency =
                        getResponseValue(
                            row,
                            'currencyISO',
                            'CurrencyISO',
                            ''
                        );
                }

                if (!currency) {
                    currency =
                        getResponseValue(
                            row,
                            'paymentCurrency',
                            'PaymentCurrency',
                            ''
                        );
                }

                var currencySymbol =
                    getResponseValue(
                        row,
                        'currencySymbol',
                        'CurrencySymbol',
                        ''
                    );

                if (!currencySymbol) {
                    currencySymbol =
                        getResponseValue(
                            row,
                            'paymentCurrencySymbol',
                            'PaymentCurrencySymbol',
                            currency
                        );
                }

                var amount =
                    getResponseValue(
                        row,
                        'amount',
                        'Amount',
                        0
                    );

                var method =
                    getResponseValue(
                        row,
                        'method',
                        'Method',
                        ''
                    );

                if (!method) {
                    method =
                        getResponseValue(
                            row,
                            'paymentMethodName',
                            'PaymentMethodName',
                            ''
                        );
                }

                if (!method) {
                    method =
                        getResponseValue(
                            row,
                            'tenderTypeName',
                            'TenderTypeName',
                            ''
                        );
                }

                var status =
                    getResponseValue(
                        row,
                        'status',
                        'Status',
                        ''
                    );

                if (!status) {
                    status =
                        getResponseValue(
                            row,
                            'statusName',
                            'StatusName',
                            ''
                        );
                }

                if (!status) {
                    status =
                        getResponseValue(
                            row,
                            'executionStatus',
                            'ExecutionStatus',
                            ''
                        );
                }

                var precision =
                    Number(
                        getResponseValue(
                            row,
                            'stdPrecision',
                            'StdPrecision',
                            2
                        )
                    );

                if (
                    isNaN(precision) ||
                    precision < 0
                ) {
                    precision = 2;
                }

                var dateText =
                    formatDate(paymentDate);

                var bankText =
                    formatBank({
                        bankName: bankName,
                        accountNo: accountNo
                    });

                var amountText =
                    formatAmountWithPrecision(
                        amount,
                        precision
                    );

                var amountHtml =
                    currencySymbol
                        ? '<span class="' +
                        'vas-bounced-ap-payment-currency-inline">' +
                        escapeHtml(currencySymbol) +
                        '</span> ' +
                        escapeHtml(amountText)
                        : escapeHtml(amountText);

                var statusHtml =
                    status
                        ? '<span class="' +
                        'vas-bounced-ap-payment-status-chip">' +
                        escapeHtml(status) +
                        '</span>'
                        : '';

                $dialogTbody.append(
                    '<tr>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-doc" ' +
                    'title="' +
                    escapeHtml(paymentNo) +
                    '">' +
                    escapeHtml(paymentNo) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-date" ' +
                    'title="' +
                    escapeHtml(dateText) +
                    '">' +
                    escapeHtml(dateText) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-vendor" ' +
                    'title="' +
                    escapeHtml(vendorName) +
                    '">' +
                    escapeHtml(vendorName) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-bank" ' +
                    'title="' +
                    escapeHtml(bankText) +
                    '">' +
                    escapeHtml(bankText) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-currency" ' +
                    'title="' +
                    escapeHtml(currency) +
                    '">' +
                    escapeHtml(currency) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-amount" ' +
                    'title="' +
                    escapeHtml(
                        (
                            currencySymbol
                                ? currencySymbol + ' '
                                : ''
                        ) +
                        amountText
                    ) +
                    '">' +
                    amountHtml +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-method" ' +
                    'title="' +
                    escapeHtml(method) +
                    '">' +
                    escapeHtml(method) +
                    '</td>' +

                    '<td ' +
                    'class="vas-bounced-ap-payment-td-status">' +
                    statusHtml +
                    '</td>' +

                    '</tr>'
                );
            }
        }

        /**
         * Updates pagination information.
         */
        function updatePager() {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var from =
                        (pageNo - 1) *
                        pageSize +
                        1;

                    var to =
                        Math.min(
                            (pageNo - 1) *
                            pageSize +
                            pageSize,
                            totalRecords
                        );

                    $pagerHelper.text(
                        lbl(
                            'VAS_Showing',
                            'Showing'
                        ) +
                        ' ' +
                        from +
                        '-' +
                        to +
                        ' ' +
                        lbl(
                            'VAS_Of',
                            'of'
                        ) +
                        ' ' +
                        totalRecords
                    );
                }
                else {
                    $pagerHelper.text('');
                }
            }

            if ($pagerText) {
                $pagerText.text(
                    totalPages > 0
                        ? pageNo +
                        ' ' +
                        lbl(
                            'VAS_Of',
                            'of'
                        ) +
                        ' ' +
                        totalPages
                        : ''
                );
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    'disabled',
                    rowsLoading ||
                    pageNo <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    'disabled',
                    rowsLoading ||
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }
        }

        /**
         * Shows or hides dialog loading state.
         */
        function showDialogBusy(show) {
            if ($dialogBusy) {
                $dialogBusy.toggleClass(
                    'is-visible',
                    !!show
                );
            }
        }

        /**
         * Creates details dialog.
         */
        function createDialog() {
            $dialog = $(
                '<div ' +
                'class="vas-bounced-ap-payment-dialog" ' +
                'style="display:none;" ' +
                'role="dialog" ' +
                'aria-modal="true" ' +
                'aria-hidden="true">' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-scrim">' +
                '</div>' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-card">' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-header">' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-icon">' +

                '<svg ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="1.8" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<circle ' +
                'cx="12" cy="12" r="10"/>' +

                '<line ' +
                'x1="12" y1="8" ' +
                'x2="12" y2="13"/>' +

                '<line ' +
                'x1="12" y1="16" ' +
                'x2="12.01" y2="16"/>' +

                '</svg>' +
                '</div>' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-title-group">' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-title">' +
                escapeHtml(
                    lbl(
                        'VAS_030_MessageBouncedAPPayments',
                        'Bounced AP payments'
                    )
                ) +
                '</div>' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-subtitle">' +
                escapeHtml(
                    lbl(
                        'VAS_030_MessageNeedReissue',
                        'Need re-issue'
                    )
                ) +
                '</div>' +

                '</div>' +

                '<button ' +
                'type="button" ' +
                'class="' +
                'vas-bounced-ap-payment-dialog-close" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Close',
                        'Close'
                    )
                ) +
                '">' +

                '<svg ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="1.8" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<line ' +
                'x1="18" y1="6" ' +
                'x2="6" y2="18"/>' +

                '<line ' +
                'x1="6" y1="6" ' +
                'x2="18" y2="18"/>' +

                '</svg>' +
                '</button>' +

                '</div>' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-body">' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>' +

                '<table class="' +
                'vas-bounced-ap-payment-dialog-table">' +

                '<thead>' +
                '<tr>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_030_MessagePaymentNo',
                        'Payment No.'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_Date',
                        'Date'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_Vendor',
                        'Vendor'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_BankAccount',
                        'Bank account'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_PaymentCurrency',
                        'Payment Currency'
                    )
                ) +
                '</th>' +

                '<th class="' +
                'vas-bounced-ap-payment-th-amount">' +
                escapeHtml(
                    lbl(
                        'VAS_Amount',
                        'Amount'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_Method',
                        'Method'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_Status',
                        'Status'
                    )
                ) +
                '</th>' +

                '</tr>' +
                '</thead>' +

                '<tbody class="' +
                'vas-bounced-ap-payment-dialog-tbody">' +
                '</tbody>' +

                '</table>' +
                '</div>' +

                '<div class="' +
                'vas-bounced-ap-payment-dialog-footer">' +

                '<span class="' +
                'vas-bounced-ap-payment-pager-helper">' +
                '</span>' +

                '<div class="' +
                'vas-bounced-ap-payment-pager">' +

                '<button ' +
                'type="button" ' +
                'class="' +
                'vas-bounced-ap-payment-pager-btn ' +
                'vas-bounced-ap-payment-pager-prev" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Previous',
                        'Previous'
                    )
                ) +
                '">' +
                '‹' +
                '</button>' +

                '<span class="' +
                'vas-bounced-ap-payment-pager-text">' +
                '</span>' +

                '<button ' +
                'type="button" ' +
                'class="' +
                'vas-bounced-ap-payment-pager-btn ' +
                'vas-bounced-ap-payment-pager-next" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Next',
                        'Next'
                    )
                ) +
                '">' +
                '›' +
                '</button>' +

                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody =
                $dialog.find(
                    '.vas-bounced-ap-payment-dialog-tbody'
                );

            $dialogBusy =
                $dialog.find(
                    '.vas-bounced-ap-payment-dialog-busy'
                );

            $pagerHelper =
                $dialog.find(
                    '.vas-bounced-ap-payment-pager-helper'
                );

            $pagerPrev =
                $dialog.find(
                    '.vas-bounced-ap-payment-pager-prev'
                );

            $pagerNext =
                $dialog.find(
                    '.vas-bounced-ap-payment-pager-next'
                );

            $pagerText =
                $dialog.find(
                    '.vas-bounced-ap-payment-pager-text'
                );

            $dialog
                .find(
                    '.vas-bounced-ap-payment-dialog-close'
                )
                .off('click.vas030')
                .on(
                    'click.vas030',
                    closeDialog
                );

            $dialog
                .find(
                    '.vas-bounced-ap-payment-dialog-scrim'
                )
                .off('click.vas030')
                .on(
                    'click.vas030',
                    closeDialog
                );

            $pagerPrev
                .off('click.vas030')
                .on(
                    'click.vas030',
                    function (e) {
                        e.preventDefault();
                        e.stopPropagation();

                        if (
                            rowsLoading ||
                            pageNo <= 1
                        ) {
                            return;
                        }

                        pageNo--;
                        loadRows();
                    }
                );

            $pagerNext
                .off('click.vas030')
                .on(
                    'click.vas030',
                    function (e) {
                        e.preventDefault();
                        e.stopPropagation();

                        if (
                            rowsLoading ||
                            totalPages <= 0 ||
                            pageNo >= totalPages
                        ) {
                            return;
                        }

                        pageNo++;
                        loadRows();
                    }
                );

            $(document)
                .off(
                    'keydown.vas-bounced-ap-payment-' +
                    self.AD_UserHomeWidgetID
                )
                .on(
                    'keydown.vas-bounced-ap-payment-' +
                    self.AD_UserHomeWidgetID,
                    function (e) {
                        if (
                            e.key === 'Escape' &&
                            $dialog &&
                            $dialog.is(':visible')
                        ) {
                            closeDialog(e);
                        }
                    }
                );

            $('body').append($dialog);
        }

        /**
         * Formats date value.
         */
        function formatDate(value) {
            if (!value) {
                return '';
            }

            /*
             * Controller returns yyyy-MM-dd.
             * Parse it locally to avoid UTC timezone shifting.
             */
            var isoMatch =
                /^(\d{4})-(\d{2})-(\d{2})$/
                    .exec(String(value));

            var date;

            if (isoMatch) {
                date = new Date(
                    Number(isoMatch[1]),
                    Number(isoMatch[2]) - 1,
                    Number(isoMatch[3])
                );
            }
            else {
                date = new Date(value);
            }

            if (isNaN(date.getTime())) {
                return String(value);
            }

            return date.toLocaleDateString(
                window.navigator.language,
                {
                    day: '2-digit',
                    month: 'short',
                    year: 'numeric'
                }
            );
        }

        /**
         * Formats amount using the row currency precision.
         */
        function formatAmountWithPrecision(
            value,
            precision
        ) {
            var amount = Number(value || 0);
            var digits = Number(precision);

            if (isNaN(amount)) {
                amount = 0;
            }

            if (
                isNaN(digits) ||
                digits < 0
            ) {
                digits = 2;
            }

            return amount.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits:
                        digits,
                    maximumFractionDigits:
                        digits
                }
            );
        }

        /**
         * Formats bank name and last four account digits.
         */
        function formatBank(row) {
            var bankName =
                row &&
                    row.bankName
                    ? String(
                        row.bankName
                    ).trim()
                    : '';

            var accountNo =
                row &&
                    row.accountNo
                    ? String(
                        row.accountNo
                    ).trim()
                    : '';

            var last4 =
                accountNo
                    ? (
                        accountNo.length > 4
                            ? accountNo.slice(-4)
                            : accountNo
                    )
                    : '';

            if (
                bankName &&
                last4
            ) {
                return (
                    bankName +
                    ' - ****' +
                    last4
                );
            }

            if (bankName) {
                return bankName;
            }

            if (last4) {
                return '****' + last4;
            }

            return '';
        }

        /**
         * Refreshes widget data.
         */
        this.refreshData = function () {
            if (isDisposed) {
                return;
            }

            loadData();

            if (
                $dialog &&
                $dialog.is(':visible')
            ) {
                pageNo = 1;
                loadRows();
            }
        };

        /**
         * Returns root element.
         */
        this.getRoot = function () {
            return $root;
        };

        /**
         * Disposes widget.
         */
        this.disposeComponent = function () {
            isDisposed = true;

            $(document).off(
                'keydown.vas-bounced-ap-payment-' +
                this.AD_UserHomeWidgetID
            );

            $('body').removeClass(
                'vas-bounced-ap-payment-body-lock'
            );

            if ($card) {
                $card.off('.vas030');
            }

            if ($pagerPrev) {
                $pagerPrev.off('.vas030');
            }

            if ($pagerNext) {
                $pagerNext.off('.vas030');
            }

            if ($dialog) {
                $dialog
                    .find('*')
                    .off('.vas030');

                $dialog.remove();
            }

            $root.remove();

            $card = null;
            $value = null;
            $description = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;

            $dialog = null;
            $dialogTbody = null;
            $dialogBusy = null;

            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
        };
    };

    /**
     * Widget init.
     */
    VAS.VAS_030_BouncedAPPaymentWidget.prototype.init =
        function (windowNo, frame) {
            this.frame = frame;
            this.windowNo = windowNo;

            if (
                frame &&
                frame.widgetInfo
            ) {
                this.AD_UserHomeWidgetID =
                    frame.widgetInfo
                        .AD_UserHomeWidgetID;
            }

            this.Initalize();

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

    /**
     * Handles widget size changes.
     */
    VAS.VAS_030_BouncedAPPaymentWidget.prototype
        .widgetSizeChange =
        function (height, width) {
            var $root =
                this.getRoot();

            if (!$root) {
                return;
            }

            $root.toggleClass(
                'vas-bounced-ap-payment-compact',
                (
                    width &&
                    width < 240
                ) ||
                (
                    height &&
                    height < 160
                )
            );
        };

    /**
     * Refreshes widget.
     */
    VAS.VAS_030_BouncedAPPaymentWidget.prototype
        .refreshWidget =
        function () {
            this.refreshData();
        };

    /**
     * Disposes widget.
     */
    VAS.VAS_030_BouncedAPPaymentWidget.prototype.dispose =
        function () {
            this.disposeComponent();

            if (
                this.frame &&
                this.frame.dispose
            ) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);
