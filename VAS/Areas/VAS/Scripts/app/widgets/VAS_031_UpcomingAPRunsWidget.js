/**
 * Upcoming runs
 * Purpose - Displays upcoming AP payment runs due within the next 7 days,
 * grouped by payment method and due date.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Upcoming runs                        | VAS_031_MessageUpcomingRuns
 *  2  | Next 7 days                          | VAS_031_MessageNext7Days
 *  3  | payment                              | VAS_031_MessagePayment
 *  4  | payments                             | VAS_031_MessagePayments
 *  5  | Loading                              | VAS_031_MessageLoading
 *  6  | No Data                              | VAS_031_MessageNoData
 *  7  | Previous                             | VAS_Previous
 *  8  | Next                                 | VAS_Next
 *  9  | Could not load data                  | VAS_ErrorLoading
 * 10  | Of                                   | VAS_Of
 * 11  | Not Specified                        | VAS_031_MessageNotSpecified
 * 12  | Pay                                  | VAS_031_MessagePay
 * 13  | Create Payment                       | VAS_031_MessageCreatePayment
 * 14  | Pre-filled from upcoming             | VAS_031_MessagePrefilledFromUpcoming
 * 15  | Loading payment details              | VAS_031_MessageLoadingDetails
 * 16  | Organization                         | VAS_031_MessageOrganization
 * 17  | Bank Account                         | VAS_031_MessageBankAccount
 * 18  | Transaction Date                     | VAS_031_MessageTransactionDate
 * 19  | Vendor                               | VAS_031_MessageVendor
 * 20  | Currency                             | VAS_PaymentCurrency
 * 21  | Currency Type                        | VAS_031_MessageCurrencyType
 * 22  | Spot                                 | VAS_031_MessageSpot
 * 23  | Document Type                        | VAS_031_MessageDocumentType
 * 24  | Tender Type                          | VAS_031_MessageTenderType
 * 25  | Payment Amount                       | VAS_031_MessagePaymentAmount
 * 26  | Document No.                         | VAS_031_MessageDocumentNo
 * 27  | PRE-FILLED                           | VAS_031_MessagePrefilled
 * 28  | Select                               | VAS_Select
 * 29  | Pre-filled for invoice               | VAS_031_MessagePrefilledForInvoice
 * 30  | Review and save.                     | VAS_031_MessageReviewAndSave
 * 31  | Organization is required.            | VAS_031_MessageOrganizationRequired
 * 32  | Bank account is required.            | VAS_031_MessageBankAccountRequired
 * 33  | Vendor is required.                  | VAS_031_MessageVendorRequired
 * 34  | Currency is required.                | VAS_031_MessageCurrencyRequired
 * 35  | Currency type is required.           | VAS_031_MessageConversionTypeRequired
 * 36  | Document type is required.           | VAS_031_MessageDocumentTypeRequired
 * 37  | Tender type is required.             | VAS_031_MessageTenderTypeRequired
 * 38  | Transaction date is required.        | VAS_031_MessageTransactionDateRequired
 * 39  | Payment amount must be greater...    | VAS_031_MessagePaymentAmountRequired
 * 40  | Saving                               | VAS_031_MessageSaving
 * 41  | Save payment                         | VAS_031_MessageSavePayment
 * 42  | Close                                | VAS_Close
 * 43  | Generated from Upcoming · 7 days     | VAS_031_MessageGeneratedFromUpcoming
 * 44  | Cancel                               | VAS_Cancel
 * 45  | Transaction date must be in...       | VAS_031_MessageTransactionDateInvalid
 * 46  | Could not save AP payment            | VAS_031_MessageCouldNotSaveAPPayment
 * 47  | Upcoming AP payment created...       | VAS_031_MessagePaymentCreatedSuccessfully
 * ─────────────────────────────────────────────────────────────────────
 */

/**
* Upcoming runs
* Purpose - Displays upcoming AP payment runs due within the next 7 days
* and creates a new AP payment from a selected upcoming payment.
*/

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_031_UpcomingAPRunsWidget = function () {

        var $self = this;

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $(
            '<div class="vas-upcoming-ap-runs-root">'
        );

        var $card = null;
        var $body = null;
        var $pager = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $busy = null;
        var $state = null;

        var $payDialog = null;
        var $payDialogTitle = null;
        var $payDialogSub = null;
        var $payDialogNotice = null;
        var $payDialogGrid = null;
        var $payDialogSave = null;
        var $payDialogSaveLabel = null;
        var $payDialogBusy = null;

        var isDisposed = false;
        var saveInProgress = false;

        var runsData = [];
        var paymentRows = [];
        var popupLookups = null;

        var selectedRun = null;
        var selectedPaymentRow = null;

        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== '[' + key + ']'
                ? text
                : fallback;
        }

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

        function normalizeResponse(response) {
            var data = response;
            var i;

            for (i = 0; i < 2; i++) {

                if (typeof data !== 'string') {
                    break;
                }

                try {
                    data = JSON.parse(data);
                }
                catch (error) {
                    return null;
                }
            }

            return data;
        }

        function getAjaxErrorMessage(
            xhr,
            fallback
        ) {
            var response;

            if (!xhr) {
                return fallback;
            }

            response = normalizeResponse(
                xhr.responseText
            );

            if (!response) {
                return fallback;
            }

            return (
                response.error ||
                response.errorText ||
                response.message ||
                fallback
            );
        }

        function firstValue() {
            var i;
            var value;

            for (
                i = 0;
                i < arguments.length;
                i++
            ) {
                value = arguments[i];

                if (
                    value !== undefined &&
                    value !== null &&
                    String(value).trim() !== ''
                ) {
                    return value;
                }
            }

            return '';
        }

        function firstPositiveValue() {
            var i;
            var value;
            var numericValue;

            for (
                i = 0;
                i < arguments.length;
                i++
            ) {
                value = arguments[i];

                if (
                    value === undefined ||
                    value === null ||
                    String(value).trim() === ''
                ) {
                    continue;
                }

                numericValue = Number(value);

                if (
                    !isNaN(numericValue) &&
                    numericValue > 0
                ) {
                    return value;
                }
            }

            return '';
        }

        function getLookup(name) {
            if (
                popupLookups &&
                $.isArray(
                    popupLookups[name]
                )
            ) {
                return popupLookups[name];
            }

            return [];
        }

        function getLookupAny(names) {
            var i;
            var items;

            names = $.isArray(names)
                ? names
                : [];

            for (
                i = 0;
                i < names.length;
                i++
            ) {
                items = getLookup(
                    names[i]
                );

                if (items.length > 0) {
                    return items;
                }
            }

            return [];
        }

        function getLookupItemValue(
            item,
            valueProp
        ) {
            item = item || {};

            if (
                valueProp &&
                item[valueProp] !== undefined &&
                item[valueProp] !== null
            ) {
                return item[valueProp];
            }

            if (
                item.id !== undefined &&
                item.id !== null
            ) {
                return item.id;
            }

            if (
                item.value !== undefined &&
                item.value !== null
            ) {
                return item.value;
            }

            if (
                item.key !== undefined &&
                item.key !== null
            ) {
                return item.key;
            }

            if (
                item.code !== undefined &&
                item.code !== null
            ) {
                return item.code;
            }

            return '';
        }

        function getLookupItemText(
            item,
            textProp,
            value
        ) {
            item = item || {};

            if (
                textProp &&
                item[textProp] !== undefined &&
                item[textProp] !== null
            ) {
                return item[textProp];
            }

            return firstValue(
                item.name,
                item.text,
                item.label,
                item.description,
                item.documentNo,
                item.code,
                value
            );
        }

        /*
         * ترجع القيمة فقط إذا كانت موجودة فعليًا
         * داخل اللستة القادمة من الباك.
         */
        function getValidLookupValue(
            items,
            requestedValue
        ) {
            var i;
            var itemValue;

            items = $.isArray(items)
                ? items
                : [];

            if (
                requestedValue === undefined ||
                requestedValue === null ||
                String(requestedValue).trim() === '' ||
                String(requestedValue).trim() === '0'
            ) {
                return '';
            }

            for (
                i = 0;
                i < items.length;
                i++
            ) {
                itemValue =
                    getLookupItemValue(
                        items[i]
                    );

                if (
                    String(itemValue) ===
                    String(requestedValue)
                ) {
                    return itemValue;
                }
            }

            return '';
        }

        function getFirstLookupValue(items) {
            var i;
            var value;

            items = $.isArray(items)
                ? items
                : [];

            for (
                i = 0;
                i < items.length;
                i++
            ) {
                value =
                    getLookupItemValue(
                        items[i]
                    );

                if (
                    value !== undefined &&
                    value !== null &&
                    String(value).trim() !== ''
                ) {
                    return value;
                }
            }

            return '';
        }

        function getFirstPositiveLookupValue(
            items
        ) {
            var i;
            var value;
            var numericValue;

            items = $.isArray(items)
                ? items
                : [];

            for (
                i = 0;
                i < items.length;
                i++
            ) {
                value =
                    getLookupItemValue(
                        items[i]
                    );

                numericValue = Number(value);

                if (
                    !isNaN(numericValue) &&
                    numericValue > 0
                ) {
                    return value;
                }
            }

            return '';
        }

        /*
         * لا تتم إضافة fallback option.
         * جميع الخيارات يجب أن تأتي من الباك.
         */
        function selectHtml(
            fieldName,
            items,
            selectedValue,
            textProp,
            valueProp
        ) {
            var html;
            var i;
            var item;
            var value;
            var text;
            var selected;

            items = $.isArray(items)
                ? items
                : [];

            html =
                '<select ' +
                'class="vas-upcoming-ap-runs-edit-control" ' +
                'data-pay-field="' +
                escapeHtml(fieldName) +
                '">';

            html +=
                '<option value="">' +
                escapeHtml(
                    lbl(
                        'VAS_Select',
                        'Select'
                    )
                ) +
                '</option>';

            for (
                i = 0;
                i < items.length;
                i++
            ) {
                item = items[i] || {};

                value =
                    getLookupItemValue(
                        item,
                        valueProp
                    );

                if (
                    value === undefined ||
                    value === null ||
                    String(value).trim() === ''
                ) {
                    continue;
                }

                text =
                    getLookupItemText(
                        item,
                        textProp,
                        value
                    );

                selected =
                    String(value) ===
                    String(selectedValue);

                html +=
                    '<option value="' +
                    escapeHtml(value) +
                    '"' +
                    (
                        selected
                            ? ' selected'
                            : ''
                    ) +
                    '>' +
                    escapeHtml(
                        text || value
                    ) +
                    '</option>';
            }

            html += '</select>';

            return html;
        }

        function inputHtml(
            fieldName,
            type,
            value,
            step
        ) {
            var html =
                '<input ' +
                'class="vas-upcoming-ap-runs-edit-control" ' +
                'data-pay-field="' +
                escapeHtml(fieldName) +
                '" ' +
                'type="' +
                escapeHtml(type) +
                '" ' +
                'value="' +
                escapeHtml(
                    value == null
                        ? ''
                        : value
                ) +
                '"';

            if (step) {
                html +=
                    ' step="' +
                    escapeHtml(step) +
                    '"';
            }

            html += '>';

            return html;
        }

        function fieldHtml(
            label,
            controlHtml,
            prefilled
        ) {
            return (
                '<div class="vas-upcoming-ap-runs-field' +
                (
                    prefilled
                        ? ' is-prefilled'
                        : ''
                ) +
                '">' +

                '<div class="vas-upcoming-ap-runs-field-label">' +

                escapeHtml(label) +

                (
                    prefilled
                        ? '<span>' +
                        escapeHtml(
                            lbl(
                                'VAS_031_MessagePrefilled',
                                'PRE-FILLED'
                            )
                        ) +
                        '</span>'
                        : ''
                ) +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-field-value">' +

                (
                    controlHtml || ''
                ) +

                '</div>' +

                '</div>'
            );
        }

        function parseDateValue(value) {
            var timestamp;
            var parts;
            var date;

            if (!value) {
                return null;
            }

            if (
                typeof value === 'string' &&
                value.indexOf('/Date(') === 0
            ) {
                timestamp = Number(
                    value.replace(
                        /[^0-9-]/g,
                        ''
                    )
                );

                if (!isNaN(timestamp)) {
                    return new Date(
                        timestamp
                    );
                }
            }

            if (
                typeof value === 'string' &&
                /^\d{4}-\d{2}-\d{2}$/.test(
                    value
                )
            ) {
                parts = value.split('-');

                date = new Date(
                    Number(parts[0]),
                    Number(parts[1]) - 1,
                    Number(parts[2])
                );

                return isNaN(
                    date.getTime()
                )
                    ? null
                    : date;
            }

            date = new Date(value);

            return isNaN(
                date.getTime()
            )
                ? null
                : date;
        }

        function formatDate(value) {
            var date =
                parseDateValue(value);

            if (!date) {
                return value || '';
            }

            return date.toLocaleDateString(
                window.navigator.language,
                {
                    weekday: 'short',
                    day: '2-digit',
                    month: 'short'
                }
            );
        }

        function formatDateForInput(value) {
            var date;
            var year;
            var month;
            var day;

            if (!value) {
                return '';
            }

            if (
                typeof value === 'string' &&
                /^\d{4}-\d{2}-\d{2}$/.test(
                    value
                )
            ) {
                return value;
            }

            date =
                parseDateValue(value);

            if (!date) {
                return '';
            }

            year = date.getFullYear();

            month = String(
                date.getMonth() + 1
            ).padStart(
                2,
                '0'
            );

            day = String(
                date.getDate()
            ).padStart(
                2,
                '0'
            );

            return (
                year +
                '-' +
                month +
                '-' +
                day
            );
        }

        function formatCurrencyAmount(
            value,
            currencySymbol,
            currencyISO,
            stdPrecision
        ) {
            var numericValue =
                Number(value || 0);

            var precision =
                Number(stdPrecision);

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            if (
                isNaN(precision) &&
                VIS &&
                VIS.Env &&
                VIS.Env.getCtx &&
                VIS.Env.getCtx() &&
                VIS.Env.getCtx()
                    .getStdPrecision
            ) {
                precision = Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
            }

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                precision = 2;
            }

            return numericValue
                .toLocaleString(
                    window.navigator.language,
                    {
                        minimumFractionDigits:
                            precision,

                        maximumFractionDigits:
                            precision
                    }
                );
        }

        function normalizeNumber(value) {
            var numericValue =
                Number(value || 0);

            return isNaN(numericValue)
                ? '0'
                : String(numericValue);
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass(
                    'is-visible',
                    !!show
                );
            }
        }

        function showState(
            show,
            message
        ) {
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
        }

        function setNoData() {
            pageNo = 1;
            totalPages = 0;

            if ($body) {
                $body.empty();
            }

            updatePager();

            showState(
                true,
                lbl(
                    'VAS_031_MessageNoData',
                    'No Data'
                )
            );
        }

        function pickRunValue(
            run,
            camelName,
            fallbackName
        ) {
            if (!run) {
                return null;
            }

            if (
                run[camelName] !== undefined &&
                run[camelName] !== null &&
                run[camelName] !== ''
            ) {
                return run[camelName];
            }

            if (
                run[fallbackName] !== undefined &&
                run[fallbackName] !== null &&
                run[fallbackName] !== ''
            ) {
                return run[fallbackName];
            }

            return null;
        }

        function getPaymentLabel(count) {
            return Number(count) === 1
                ? lbl(
                    'VAS_031_MessagePayment',
                    'payment'
                )
                : lbl(
                    'VAS_031_MessagePayments',
                    'payments'
                );
        }

        function getPaymentMethodClass(name) {
            var method =
                String(name || '')
                    .toLowerCase();

            if (
                method.indexOf('rtgs') >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-rtgs'
                );
            }

            if (
                method.indexOf('upi') >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-upi'
                );
            }

            if (
                method.indexOf('card') >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-card'
                );
            }

            if (
                method.indexOf('cheque') >= 0 ||
                method.indexOf('check') >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-cheque'
                );
            }

            return (
                'vas-upcoming-ap-runs-bar-neft'
            );
        }

        function getRunTitle(
            paymentMethodName,
            vendorName,
            count
        ) {
            if (
                Number(count) === 1 &&
                vendorName
            ) {
                return (
                    paymentMethodName +
                    ' · ' +
                    vendorName
                );
            }

            return paymentMethodName;
        }

        function createRunRow(run) {
            var paymentMethodName =
                run.paymentMethodName ||
                lbl(
                    'VAS_031_MessageNotSpecified',
                    'Not Specified'
                );

            var paymentCount =
                Number(
                    run.paymentCount || 0
                );

            var amount =
                Number(
                    pickRunValue(
                        run,
                        'totalAmount',
                        'amount'
                    ) || 0
                );

            var dueDateText =
                pickRunValue(
                    run,
                    'dueDateText',
                    'runDateText'
                ) ||
                formatDate(
                    pickRunValue(
                        run,
                        'dueDate',
                        'runDate'
                    )
                );

            var metaParts = [];

            var $row = $(
                '<div class="vas-upcoming-ap-runs-row">'
            );

            var $bar = $(
                '<span class="vas-upcoming-ap-runs-bar">'
            ).addClass(
                getPaymentMethodClass(
                    paymentMethodName
                )
            );

            var $info = $(
                '<div class="vas-upcoming-ap-runs-info">'
            );

            var $actions = $(
                '<div class="vas-upcoming-ap-runs-actions">'
            );

            var $title = $(
                '<div class="vas-upcoming-ap-runs-run-title">'
            ).text(
                getRunTitle(
                    paymentMethodName,
                    run.vendorName,
                    paymentCount
                )
            );

            var $meta;
            var $amount;
            var $payButton;

            if (dueDateText) {
                metaParts.push(
                    dueDateText
                );
            }

            metaParts.push(
                paymentCount
                    .toLocaleString(
                        window.navigator.language
                    ) +
                ' ' +
                getPaymentLabel(
                    paymentCount
                )
            );

            $meta = $(
                '<div class="vas-upcoming-ap-runs-meta">'
            ).text(
                metaParts.join(' · ')
            );

            $amount = $(
                '<span class="vas-upcoming-ap-runs-amount">'
            ).text(
                formatCurrencyAmount(
                    amount,
                    run.currencySymbol,
                    run.currencyISO,
                    run.stdPrecision
                )
            );

            $payButton = $(
                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-btn">'
            )
                .text(
                    lbl(
                        'VAS_031_MessagePay',
                        'Pay'
                    )
                )
                .append(
                    $(
                        '<span class="vas-upcoming-ap-runs-pay-arrow">'
                    ).text('›')
                );

            $payButton.on(
                'click',
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    openPayDialog(run);
                }
            );

            $info
                .append($title)
                .append($meta);

            $actions
                .append($amount)
                .append($payButton);

            $row
                .append($bar)
                .append($info)
                .append($actions);

            return $row;
        }

        function updatePager() {
            if (!$pager) {
                return;
            }

            if (totalPages > 1) {
                $pagerText.text(
                    pageNo +
                    ' ' +
                    lbl(
                        'VAS_Of',
                        'of'
                    ) +
                    ' ' +
                    totalPages
                );
            }
            else {
                $pagerText.text('');
            }

            $pagerPrev.prop(
                'disabled',
                pageNo <= 1 ||
                totalPages <= 1
            );

            $pagerNext.prop(
                'disabled',
                totalPages <= 1 ||
                pageNo >= totalPages
            );
        }

        function renderPage() {
            var startIndex;
            var pageItems;
            var i;

            if (
                !runsData ||
                runsData.length === 0
            ) {
                setNoData();
                return;
            }

            totalPages = Math.max(
                1,
                Math.ceil(
                    runsData.length /
                    pageSize
                )
            );

            pageNo = Math.max(
                1,
                Math.min(
                    pageNo,
                    totalPages
                )
            );

            startIndex =
                (pageNo - 1) *
                pageSize;

            pageItems = runsData.slice(
                startIndex,
                startIndex + pageSize
            );

            showState(false, '');

            $body.empty();

            for (
                i = 0;
                i < pageItems.length;
                i++
            ) {
                $body.append(
                    createRunRow(
                        pageItems[i]
                    )
                );
            }

            updatePager();
        }

        function renderData(data) {
            runsData = $.isArray(data.runs)
                ? $.grep(
                    data.runs,
                    function (run) {
                        var amount =
                            Number(
                                pickRunValue(
                                    run,
                                    'totalAmount',
                                    'amount'
                                ) || 0
                            );

                        var count =
                            Number(
                                run.paymentCount || 0
                            );

                        return (
                            (
                                !isNaN(amount) &&
                                amount > 0
                            ) ||
                            (
                                !isNaN(count) &&
                                count > 0
                            )
                        );
                    }
                )
                : [];

            if (runsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;

            totalPages =
                Math.ceil(
                    runsData.length /
                    pageSize
                );

            renderPage();
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetUpcomingAPRuns',

                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    var data;

                    if (isDisposed) {
                        return;
                    }

                    data =
                        normalizeResponse(
                            response
                        );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showState(
                            true,
                            (
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            ) ||
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );

                        return;
                    }

                    renderData(data);
                },

                error: function (xhr) {
                    if (isDisposed) {
                        return;
                    }

                    showState(
                        true,
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        )
                    );
                },

                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function getBankAccountDisplay(row) {
            var bankName =
                row.bankName || '';

            var accountName =
                row.bankAccountName || '';

            var accountNo =
                row.bankAccountNo || '';

            var tail = accountNo
                ? String(accountNo)
                    .slice(-4)
                : '';

            if (
                bankName &&
                accountName &&
                tail
            ) {
                return (
                    bankName +
                    ' · ' +
                    accountName +
                    ' · ****' +
                    tail
                );
            }

            if (
                bankName &&
                tail
            ) {
                return (
                    bankName +
                    ' · ****' +
                    tail
                );
            }

            if (
                accountName &&
                tail
            ) {
                return (
                    accountName +
                    ' · ****' +
                    tail
                );
            }

            return (
                accountName ||
                bankName ||
                (
                    tail
                        ? '****' + tail
                        : ''
                )
            );
        }

        function renderPayDialogGrid(row) {
            var organizations;
            var bankAccounts;
            var vendors;
            var currencies;
            var conversionTypes;
            var documentTypes;
            var tenderTypes;

            var selectedOrganizationId;
            var selectedBankAccountId;
            var selectedVendorId;
            var selectedCurrencyId;
            var selectedConversionTypeId;
            var selectedDocTypeId;
            var selectedTenderType;

            var transactionDate;
            var paymentAmount;
            var paymentDocumentNo;
            var html;

            if (
                !$payDialogGrid ||
                !row
            ) {
                return;
            }

            /*
             * جميع اللستات من Response الباك.
             */
            organizations =
                getLookup(
                    'organizations'
                );

            bankAccounts =
                getLookup(
                    'bankAccounts'
                );

            vendors =
                getLookup(
                    'vendors'
                );

            currencies =
                getLookup(
                    'currencies'
                );

            conversionTypes =
                getLookup(
                    'conversionTypes'
                );

            documentTypes =
                getLookupAny([
                    'documentTypes',
                    'docTypes'
                ]);

            tenderTypes =
                getLookupAny([
                    'tenderTypes',
                    'paymentMethods'
                ]);

            selectedOrganizationId =
                getValidLookupValue(
                    organizations,
                    firstPositiveValue(
                        row.organizationId,
                        row.adOrgId,

                        selectedRun &&
                        selectedRun.organizationId,

                        selectedRun &&
                        selectedRun.adOrgId
                    )
                );

            selectedBankAccountId =
                getValidLookupValue(
                    bankAccounts,
                    firstPositiveValue(
                        row.bankAccountId,

                        selectedRun &&
                        selectedRun.bankAccountId
                    )
                );

            /*
             * مهم:
             * إذا Business Partner الموجود بالسجل ليس Vendor،
             * فلن يكون موجودًا داخل vendors،
             * وبالتالي تبقى اللستة بدون تحديد بدل إرساله للباك.
             */
            selectedVendorId =
                getValidLookupValue(
                    vendors,
                    firstPositiveValue(
                        row.vendorId,
                        row.cBPartnerId,

                        selectedRun &&
                        selectedRun.vendorId,

                        selectedRun &&
                        selectedRun.cBPartnerId
                    )
                );

            selectedCurrencyId =
                getValidLookupValue(
                    currencies,
                    firstPositiveValue(
                        row.cCurrencyId,
                        row.currencyId,

                        selectedRun &&
                        selectedRun.cCurrencyId,

                        selectedRun &&
                        selectedRun.currencyId
                    )
                );

            selectedConversionTypeId =
                getValidLookupValue(
                    conversionTypes,
                    firstPositiveValue(
                        row.conversionTypeId,
                        row.cConversionTypeId,

                        selectedRun &&
                        selectedRun.conversionTypeId,

                        selectedRun &&
                        selectedRun.cConversionTypeId
                    )
                );

            selectedDocTypeId =
                getValidLookupValue(
                    documentTypes,
                    firstPositiveValue(
                        row.docTypeId,
                        row.cDocTypeId,

                        selectedRun &&
                        selectedRun.docTypeId,

                        selectedRun &&
                        selectedRun.cDocTypeId
                    )
                );

            /*
             * إذا الـ Doc Type الأصلي صفر،
             * اختار أول Doc Type صالح قادم من الباك.
             */
            if (!selectedDocTypeId) {
                selectedDocTypeId =
                    getFirstPositiveLookupValue(
                        documentTypes
                    );
            }

            selectedTenderType =
                getValidLookupValue(
                    tenderTypes,
                    firstValue(
                        row.tenderType,
                        row.paymentMethodValue,

                        selectedRun &&
                        selectedRun.tenderType,

                        selectedRun &&
                        selectedRun.paymentMethodValue
                    )
                );

            if (!selectedTenderType) {
                selectedTenderType =
                    getFirstLookupValue(
                        tenderTypes
                    );
            }

            transactionDate =
                formatDateForInput(
                    firstValue(
                        row.transactionDate,
                        row.dateTrx,

                        selectedRun &&
                        selectedRun.transactionDate,

                        selectedRun &&
                        selectedRun.runDate,

                        selectedRun &&
                        selectedRun.dueDate
                    )
                );

            paymentAmount =
                firstValue(
                    row.amount,
                    row.payAmt,

                    selectedRun &&
                    selectedRun.amount,

                    selectedRun &&
                    selectedRun.totalAmount,

                    0
                );

            paymentDocumentNo =
                firstValue(
                    row.paymentDocumentNo,
                    row.newPaymentDocumentNo,
                    row.documentNo,
                    ''
                );

            html =
                fieldHtml(
                    lbl(
                        'VAS_031_MessageOrganization',
                        'Organization'
                    ),

                    selectHtml(
                        'adOrgId',
                        organizations,
                        selectedOrganizationId
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageBankAccount',
                        'Bank Account'
                    ),

                    selectHtml(
                        'bankAccountId',
                        bankAccounts,
                        selectedBankAccountId
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageTransactionDate',
                        'Transaction Date'
                    ),

                    inputHtml(
                        'transactionDate',
                        'date',
                        transactionDate
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageVendor',
                        'Vendor'
                    ),

                    selectHtml(
                        'vendorId',
                        vendors,
                        selectedVendorId
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_PaymentCurrency',
                        'Currency'
                    ),

                    selectHtml(
                        'currencyId',
                        currencies,
                        selectedCurrencyId
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageCurrencyType',
                        'Currency Type'
                    ),

                    selectHtml(
                        'conversionTypeId',
                        conversionTypes,
                        selectedConversionTypeId
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageDocumentType',
                        'Document Type'
                    ),

                    selectHtml(
                        'docTypeId',
                        documentTypes,
                        selectedDocTypeId
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageTenderType',
                        'Tender Type'
                    ),

                    selectHtml(
                        'tenderType',
                        tenderTypes,
                        selectedTenderType
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessagePaymentAmount',
                        'Payment Amount'
                    ),

                    inputHtml(
                        'payAmt',
                        'number',
                        normalizeNumber(
                            paymentAmount
                        ),
                        '0.01'
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageDocumentNo',
                        'Document No.'
                    ),

                    inputHtml(
                        'documentNo',
                        'text',
                        paymentDocumentNo
                    ),

                    false
                );

            $payDialogGrid.html(html);
        }

        function updatePayNotice(row) {
            var amountText;
            var sourceDocumentNo;
            var vendorName;

            if (
                !$payDialogNotice ||
                !row
            ) {
                return;
            }

            amountText =
                formatCurrencyAmount(
                    firstValue(
                        row.amount,
                        row.payAmt,

                        selectedRun &&
                        selectedRun.totalAmount,

                        0
                    ),

                    row.currencySymbol ||
                    (
                        selectedRun &&
                        selectedRun.currencySymbol
                    ),

                    row.currencyISO ||
                    (
                        selectedRun &&
                        selectedRun.currencyISO
                    ),

                    row.stdPrecision ||
                    (
                        selectedRun &&
                        selectedRun.stdPrecision
                    )
                );

            sourceDocumentNo =
                firstValue(
                    row.documentNo,
                    row.invoiceDocumentNo,
                    row.invoiceNo,
                    ''
                );

            vendorName =
                firstValue(
                    row.vendorName,

                    selectedRun &&
                    selectedRun.vendorName,

                    ''
                );

            $payDialogNotice
                .removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .html(
                    escapeHtml(
                        lbl(
                            'VAS_031_MessagePrefilledForInvoice',
                            'Pre-filled for invoice'
                        )
                    ) +

                    (
                        sourceDocumentNo
                            ? ' <strong>' +
                            escapeHtml(
                                sourceDocumentNo
                            ) +
                            '</strong>'
                            : ''
                    ) +

                    (
                        vendorName
                            ? ' — ' +
                            escapeHtml(
                                vendorName
                            )
                            : ''
                    ) +

                    ' · ' +

                    escapeHtml(
                        amountText
                    ) +

                    '. ' +

                    escapeHtml(
                        lbl(
                            'VAS_031_MessageReviewAndSave',
                            'Review and save.'
                        )
                    )
                );
        }

        function showPayError(message) {
            if (!$payDialogNotice) {
                return;
            }

            $payDialogNotice
                .addClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .text(
                    message ||
                    lbl(
                        'VAS_ErrorLoading',
                        'Could not load data'
                    )
                );
        }

        function setPayDialogBusy(
            show,
            isSaving
        ) {
            var busy = !!show;

            var saving =
                busy &&
                !!isSaving;

            if ($payDialogBusy) {
                $payDialogBusy.toggleClass(
                    'is-visible',
                    busy
                );
            }

            if ($payDialogSave) {
                $payDialogSave
                    .prop(
                        'disabled',
                        busy
                    )
                    .toggleClass(
                        'is-loading',
                        saving
                    );
            }

            if ($payDialogSaveLabel) {
                $payDialogSaveLabel.text(
                    saving
                        ? lbl(
                            'VAS_031_MessageSaving',
                            'Saving'
                        )
                        : lbl(
                            'VAS_031_MessageSavePayment',
                            'Save payment'
                        )
                );
            }
        }

        /*
         * إذا فشل تحميل اللستات،
         * لا يكمل تحميل البوب ولا ينشئ خيارات fallback.
         */
        function ensurePopupLookups(callback) {
            if (popupLookups) {
                callback(true);
                return;
            }

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetPaymentPopupLookups',

                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    var data =
                        normalizeResponse(
                            response
                        );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        popupLookups = null;

                        showPayError(
                            (
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            ) ||
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load lookup data'
                            )
                        );

                        setPayDialogBusy(
                            false,
                            false
                        );

                        callback(false);
                        return;
                    }

                    popupLookups = data;

                    callback(true);
                },

                error: function (xhr) {
                    popupLookups = null;

                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load lookup data'
                            )
                        )
                    );

                    setPayDialogBusy(
                        false,
                        false
                    );

                    callback(false);
                }
            });
        }

        function loadRunPaymentDetails(run) {
            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetUpcomingAPRunDetails',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    runDate:
                        run.runDate ||
                        run.dueDate ||
                        '',

                    paymentMethodId:
                        run.paymentMethodId ||
                        0
                },

                success: function (response) {
                    var data =
                        normalizeResponse(
                            response
                        );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            (
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            ) ||
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );

                        return;
                    }

                    paymentRows =
                        $.isArray(data.rows)
                            ? data.rows
                            : [];

                    if (
                        paymentRows.length === 0
                    ) {
                        showPayError(
                            lbl(
                                'VAS_031_MessageNoData',
                                'No Data'
                            )
                        );

                        return;
                    }

                    selectedPaymentRow =
                        paymentRows[0];

                    renderPayDialogGrid(
                        selectedPaymentRow
                    );

                    updatePayNotice(
                        selectedPaymentRow
                    );
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        )
                    );
                },

                complete: function () {
                    setPayDialogBusy(
                        false,
                        false
                    );
                }
            });
        }

        function openPayDialog(run) {
            if (
                !$payDialog ||
                !run
            ) {
                return;
            }

            selectedRun = run;
            selectedPaymentRow = null;
            paymentRows = [];

            $payDialogTitle.text(
                lbl(
                    'VAS_031_MessageCreatePayment',
                    'Create Payment'
                )
            );

            $payDialogSub.text(
                lbl(
                    'VAS_031_MessagePrefilledFromUpcoming',
                    'Pre-filled from upcoming'
                ) +
                ' · ' +
                (
                    run.paymentMethodName ||
                    ''
                )
            );

            $payDialogNotice
                .removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .text(
                    lbl(
                        'VAS_031_MessageLoadingDetails',
                        'Loading payment details'
                    )
                );

            $payDialogGrid.empty();

            setPayDialogBusy(
                true,
                false
            );

            $payDialog.show();

            $('body').addClass(
                'vas-upcoming-ap-runs-body-lock'
            );

            ensurePopupLookups(
                function (loaded) {
                    if (!loaded) {
                        return;
                    }

                    loadRunPaymentDetails(
                        run
                    );
                }
            );
        }

        function closePayDialog() {
            if (
                !$payDialog ||
                saveInProgress
            ) {
                return;
            }

            selectedRun = null;
            selectedPaymentRow = null;
            paymentRows = [];

            $payDialog.hide();
            $payDialogGrid.empty();

            $payDialogNotice
                .removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .text('');

            $('body').removeClass(
                'vas-upcoming-ap-runs-body-lock'
            );
        }

        /*
         * القيم الخاصة باللستات تؤخذ فقط من الـ select الظاهر.
         * لا يتم الرجوع إلى Vendor قديم غير صالح.
         */
        function savePayDialog() {
            var payload;

            if (
                !selectedRun ||
                !selectedPaymentRow ||
                saveInProgress
            ) {
                return;
            }

            payload =
                readPayDialogPayload();

            if (!payload) {
                return;
            }

            saveInProgress = true;

            setPayDialogBusy(
                true,
                true
            );

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/CreateUpcomingAPPayment',

                type: 'POST',

                dataType: 'json',

                cache: false,

                data: {
                    /*
                     * Payment الأصلية التي ظهرت في Upcoming.
                     * الكونترولر يحدث حالتها إلى R.
                     */
                    sourcePaymentId:
                        payload.sourcePaymentId,

                    adOrgId:
                        payload.adOrgId,

                    bankAccountId:
                        payload.bankAccountId,

                    vendorId:
                        payload.vendorId,

                    currencyId:
                        payload.currencyId,

                    conversionTypeId:
                        payload.conversionTypeId,

                    docTypeId:
                        payload.docTypeId,

                    tenderType:
                        payload.tenderType,

                    transactionDate:
                        payload.transactionDate,

                    documentNo:
                        payload.documentNo,

                    payAmt:
                        payload.payAmt
                },

                success: function (response) {
                    var data =
                        normalizeResponse(
                            response
                        );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            (
                                data &&
                                (
                                    data.error ||
                                    data.errorText ||
                                    data.message
                                )
                            ) ||
                            lbl(
                                'VAS_031_MessageCouldNotSaveAPPayment',
                                'Could not save AP payment'
                            )
                        );

                        return;
                    }

                    /*
                     * نوقف حالة الحفظ قبل إغلاق الـpopup
                     * لأن closePayDialog لا يغلقه إذا
                     * saveInProgress = true.
                     */
                    saveInProgress = false;

                    setPayDialogBusy(
                        false,
                        false
                    );

                    /*
                     * إفراغ السجلات القديمة حتى لا يبقى
                     * القيد السابق مخزوناً داخل الـJS.
                     */
                    selectedPaymentRow = null;
                    selectedRun = null;
                    paymentRows = [];

                    /*
                     * إعادة تحميل Lookups في المرة القادمة.
                     */
                    popupLookups = null;

                    /*
                     * إغلاق الـpopup يدوياً.
                     */
                    if ($payDialog) {
                        $payDialog.hide();
                    }

                    if ($payDialogGrid) {
                        $payDialogGrid.empty();
                    }

                    if ($payDialogNotice) {
                        $payDialogNotice
                            .removeClass(
                                'vas-upcoming-ap-runs-pay-error'
                            )
                            .text('');
                    }

                    $('body').removeClass(
                        'vas-upcoming-ap-runs-body-lock'
                    );

                    /*
                     * إعادة الصفحة إلى البداية.
                     */
                    pageNo = 1;

                    /*
                     * إعادة استدعاء GetUpcomingAPRuns.
                     *
                     * بما أن الكونترولر حدث:
                     * VA009_ExecutionStatus = 'R'
                     *
                     * والكويري تعرض فقط الحالة I،
                     * فلن يرجع القيد مرة ثانية.
                     */
                    loadData();
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_031_MessageCouldNotSaveAPPayment',
                                'Could not save AP payment'
                            )
                        )
                    );
                },

                complete: function () {
                    /*
                     * إذا حدث خطأ، نعيد تفعيل زر الحفظ.
                     *
                     * في حالة النجاح تم تحويل
                     * saveInProgress إلى false مسبقاً.
                     */
                    if (saveInProgress) {
                        saveInProgress = false;

                        setPayDialogBusy(
                            false,
                            false
                        );
                    }
                }
            });
        }

        function savePayDialog() {
            var payload;

            if (
                !selectedRun ||
                !selectedPaymentRow ||
                saveInProgress
            ) {
                return;
            }

            payload =
                readPayDialogPayload();

            if (!payload) {
                return;
            }

            saveInProgress = true;

            setPayDialogBusy(
                true,
                true
            );

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/CreateUpcomingAPPayment',

                type: 'POST',
                dataType: 'json',
                data: payload,

                success: function (response) {
                    var data =
                        normalizeResponse(
                            response
                        );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            (
                                data &&
                                (
                                    data.error ||
                                    data.errorText ||
                                    data.message
                                )
                            ) ||
                            lbl(
                                'VAS_031_MessageCouldNotSaveAPPayment',
                                'Could not save AP payment'
                            )
                        );

                        return;
                    }

                    saveInProgress = false;

                    setPayDialogBusy(
                        false,
                        true
                    );

                    closePayDialog();

                    /*
                     * إعادة تحميل اللستات والسجلات
                     * بعد إنشاء الدفع.
                     */
                    popupLookups = null;

                    loadData();
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_031_MessageCouldNotSaveAPPayment',
                                'Could not save AP payment'
                            )
                        )
                    );
                },

                complete: function () {
                    if (saveInProgress) {
                        saveInProgress = false;

                        setPayDialogBusy(
                            false,
                            true
                        );
                    }
                }
            });
        }

        function createPayDialog() {
            var closeSelector;

            if ($payDialog) {
                return;
            }

            $payDialog = $(
                '<div ' +
                'class="vas-upcoming-ap-runs-pay-dialog" ' +
                'role="dialog" ' +
                'aria-modal="true">' +

                '<div class="vas-upcoming-ap-runs-pay-scrim"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-card">' +

                '<div class="vas-upcoming-ap-runs-pay-header">' +

                '<span class="vas-upcoming-ap-runs-pay-icon">' +

                '<svg ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="1.8" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round" ' +
                'aria-hidden="true">' +

                '<rect ' +
                'x="3" ' +
                'y="5" ' +
                'width="18" ' +
                'height="14" ' +
                'rx="2">' +
                '</rect>' +

                '<line ' +
                'x1="3" ' +
                'y1="10" ' +
                'x2="21" ' +
                'y2="10">' +
                '</line>' +

                '</svg>' +

                '</span>' +

                '<div class="vas-upcoming-ap-runs-pay-title-group">' +

                '<div class="vas-upcoming-ap-runs-pay-title"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-sub"></div>' +

                '</div>' +

                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-close" ' +
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
                'x1="18" ' +
                'y1="6" ' +
                'x2="6" ' +
                'y2="18">' +
                '</line>' +

                '<line ' +
                'x1="6" ' +
                'y1="6" ' +
                'x2="18" ' +
                'y2="18">' +
                '</line>' +

                '</svg>' +

                '</button>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-body">' +

                '<div class="vas-upcoming-ap-runs-pay-busy">' +

                '<div class="vis-busyindicatorinnerwrap">' +

                '<i class="vis_widgetloader"></i>' +

                '</div>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-notice"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-grid"></div>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-footer">' +

                '<span>' +

                escapeHtml(
                    lbl(
                        'VAS_031_MessageGeneratedFromUpcoming',
                        'Generated from Upcoming · 7 days'
                    )
                ) +

                '</span>' +

                '<div class="vas-upcoming-ap-runs-pay-footer-actions">' +

                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-cancel">' +

                escapeHtml(
                    lbl(
                        'VAS_Cancel',
                        'Cancel'
                    )
                ) +

                '</button>' +

                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-save">' +

                '<span ' +
                'class="vas-upcoming-ap-runs-save-spinner" ' +
                'aria-hidden="true">' +
                '</span>' +

                '<span ' +
                'class="vas-upcoming-ap-runs-save-check" ' +
                'aria-hidden="true">' +
                '✓' +
                '</span>' +

                '<span class="vas-upcoming-ap-runs-save-label">' +

                escapeHtml(
                    lbl(
                        'VAS_031_MessageSavePayment',
                        'Save payment'
                    )
                ) +

                '</span>' +

                '</button>' +

                '</div>' +

                '</div>' +

                '</div>' +

                '</div>'
            );

            $payDialog.hide();

            $payDialogTitle =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-title'
                );

            $payDialogSub =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-sub'
                );

            $payDialogNotice =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-notice'
                );

            $payDialogGrid =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-grid'
                );

            $payDialogSave =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-save'
                );

            $payDialogSaveLabel =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-save-label'
                );

            $payDialogBusy =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-busy'
                );

            closeSelector =
                '.vas-upcoming-ap-runs-pay-close, ' +
                '.vas-upcoming-ap-runs-pay-cancel';

            $payDialog
                .find(closeSelector)
                .on(
                    'click',
                    function () {
                        closePayDialog();
                    }
                );

            $payDialog
                .find(
                    '.vas-upcoming-ap-runs-pay-scrim'
                )
                .on(
                    'click',
                    function () {
                        closePayDialog();
                    }
                );

            $payDialogSave.on(
                'click',
                function () {
                    savePayDialog();
                }
            );

            $(document).on(
                'keydown.vas-upcoming-ap-runs-pay-' +
                $self.AD_UserHomeWidgetID,

                function (event) {
                    if (
                        event.key === 'Escape' &&
                        $payDialog &&
                        $payDialog.is(':visible')
                    ) {
                        closePayDialog();
                    }
                }
            );

            $('body').append(
                $payDialog
            );
        }

        function createWidget() {
            var $head;
            var $headLeft;
            var $titleRow;
            var $iconBox;
            var $icon;
            var $title;
            var $arrow;
            var $sub;
            var $footer;

            $card = $(
                '<div class="vas-upcoming-ap-runs-card">'
            );

            $head = $(
                '<div class="vas-upcoming-ap-runs-head">'
            );

            $headLeft = $(
                '<div class="vas-upcoming-ap-runs-head-left">'
            );

            $titleRow = $(
                '<div class="vas-upcoming-ap-runs-title-row">'
            );

            $iconBox = $(
                '<span class="vas-upcoming-ap-runs-icon-box">'
            );

            $icon = $(
                '<svg ' +
                'class="vas-upcoming-ap-runs-icon" ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round" ' +
                'aria-hidden="true" ' +
                'focusable="false">' +

                '<circle ' +
                'cx="12" ' +
                'cy="12" ' +
                'r="10">' +
                '</circle>' +

                '<polyline ' +
                'points="12 6 12 12 16 14">' +
                '</polyline>' +

                '</svg>'
            );

            $title = $(
                '<div class="vas-upcoming-ap-runs-title">'
            ).text(
                lbl(
                    'VAS_031_MessageUpcomingRuns',
                    'Upcoming runs'
                )
            );

            $arrow = $(
                '<span class="vas-upcoming-ap-runs-arrow" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 18l6-6-6-6"></path>' +
                '</svg>' +
                '</span>'
            );

            $sub = $(
                '<div class="vas-upcoming-ap-runs-sub">'
            ).text(
                lbl(
                    'VAS_031_MessageNext7Days',
                    'Next 7 days'
                )
            );

    
            $pager = $(
                '<div class="vas-upcoming-ap-runs-pager">'
            );

            $pagerPrev = $(
                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-page-btn" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Previous',
                        'Previous'
                    )
                ) +
                '">' +
                '‹' +
                '</button>'
            );

            $pagerText = $(
                '<span class="vas-upcoming-ap-runs-page-text">'
            );

            $pagerNext = $(
                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-page-btn" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Next',
                        'Next'
                    )
                ) +
                '">' +
                '›' +
                '</button>'
            );

            $body = $(
                '<div class="vas-upcoming-ap-runs-body">'
            );

            $footer = $(
                '<div class="vas-upcoming-ap-runs-footer">'
            );

            $busy = $(
                '<div class="vas-upcoming-ap-runs-busy">' +

                '<div class="vis-busyindicatorinnerwrap">' +

                '<i class="vis_widgetloader"></i>' +

                '</div>' +

                '</div>'
            );

            $state = $(
                '<div class="vas-upcoming-ap-runs-state-message">'
            );

            $iconBox.append($icon);

            $titleRow
                .append($iconBox)
                .append($title);

            $headLeft
                .append($titleRow)
                .append($sub);

            $head
                .append($headLeft)
                .append($arrow);

            $pager
                .append($pagerPrev)
                .append($pagerText)
                .append($pagerNext);

            $footer.append($pager);

            $card
                .append($head)
                .append($body)
                .append($footer)
                .append($busy)
                .append($state);

            $root
                .empty()
                .append($card);

            $pagerPrev.on(
                'click',
                function () {
                    if (pageNo <= 1) {
                        return;
                    }

                    pageNo--;

                    renderPage();
                }
            );

            $pagerNext.on(
                'click',
                function () {
                    if (
                        totalPages <= 1 ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;

                    renderPage();
                }
            );

            createPayDialog();
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshWidget = function () {
            if (isDisposed) {
                return;
            }

            popupLookups = null;

            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            $(document).off(
                'keydown.vas-upcoming-ap-runs-pay-' +
                $self.AD_UserHomeWidgetID
            );

            $('body').removeClass(
                'vas-upcoming-ap-runs-body-lock'
            );

            if ($pagerPrev) {
                $pagerPrev.off();
            }

            if ($pagerNext) {
                $pagerNext.off();
            }

            if ($payDialog) {
                $payDialog
                    .find('*')
                    .off();

                $payDialog.remove();
            }

            $root
                .find('*')
                .off();

            $root.remove();

            $card = null;
            $body = null;
            $pager = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $busy = null;
            $state = null;

            $payDialog = null;
            $payDialogTitle = null;
            $payDialogSub = null;
            $payDialogNotice = null;
            $payDialogGrid = null;
            $payDialogSave = null;
            $payDialogSaveLabel = null;
            $payDialogBusy = null;

            selectedRun = null;
            selectedPaymentRow = null;

            paymentRows = [];
            popupLookups = null;
            runsData = [];
        };
    };

    VAS.VAS_031_UpcomingAPRunsWidget
        .prototype
        .init = function (
            windowNo,
            frame
        ) {
            this.frame = frame;

            this.AD_UserHomeWidgetID =
                frame.widgetInfo
                    .AD_UserHomeWidgetID;

            this.windowNo = windowNo;

            this.Initalize();

            this.frame
                .getContentGrid()
                .append(
                    this.getRoot()
                );
        };

    VAS.VAS_031_UpcomingAPRunsWidget
        .prototype
        .widgetSizeChange = function (
            height,
            width
        ) {
        };

    VAS.VAS_031_UpcomingAPRunsWidget
        .prototype
        .dispose = function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);
