/**
 * VAS_031 Upcoming AP Runs Widget
 * Purpose - Displays upcoming AP invoices due within the next seven days,
 * groups them by due date, business partner, payment method and currency, displays invoice
 * details, and creates an AP payment linked to the selected invoice.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Upcoming runs                        | VAS_031_MessageUpcomingRuns
 *  2  | Next 7 days                          | VAS_031_MessageNext7Days
 *  3  | invoice                              | VAS_031_MessageInvoice
 *  4  | invoices                             | VAS_031_MessageInvoices
 *  5  | Loading                              | VAS_031_MessageLoading
 *  6  | No Data                              | VAS_031_MessageNoData
 *  7  | Previous                             | VAS_Previous
 *  8  | Next                                 | VAS_Next
 *  9  | Showing                              | VAS_031_MessageShowing
 * 10  | of                                   | VAS_Of
 * 11  | Not Specified                        | VAS_031_MessageNotSpecified
 * 12  | Pay                                  | VAS_031_MessagePay
 * 13  | Create Payment                       | VAS_031_MessageCreatePayment
 * 14  | Pre-filled from upcoming             | VAS_031_PrefilledFromUpcoming
 * 15  | Loading invoice details              | VAS_031_MessageLoadingDetails
 * 16  | Invoices                             | VAS_031_MessageInvoices
 * 17  | Invoice                              | VAS_031_MessageInvoice
 * 18  | Organization                         | VAS_031_MessageOrganization
 * 19  | Bank Account                         | VAS_031_MessageBankAccount
 * 20  | Transaction Date                     | VAS_031_MessageTransactionDate
 * 21  | Vendor                               | VAS_031_MessageVendor
 * 22  | Currency                             | VAS_PaymentCurrency
 * 23  | Currency Type                        | VAS_031_MessageCurrencyType
 * 24  | Document Type                        | VAS_031_MessageDocumentType
 * 25  | Tender Type                          | VAS_031_MessageTenderType
 * 26  | Payment Amount                       | VAS_031_MessagePaymentAmount
 * 27  | Payment Document No.                 | VAS_031_MessageDocumentNo
 * 28  | pre-filled                           | VAS_031_MessagePrefilled
 * 30  | Pre-filled for invoice               | VAS_031_MessagePrefilledForInvoice
 * 31  | Review and save.                     | VAS_031_MessageReviewAndSave
 * 32  | Saving                               | VAS_031_MessageSaving
 * 33  | Save payment                         | VAS_031_MessageSavePayment
 * 34  | Cancel                               | VAS_Cancel
 * 35  | Close                                | VAS_Close
 * 36  | Could not load data                  | VAS_ErrorLoading
 * 37  | Could not save AP payment            | VAS_031_CouldNotSaveAPPayment
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

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
        var $state = null;
        var $busy = null;

        var $footer = null;
        var $showingText = null;
        var $pager = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var $payDialog = null;
        var $payDialogTitle = null;
        var $payDialogSub = null;
        var $payDialogNotice = null;
        var $invoiceList = null;
        var $payDialogGrid = null;
        var $payDialogSave = null;
        var $payDialogSaveLabel = null;
        var $payDialogBusy = null;

        var isDisposed = false;
        var saveInProgress = false;

        var runsData = [];
        var invoiceRows = [];
        var popupLookups = null;
        var popupOriginalValues = null;

        var selectedRun = null;
        var selectedInvoiceRow = null;
        var currentTenderType = null;

        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        var resizeObserver = null;

        /*
         * Two-line widget row:
         * title + metadata + padding + divider.
         */
        var widgetRowHeight = 65;
        var widgetMinimumRows = 3;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return (
                text &&
                text !== key &&
                text !== '[' + key + ']'
            )
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
            var index;

            for (
                index = 0;
                index < 2;
                index++
            ) {
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

            return getResponseMessage(
                response,
                fallback
            );
        }

        function getResponseMessage(
            response,
            fallback
        ) {
            var key;

            if (!response) {
                return fallback;
            }

            key =
                response.errorKey ||
                response.messageKey;

            if (key) {
                return lbl(
                    key,
                    response.error ||
                    response.errorText ||
                    response.message ||
                    fallback
                );
            }

            return (
                response.error ||
                response.errorText ||
                response.message ||
                fallback
            );
        }

        function firstValue() {
            var index;
            var value;

            for (
                index = 0;
                index < arguments.length;
                index++
            ) {
                value = arguments[index];

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
            var index;
            var value;
            var numericValue;

            for (
                index = 0;
                index < arguments.length;
                index++
            ) {
                value = arguments[index];

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
                    return numericValue;
                }
            }

            return 0;
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
                    return new Date(timestamp);
                }
            }

            if (
                typeof value === 'string' &&
                /^\d{4}-\d{2}-\d{2}$/.test(value)
            ) {
                parts = value.split('-');

                date = new Date(
                    Number(parts[0]),
                    Number(parts[1]) - 1,
                    Number(parts[2])
                );

                return isNaN(date.getTime())
                    ? null
                    : date;
            }

            date = new Date(value);

            return isNaN(date.getTime())
                ? null
                : date;
        }

        function formatDate(value) {
            var date = parseDateValue(value);

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
                /^\d{4}-\d{2}-\d{2}$/.test(value)
            ) {
                return value;
            }

            date = parseDateValue(value);

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
            var numericValue = Number(value || 0);
            var precision = Number(stdPrecision);
            var amountText;
            var currencyText;

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                if (
                    VIS &&
                    VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env.getCtx() &&
                    VIS.Env.getCtx().getStdPrecision
                ) {
                    precision = Number(
                        VIS.Env
                            .getCtx()
                            .getStdPrecision()
                    );
                }
            }

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                precision = 2;
            }

            amountText = numericValue.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: precision,
                    maximumFractionDigits: precision
                }
            );

            currencyText =
                currencySymbol ||
                currencyISO ||
                '';

            return currencyText
                ? currencyText + ' ' + amountText
                : amountText;
        }

        function normalizeNumber(value) {
            var numericValue = Number(value || 0);

            return isNaN(numericValue)
                ? '0'
                : String(numericValue);
        }

        function normalizeNumberOrEmpty(value) {
            var numericValue = Number(value || 0);

            return (
                isNaN(numericValue) ||
                numericValue === 0
            )
                ? ''
                : String(numericValue);
        }

        function normalizePrecision(value) {
            var precision = Number(value);

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                precision = 2;
            }

            return precision;
        }

        function getInvoiceLabel(count) {
            return Number(count) === 1
                ? lbl(
                    'VAS_031_MessageInvoice',
                    'invoice'
                )
                : lbl(
                    'VAS_031_MessageInvoices',
                    'invoices'
                );
        }

        function showBusy(show) {
            var $b = $root.find(
                '#VAS-gljtm-busy-' +
                $self.AD_UserHomeWidgetID
            );

            if (show) {
                $b.show();
                $b.addClass('is-visible');
            }
            else {
                $b.hide();
                $b.removeClass('is-visible');
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

            if ($footer) {
                $footer.toggle(!show);
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

        function getLookup(name) {
            if (
                popupLookups &&
                $.isArray(popupLookups[name])
            ) {
                return popupLookups[name];
            }

            return [];
        }

        function getLookupAny(names) {
            var index;
            var items;

            names = $.isArray(names)
                ? names
                : [];

            for (
                index = 0;
                index < names.length;
                index++
            ) {
                items = getLookup(names[index]);

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

        function getValidLookupValue(
            items,
            requestedValue
        ) {
            var index;
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
                index = 0;
                index < items.length;
                index++
            ) {
                itemValue = getLookupItemValue(
                    items[index]
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
            var index;
            var value;

            items = $.isArray(items)
                ? items
                : [];

            for (
                index = 0;
                index < items.length;
                index++
            ) {
                value = getLookupItemValue(
                    items[index]
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

        function getFirstPositiveLookupValue(items) {
            var index;
            var value;
            var numericValue;

            items = $.isArray(items)
                ? items
                : [];

            for (
                index = 0;
                index < items.length;
                index++
            ) {
                value = getLookupItemValue(
                    items[index]
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

        function selectHtml(
            fieldName,
            items,
            selectedValue,
            disabled
        ) {
            var html;
            var index;
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
                '"' +
                (
                    disabled
                        ? ' disabled'
                        : ''
                ) +
                '>';

            /*
             * All popup dropdowns are mandatory on the backend,
             * so no empty "Select" placeholder option is rendered.
             * The first real option becomes the default selection.
             */
            for (
                index = 0;
                index < items.length;
                index++
            ) {
                item = items[index] || {};

                value = getLookupItemValue(item);

                if (
                    value === undefined ||
                    value === null ||
                    String(value).trim() === ''
                ) {
                    continue;
                }

                text = getLookupItemText(
                    item,
                    null,
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
                    escapeHtml(text || value) +
                    '</option>';
            }

            html += '</select>';

            return html;
        }

        function inputHtml(
            fieldName,
            type,
            value,
            step,
            readonly
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

            if (readonly) {
                html += ' readonly';
            }

            html += '>';

            return html;
        }

        function fieldHtml(
            label,
            controlHtml,
            prefilled,
            extraAttributes
        ) {
            return (
                '<div class="vas-upcoming-ap-runs-field' +
                (
                    prefilled
                        ? ' is-prefilled'
                        : ''
                ) +
                '"' +
                (
                    extraAttributes
                        ? ' ' + extraAttributes
                        : ''
                ) +
                '>' +

                '<div class="vas-upcoming-ap-runs-field-label">' +

                escapeHtml(label) +

                (
                    prefilled
                        ? '<span>' +
                        escapeHtml(
                            lbl(
                                'VAS_031_MessagePrefilled',
                                'pre-filled'
                            )
                        ) +
                        '</span>'
                        : ''
                ) +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-field-value">' +
                controlHtml +
                '</div>' +

                '</div>'
            );
        }

        function getPaymentMethodClass(name) {
            var method = String(
                name || ''
            ).toLowerCase();

            if (method.indexOf('rtgs') >= 0) {
                return 'vas-upcoming-ap-runs-bar-rtgs';
            }

            if (method.indexOf('upi') >= 0) {
                return 'vas-upcoming-ap-runs-bar-upi';
            }

            if (method.indexOf('card') >= 0) {
                return 'vas-upcoming-ap-runs-bar-card';
            }

            if (
                method.indexOf('cheque') >= 0 ||
                method.indexOf('check') >= 0
            ) {
                return 'vas-upcoming-ap-runs-bar-cheque';
            }

            return 'vas-upcoming-ap-runs-bar-neft';
        }

        function createRunRow(run) {
            var paymentMethodName;
            var paymentCount;
            var amount;
            var dueDateText;
            var titleText;
            var metaText;
            var amountText;

            var $row;
            var $bar;
            var $info;
            var $actions;
            var $title;
            var $meta;
            var $amount;
            var $payButton;

            paymentMethodName =
                run.paymentMethodName ||
                lbl(
                    'VAS_031_MessageNotSpecified',
                    'Not Specified'
                );

            paymentCount = Number(
                run.paymentCount || 0
            );

            amount = Number(
                firstValue(
                    run.totalAmount,
                    run.amount,
                    0
                )
            );

            dueDateText = firstValue(
                run.dueDateText,
                run.runDateText,
                formatDate(
                    firstValue(
                        run.dueDate,
                        run.runDate
                    )
                )
            );

            titleText = paymentMethodName;

            if (
                paymentCount === 1 &&
                run.vendorName
            ) {
                titleText +=
                    ' · ' +
                    run.vendorName;
            }

            metaText =
                dueDateText +
                ' · ' +
                paymentCount.toLocaleString(
                    window.navigator.language
                ) +
                ' ' +
                getInvoiceLabel(paymentCount);

            amountText = formatCurrencyAmount(
                amount,
                run.currencySymbol,
                run.currencyISO,
                run.stdPrecision
            );

            $row = $(
                '<div ' +
                'class="vas-upcoming-ap-runs-row" ' +
                'role="button" ' +
                'tabindex="0">'
            );

            $bar = $(
                '<span class="vas-upcoming-ap-runs-bar">'
            ).addClass(
                getPaymentMethodClass(
                    paymentMethodName
                )
            );

            $info = $(
                '<div class="vas-upcoming-ap-runs-info">'
            );

            $actions = $(
                '<div class="vas-upcoming-ap-runs-actions">'
            );

            $title = $(
                '<div class="vas-upcoming-ap-runs-run-title">'
            )
                .text(titleText)
                .attr('title', titleText);

            $meta = $(
                '<div class="vas-upcoming-ap-runs-meta">'
            )
                .text(metaText)
                .attr('title', metaText);

            $amount = $(
                '<span class="vas-upcoming-ap-runs-amount">'
            )
                .text(amountText)
                .attr('title', amountText);

            $payButton = $(
                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-btn">' +

                escapeHtml(
                    lbl(
                        'VAS_031_MessagePay',
                        'Pay'
                    )
                ) +

                '<span ' +
                'class="vas-upcoming-ap-runs-pay-arrow" ' +
                'aria-hidden="true">›</span>' +

                '</button>'
            );

            function openSelectedRun(event) {
                if (event) {
                    event.preventDefault();
                    event.stopPropagation();
                }

                openPayDialog(run);
            }

            $payButton.on(
                'click',
                openSelectedRun
            );

            $row.on(
                'click',
                openSelectedRun
            );

            $row.on(
                'keydown',
                function (event) {
                    if (
                        event.key === 'Enter' ||
                        event.key === ' '
                    ) {
                        openSelectedRun(event);
                    }
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

        function calculatePageSize() {
            var availableHeight;
            var calculatedRows;
            var newPageSize;
            var newTotalPages;

            if (
                !$body ||
                !$body.length
            ) {
                return;
            }

            availableHeight =
                $body[0].clientHeight;

            if (availableHeight <= 0) {
                return;
            }

            calculatedRows = Math.floor(
                availableHeight /
                widgetRowHeight
            );

            newPageSize = Math.max(
                widgetMinimumRows,
                calculatedRows
            );

            if (newPageSize === pageSize) {
                return;
            }

            pageSize = newPageSize;

            newTotalPages =
                runsData.length > 0
                    ? Math.max(
                        1,
                        Math.ceil(
                            runsData.length /
                            pageSize
                        )
                    )
                    : 0;

            pageNo = Math.max(
                1,
                Math.min(
                    pageNo,
                    newTotalPages || 1
                )
            );

            renderPage();
        }

        function startAdaptiveRowObserver() {
            if (
                resizeObserver ||
                !$body ||
                !$body.length
            ) {
                return;
            }

            if (
                typeof ResizeObserver ===
                'undefined'
            ) {
                calculatePageSize();
                return;
            }

            resizeObserver = new ResizeObserver(
                function () {
                    window.requestAnimationFrame(
                        calculatePageSize
                    );
                }
            );

            resizeObserver.observe(
                $body[0]
            );

            calculatePageSize();
        }

        function stopAdaptiveRowObserver() {
            if (resizeObserver) {
                resizeObserver.disconnect();
                resizeObserver = null;
            }
        }

        function updatePager() {
            var count;
            var startIndex;
            var endIndex;

            if (!$pager) {
                return;
            }

            count = runsData.length;

            totalPages =
                count > 0
                    ? Math.max(
                        1,
                        Math.ceil(
                            count /
                            pageSize
                        )
                    )
                    : 0;

            pageNo = Math.max(
                1,
                Math.min(
                    pageNo,
                    totalPages || 1
                )
            );

            startIndex =
                count > 0
                    ? (
                        (pageNo - 1) *
                        pageSize
                    ) + 1
                    : 0;

            endIndex =
                count > 0
                    ? Math.min(
                        pageNo * pageSize,
                        count
                    )
                    : 0;

            if ($showingText) {
                $showingText.text(
                    count > 0
                        ? lbl(
                            'VAS_031_MessageShowing',
                            'Showing'
                        ) +
                        ' ' +
                        startIndex +
                        '–' +
                        endIndex +
                        ' ' +
                        lbl(
                            'VAS_Of',
                            'of'
                        ) +
                        ' ' +
                        count
                        : ''
                );
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

            $pager.toggle(
                totalPages > 1
            );
        }

        function renderPage() {
            var startIndex;
            var pageItems;
            var index;

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
                index = 0;
                index < pageItems.length;
                index++
            ) {
                $body.append(
                    createRunRow(
                        pageItems[index]
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
                        var amount = Number(
                            firstValue(
                                run.totalAmount,
                                run.amount,
                                0
                            )
                        );

                        var count = Number(
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

            calculatePageSize();
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

                    data = normalizeResponse(response);

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showState(
                            true,
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_ErrorLoading',
                                    'Could not load data'
                                )
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

        function setPayDialogBusy(
            show,
            isSaving
        ) {
            var busy = !!show;
            var saving =
                busy &&
                !!isSaving;

            if ($payDialogBusy) {
                $payDialogBusy
                    .toggleClass(
                        'is-visible',
                        busy
                    );
            }

            if ($payDialogSave) {
                $payDialogSave
                    .prop(
                        'disabled',
                        busy ||
                        !selectedInvoiceRow
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

        function ensurePopupLookups(
            currencyId,
            callback
        ) {
            if (typeof currencyId === 'function') {
                callback = currencyId;
                currencyId = 0;
            }

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
                data: {
                    currencyId:
                        Number(currencyId || 0)
                },

                success: function (response) {
                    var data = normalizeResponse(
                        response
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        popupLookups = null;

                        showPayError(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_ErrorLoading',
                                    'Could not load lookup data.'
                                )
                            )
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
                                'Could not load lookup data.'
                            )
                        )
                    );

                    callback(false);
                }
            });
        }

        function refreshPopupBankAccounts(
            currencyId,
            callback
        ) {
            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetPaymentPopupLookups',

                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    currencyId:
                        Number(currencyId || 0)
                },

                success: function (response) {
                    var data = normalizeResponse(
                        response
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_ErrorLoading',
                                    'Could not load lookup data.'
                                )
                            )
                        );

                        callback(false);
                        return;
                    }

                    popupLookups =
                        popupLookups || {};

                    popupLookups.bankAccounts =
                        $.isArray(
                            data.bankAccounts
                        )
                            ? data.bankAccounts
                            : [];

                    callback(true);
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load lookup data.'
                            )
                        )
                    );

                    callback(false);
                }
            });
        }

        function updatePopupBankAccountField() {
            var bankAccounts;
            var selectedBankAccountId;
            var validBankAccountId;

            if (
                !$payDialogGrid ||
                !getPayField('bankAccountId').length
            ) {
                return;
            }

            bankAccounts =
                getLookup('bankAccounts');

            selectedBankAccountId = Number(
                getPayField('bankAccountId').val() || 0
            );

            validBankAccountId =
                getValidLookupValue(
                    bankAccounts,
                    selectedBankAccountId
                );

            if (!validBankAccountId) {
                validBankAccountId =
                    getFirstPositiveLookupValue(
                        bankAccounts
                    );
            }

            getPayField('bankAccountId').replaceWith(
                selectHtml(
                    'bankAccountId',
                    bankAccounts,
                    validBankAccountId,
                    false
                )
            );
        }

        function renderInvoiceRows(rows) {
            var html = '';
            var index;
            var row;
            var amountText;
            var documentText;
            var vendorText;
            var dueText;

            rows = $.isArray(rows)
                ? rows
                : [];

            for (
                index = 0;
                index < rows.length;
                index++
            ) {
                row = rows[index] || {};

                amountText = formatCurrencyAmount(
                    firstValue(
                        row.openAmount,
                        row.amount,
                        0
                    ),
                    row.currencySymbol,
                    row.currencyISO,
                    row.stdPrecision
                );

                documentText = firstValue(
                    row.invoiceDocumentNo,
                    row.documentNo,
                    ''
                );

                vendorText = firstValue(
                    row.vendorName,
                    ''
                );

                dueText = formatDate(
                    firstValue(
                        row.dueDate,
                        row.transactionDate
                    )
                );

                html +=
                    '<button ' +
                    'type="button" ' +
                    'class="vas-upcoming-ap-runs-invoice-row' +
                    (
                        selectedInvoiceRow &&
                            Number(
                                selectedInvoiceRow.invoiceId
                            ) ===
                            Number(row.invoiceId) &&
                            Number(
                                selectedInvoiceRow.invoicePayScheduleId
                            ) ===
                            Number(row.invoicePayScheduleId)
                            ? ' is-selected'
                            : ''
                    ) +
                    '" ' +
                    'data-invoice-index="' +
                    index +
                    '">' +

                    '<span class="vas-upcoming-ap-runs-invoice-main">' +

                    '<strong title="' +
                    escapeHtml(documentText) +
                    '">' +
                    escapeHtml(documentText) +
                    '</strong>' +

                    '<small title="' +
                    escapeHtml(vendorText) +
                    '">' +
                    escapeHtml(vendorText) +
                    '</small>' +

                    '</span>' +

                    '<span class="vas-upcoming-ap-runs-invoice-side">' +

                    '<strong title="' +
                    escapeHtml(amountText) +
                    '">' +
                    escapeHtml(amountText) +
                    '</strong>' +

                    '<small title="' +
                    escapeHtml(dueText) +
                    '">' +
                    escapeHtml(dueText) +
                    '</small>' +

                    '</span>' +

                    '</button>';
            }

            $invoiceList.html(html);

            $invoiceList
                .find(
                    '[data-invoice-index]'
                )
                .off('click')
                .on(
                    'click',
                    function () {
                        var selectedIndex = Number(
                            $(this).attr(
                                'data-invoice-index'
                            )
                        );

                        if (
                            isNaN(selectedIndex) ||
                            !invoiceRows[selectedIndex]
                        ) {
                            return;
                        }

                        selectInvoiceRow(
                            invoiceRows[selectedIndex]
                        );
                    }
                );
        }

        function selectInvoiceRow(row) {
            selectedInvoiceRow = row;

            renderInvoiceRows(invoiceRows);
            renderPayDialogGrid(row);
            updatePayNotice(row);

            setPayDialogBusy(
                false,
                false
            );
        }

        function getSelectedLookupItem(
            items,
            selectedValue
        ) {
            var index;
            var item;

            items = $.isArray(items)
                ? items
                : [];

            for (index = 0; index < items.length; index++) {
                item = items[index] || {};

                if (
                    String(getLookupItemValue(item)) ===
                    String(selectedValue)
                ) {
                    return item;
                }
            }

            return null;
        }

        function isCheckPaymentMethodItem(item) {
            var text;

            item = item || {};

            if (
                item.isCheck === true ||
                String(item.isCheck).toLowerCase() === 'true'
            ) {
                return true;
            }

            text = String(
                firstValue(
                    item.name,
                    item.text,
                    item.label,
                    ''
                )
            ).toLowerCase();

            return (
                text.indexOf('cheque') >= 0 ||
                text.indexOf('check') >= 0 ||
                text.indexOf('chq') >= 0 ||
                text.indexOf('صك') >= 0
            );
        }

        function updateCheckFieldsVisibility() {
            var paymentMethodId;
            var selectedMethod;
            var showFields;

            paymentMethodId = Number(
                getPayField('paymentMethodId').val() || 0
            );

            selectedMethod = getSelectedLookupItem(
                getLookup('paymentMethods'),
                paymentMethodId
            );

            showFields = isCheckPaymentMethodItem(
                selectedMethod
            );

            $payDialogGrid
                .find('[data-check-field="true"]')
                .toggle(showFields);

            if (!showFields) {
                getPayField('checkNo').val('');
                getPayField('checkDate').val('');
            }
        }

        function getPopupBasePaymentAmount(row) {
            if (
                popupOriginalValues &&
                popupOriginalValues.hasOwnProperty(
                    'payAmt'
                )
            ) {
                return popupOriginalValues.payAmt;
            }

            var amount = Number(
                firstValue(
                    row && row.payAmt,
                    row && row.openAmount,
                    row && row.amount,
                    0
                )
            );

            return isNaN(amount)
                ? 0
                : amount;
        }

        function getPopupBaseDiscountAmount(row) {
            if (
                popupOriginalValues &&
                popupOriginalValues.hasOwnProperty(
                    'discountAmt'
                )
            ) {
                return popupOriginalValues.discountAmt;
            }

            var amount = Number(
                firstValue(
                    row && row.discountAmt,
                    row && row.discountAmount,
                    0
                )
            );

            return isNaN(amount) || amount < 0
                ? 0
                : amount;
        }

        function getPopupBaseOpenAmount(row) {
            if (
                popupOriginalValues &&
                popupOriginalValues.hasOwnProperty(
                    'openAmount'
                )
            ) {
                return popupOriginalValues.openAmount;
            }

            var amount = Number(
                firstValue(
                    row && row.openAmount,
                    row && row.scheduleOpenAmount,
                    row && row.amount,
                    0
                )
            );

            return isNaN(amount) || amount < 0
                ? 0
                : amount;
        }

        function getPopupBaseWriteOffAmount(row) {
            if (
                popupOriginalValues &&
                popupOriginalValues.hasOwnProperty(
                    'writeOffAmt'
                )
            ) {
                return popupOriginalValues.writeOffAmt;
            }

            var amount = Number(
                firstValue(
                    row && row.writeOffAmt,
                    row && row.writeOffAmount,
                    0
                )
            );

            return isNaN(amount) || amount < 0
                ? 0
                : amount;
        }

        function getPopupOriginalCurrencyId(
            row
        ) {
            if (
                popupOriginalValues &&
                popupOriginalValues.hasOwnProperty(
                    'currencyId'
                )
            ) {
                return popupOriginalValues.currencyId;
            }

            return Number(
                firstPositiveValue(
                    row && row.currencyId,
                    row && row.cCurrencyId
                )
            );
        }

        function setPopupOriginalValues(row) {
            row = row || {};

            popupOriginalValues = {
                payAmt: Number(
                    firstValue(
                        row.payAmt,
                        row.openAmount,
                        row.amount,
                        0
                    )
                ) || 0,
                discountAmt: Number(
                    firstValue(
                        row.discountAmt,
                        row.discountAmount,
                        0
                    )
                ) || 0,
                openAmount: Number(
                    firstValue(
                        row.openAmount,
                        row.scheduleOpenAmount,
                        row.amount,
                        0
                    )
                ) || 0,
                writeOffAmt: Number(
                    firstValue(
                        row.writeOffAmt,
                        row.writeOffAmount,
                        0
                    )
                ) || 0,
                currencyId:
                    Number(
                        firstPositiveValue(
                            row.currencyId,
                            row.cCurrencyId
                        )
                    )
            };
        }

        function getPopupAmountPrecision() {
            var currencyId;
            var currencies;
            var index;
            var item;

            currencyId = Number(
                getPayField('currencyId').val() || 0
            );

            currencies = getLookup('currencies');

            for (index = 0; index < currencies.length; index++) {
                item = currencies[index] || {};

                if (
                    Number(getLookupItemValue(item)) ===
                    currencyId
                ) {
                    return normalizePrecision(
                        firstValue(
                            item.stdPrecision,
                            item.StdPrecision,
                            selectedInvoiceRow &&
                            selectedInvoiceRow.stdPrecision,
                            2
                        )
                    );
                }
            }

            return normalizePrecision(
                firstValue(
                    selectedInvoiceRow &&
                    selectedInvoiceRow.stdPrecision,
                    2
                )
            );
        }

        function roundPopupAmount(value) {
            var numericValue = Number(value || 0);
            var precision = getPopupAmountPrecision();
            var factor;

            if (isNaN(numericValue)) {
                return 0;
            }

            factor = Math.pow(10, precision);

            return Math.round(numericValue * factor) / factor;
        }

        function getPopupConversionContext(
            sourceCurrencyId
        ) {
            var targetCurrencyId;
            var conversionTypeId;
            var transactionDate;
            var adOrgId;
            var adClientId;

            if (!selectedInvoiceRow) {
                return null;
            }

            sourceCurrencyId = Number(
                sourceCurrencyId || 0
            );

            if (sourceCurrencyId <= 0) {
                sourceCurrencyId =
                    getPopupOriginalCurrencyId(
                        selectedInvoiceRow
                    );
            }

            targetCurrencyId = Number(
                getPayField('currencyId').val() || 0
            );

            conversionTypeId = Number(
                getPayField('conversionTypeId').val() || 0
            );

            transactionDate = String(
                getPayField('transactionDate').val() || ''
            ).trim();

            adOrgId = Number(
                getPayField('adOrgId').val() ||
                firstPositiveValue(
                    selectedInvoiceRow.adOrgId,
                    selectedInvoiceRow.organizationId
                ) ||
                (VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env.getCtx() &&
                    VIS.Env.getCtx().getAD_Org_ID
                    ? VIS.Env.getCtx().getAD_Org_ID()
                    : 0)
            );

            adClientId =
                VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env.getCtx() &&
                    VIS.Env.getCtx().getAD_Client_ID
                    ? Number(
                        VIS.Env.getCtx().getAD_Client_ID()
                    )
                    : 0;

            return {
                sourceCurrencyId:
                    sourceCurrencyId,
                targetCurrencyId:
                    targetCurrencyId,
                conversionTypeId:
                    conversionTypeId,
                transactionDate:
                    transactionDate,
                adOrgId: adOrgId,
                adClientId: adClientId
            };
        }

        function convertPopupAmount(
            amount,
            sourceCurrencyId
        ) {
            var context;
            var paramString;
            var convertedAmount;

            context = getPopupConversionContext(
                sourceCurrencyId
            );

            if (
                !context ||
                context.sourceCurrencyId <= 0 ||
                context.targetCurrencyId <= 0
            ) {
                return null;
            }

            if (
                !context.transactionDate ||
                context.conversionTypeId <= 0 ||
                context.adClientId <= 0 ||
                context.adOrgId < 0
            ) {
                return roundPopupAmount(amount);
            }

            if (
                context.sourceCurrencyId ===
                context.targetCurrencyId
            ) {
                return roundPopupAmount(amount);
            }

            paramString =
                amount.toString() + "," +
                context.sourceCurrencyId.toString() + "," +
                context.targetCurrencyId.toString() + "," +
                context.transactionDate + "," +
                context.conversionTypeId.toString() + "," +
                context.adClientId.toString() + "," +
                context.adOrgId.toString();

            convertedAmount = Number(
                VIS.dataContext.getJSONRecord(
                    "MConversionRate/CurrencyConvert",
                    paramString
                )
            );

            if (isNaN(convertedAmount)) {
                return null;
            }

            return roundPopupAmount(convertedAmount);
        }

        function convertPopupBaseAmount(baseAmount) {
            return convertPopupAmount(
                baseAmount,
                getPopupOriginalCurrencyId(
                    selectedInvoiceRow
                )
            );
        }

        function getPopupCurrentOpenAmount() {
            return convertPopupBaseAmount(
                getPopupBaseOpenAmount(
                    selectedInvoiceRow
                )
            );
        }

        function syncPopupAmountsFromBase() {
            var convertedPayAmount;
            var convertedDiscountAmount;
            var convertedWriteOffAmount;

            convertedPayAmount =
                convertPopupBaseAmount(
                    getPopupBasePaymentAmount(
                        selectedInvoiceRow
                    )
                );

            convertedDiscountAmount =
                convertPopupBaseAmount(
                    getPopupBaseDiscountAmount(
                        selectedInvoiceRow
                    )
                );

            convertedWriteOffAmount =
                convertPopupBaseAmount(
                    getPopupBaseWriteOffAmount(
                        selectedInvoiceRow
                    )
                );

            if (
                convertedPayAmount == null ||
                convertedDiscountAmount == null ||
                convertedWriteOffAmount == null
            ) {
                showPayError(
                    lbl(
                        'NoCurrencyConversion',
                        'No currency conversion found.'
                    )
                );

                getPayField('discountAmt').val('');
                getPayField('payAmt').val('0');
                return false;
            }

            getPayField('discountAmt').val(
                normalizeNumberOrEmpty(
                    convertedDiscountAmount
                )
            );

            getPayField('writeOffAmt').val(
                normalizeNumberOrEmpty(
                    convertedWriteOffAmount
                )
            );

            getPayField('payAmt').val(
                normalizeNumber(
                    convertedPayAmount
                )
            );

            $payDialogNotice.removeClass(
                'vas-upcoming-ap-runs-pay-error'
            );

            return true;
        }

        function recalculatePopupFromDiscount() {
            var openAmount;
            var discountAmt;
            var writeOffAmt;
            var payAmt;

            openAmount =
                getPopupCurrentOpenAmount();

            if (openAmount == null) {
                showPayError(
                    lbl(
                        'NoCurrencyConversion',
                        'No currency conversion found.'
                    )
                );
                return;
            }

            discountAmt = Number(
                getPayField('discountAmt').val() || 0
            );

            if (isNaN(discountAmt) || discountAmt < 0) {
                discountAmt = 0;
            }

            if (discountAmt > openAmount) {
                discountAmt = openAmount;
            }

            writeOffAmt = Number(
                getPayField('writeOffAmt').val() || 0
            );

            if (isNaN(writeOffAmt) || writeOffAmt < 0) {
                writeOffAmt = 0;
            }

            if (
                writeOffAmt >
                (openAmount - discountAmt)
            ) {
                writeOffAmt =
                    Math.max(
                        0,
                        openAmount - discountAmt
                    );
            }

            payAmt = roundPopupAmount(
                openAmount -
                discountAmt -
                writeOffAmt
            );

            getPayField('discountAmt').val(
                normalizeNumberOrEmpty(
                    roundPopupAmount(discountAmt)
                )
            );

            getPayField('payAmt').val(
                normalizeNumber(payAmt)
            );

            getPayField('writeOffAmt').val(
                normalizeNumberOrEmpty(
                    roundPopupAmount(writeOffAmt)
                )
            );
        }

        function recalculatePopupFromPayAmt() {
            var openAmount;
            var payAmt;
            var discountAmt;
            var writeOffAmt;
            var maximumPayAmt;

            openAmount =
                getPopupCurrentOpenAmount();

            if (openAmount == null) {
                showPayError(
                    lbl(
                        'NoCurrencyConversion',
                        'No currency conversion found.'
                    )
                );
                return;
            }

            payAmt = Number(
                getPayField('payAmt').val() || 0
            );

            if (isNaN(payAmt) || payAmt < 0) {
                payAmt = 0;
            }

            if (payAmt > openAmount) {
                payAmt = openAmount;
            }

            writeOffAmt = Number(
                getPayField('writeOffAmt').val() || 0
            );

            discountAmt = Number(
                getPayField('discountAmt').val() || 0
            );

            if (isNaN(writeOffAmt) || writeOffAmt < 0) {
                writeOffAmt = 0;
            }

            if (isNaN(discountAmt) || discountAmt < 0) {
                discountAmt = 0;
            }

            maximumPayAmt = roundPopupAmount(
                openAmount - discountAmt - writeOffAmt
            );

            if (maximumPayAmt < 0) {
                maximumPayAmt = 0;
            }

            if (payAmt > maximumPayAmt) {
                payAmt = maximumPayAmt;
            }

            getPayField('payAmt').val(
                normalizeNumber(
                    roundPopupAmount(payAmt)
                )
            );

            getPayField('writeOffAmt').val(
                normalizeNumberOrEmpty(
                    roundPopupAmount(writeOffAmt)
                )
            );
        }

        function recalculatePopupFromWriteOff() {
            var openAmount;
            var discountAmt;
            var writeOffAmt;
            var payAmt;

            openAmount =
                getPopupCurrentOpenAmount();

            if (openAmount == null) {
                showPayError(
                    lbl(
                        'NoCurrencyConversion',
                        'No currency conversion found.'
                    )
                );
                return;
            }

            discountAmt = Number(
                getPayField('discountAmt').val() || 0
            );

            writeOffAmt = Number(
                getPayField('writeOffAmt').val() || 0
            );

            if (isNaN(discountAmt) || discountAmt < 0) {
                discountAmt = 0;
            }

            if (isNaN(writeOffAmt) || writeOffAmt < 0) {
                writeOffAmt = 0;
            }

            if (discountAmt > openAmount) {
                discountAmt = openAmount;
            }

            if (
                writeOffAmt >
                (openAmount - discountAmt)
            ) {
                writeOffAmt =
                    Math.max(
                        0,
                        openAmount - discountAmt
                    );
            }

            payAmt = roundPopupAmount(
                openAmount -
                discountAmt -
                writeOffAmt
            );

            getPayField('writeOffAmt').val(
                normalizeNumberOrEmpty(
                    roundPopupAmount(writeOffAmt)
                )
            );

            getPayField('payAmt').val(
                normalizeNumber(payAmt)
            );
        }

        function updatePopupConvertedPaymentAmount() {
            if (
                !selectedInvoiceRow ||
                !$payDialogGrid ||
                !getPayField('payAmt').length
            ) {
                return;
            }

            syncPopupAmountsFromBase();
        }

        function renderPayDialogGrid(row) {
            var organizations;
            var bankAccounts;
            var vendors;
            var currencies;
            var conversionTypes;
            var documentTypes;
            var tenderTypes;
            var paymentMethods;

            var organizationId;
            var vendorId;
            var currencyId;
            var bankAccountId;
            var conversionTypeId;
            var docTypeId;
            var paymentMethodId;

            var html;

            if (
                !$payDialogGrid ||
                !row
            ) {
                return;
            }

            setPopupOriginalValues(row);

            organizations =
                getLookup('organizations');

            bankAccounts =
                getLookup('bankAccounts');

            vendors =
                getLookup('vendors');

            currencies =
                getLookup('currencies');

            conversionTypes =
                getLookup('conversionTypes');

            documentTypes =
                getLookupAny([
                    'documentTypes',
                    'docTypes'
                ]);

            tenderTypes =
                getLookup('tenderTypes');

            paymentMethods =
                getLookup('paymentMethods');

            organizationId =
                getValidLookupValue(
                    organizations,
                    firstPositiveValue(
                        row.organizationId,
                        row.adOrgId
                    )
                );

            vendorId =
                getValidLookupValue(
                    vendors,
                    firstPositiveValue(
                        row.vendorId,
                        row.cBPartnerId
                    )
                );

            /*
             * Display exactly the currency returned by
             * GetUpcomingAPRunDetails SQL query.
             * Do not validate, replace, or select a fallback currency.
             */
            currencyId = row.currencyId;

            bankAccountId =
                getValidLookupValue(
                    bankAccounts,
                    firstPositiveValue(
                        row.bankAccountId
                    )
                );

            conversionTypeId =
                getValidLookupValue(
                    conversionTypes,
                    firstPositiveValue(
                        row.conversionTypeId,
                        row.cConversionTypeId
                    )
                );

            if (!conversionTypeId) {
                conversionTypeId =
                    getFirstPositiveLookupValue(
                        conversionTypes
                    );
            }

            docTypeId =
                getValidLookupValue(
                    documentTypes,
                    firstPositiveValue(
                        row.docTypeId,
                        row.cDocTypeId
                    )
                );

            if (!docTypeId) {
                docTypeId =
                    getFirstPositiveLookupValue(
                        documentTypes
                    );
            }

            currentTenderType =
                getValidLookupValue(
                    tenderTypes,
                    firstValue(
                        row.tenderType,
                        row.paymentMethodValue
                    )
                );

            if (!currentTenderType) {
                currentTenderType =
                    getFirstLookupValue(
                        tenderTypes
                    );
            }

            paymentMethodId = 0;

            html =
                fieldHtml(
                    lbl(
                        'VAS_031_MessageOrganization',
                        'Organization'
                    ),

                    selectHtml(
                        'adOrgId',
                        organizations,
                        organizationId,
                        true
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageVendor',
                        'Vendor'
                    ),

                    selectHtml(
                        'vendorId',
                        vendors,
                        vendorId,
                        true
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
                        currencyId,
                        false
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
                        bankAccountId,
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageTransactionDate',
                        'Transaction Date'
                    ),

                    inputHtml(
                        'transactionDate',
                        'date',
                        formatDateForInput(
                            firstValue(
                                row.transactionDate,
                                row.dueDate,
                                row.dateTrx
                            )
                        ),
                        null,
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageCurrencyType',
                        'Currency Type'
                    ),

                    selectHtml(
                        'conversionTypeId',
                        conversionTypes,
                        conversionTypeId,
                        false
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
                        docTypeId,
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessagePaymentMethod',
                        'Payment Method'
                    ),

                    selectHtml(
                        'paymentMethodId',
                        paymentMethods,
                        paymentMethodId,
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'DiscountAmt',
                        'Discount'
                    ),

                    inputHtml(
                        'discountAmt',
                        'number',
                        normalizeNumberOrEmpty(
                            firstValue(
                                row.discountAmt,
                                row.discountAmount,
                                0
                            )
                        ),
                        '0.01',
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'WriteOffAmt',
                        'Write Off Amount'
                    ),

                    inputHtml(
                        'writeOffAmt',
                        'number',
                        normalizeNumberOrEmpty(
                            firstValue(
                                row.writeOffAmt,
                                row.writeOffAmount,
                                0
                            )
                        ),
                        '0.01',
                        false
                    ),

                    false
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageCheckNo',
                        'Check No.'
                    ),

                    inputHtml(
                        'checkNo',
                        'text',
                        firstValue(row.checkNo, ''),
                        null,
                        false
                    ),

                    false,
                    'data-check-field="true" style="display:none;"'
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageCheckDate',
                        'Check Date'
                    ),

                    inputHtml(
                        'checkDate',
                        'date',
                        formatDateForInput(
                            firstValue(row.checkDate, '')
                        ),
                        null,
                        false
                    ),

                    false,
                    'data-check-field="true" style="display:none;"'
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
                            firstValue(
                                row.payAmt,
                                row.openAmount,
                                row.amount,
                                0
                            )
                        ),
                        '0.01',
                        false
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageInvoice',
                        'Invoice'
                    ),

                    inputHtml(
                        'invoiceDocumentNo',
                        'text',
                        firstValue(
                            row.invoiceDocumentNo,
                            row.documentNo,
                            ''
                        ),
                        null,
                        true
                    ),

                    true
                ) +

                fieldHtml(
                    lbl(
                        'Description',
                        'Description'
                    ),

                    inputHtml(
                        'invoiceDescription',
                        'text',
                        firstValue(
                            row.description,
                            row.invoiceDescription,
                            ''
                        ),
                        null,
                        true
                    ),

                    true,
                    'data-full-span="true"'
                );

            $payDialogGrid.html(html);
            bindDatePickers();

            getPayField('paymentMethodId')
                .off('change.vasPaymentMethod')
                .on(
                    'change.vasPaymentMethod',
                    function () {
                        updateCheckFieldsVisibility();
                    }
                );

            getPayField('currencyId')
                .off('change.vasCurrencyConvert')
                .on(
                    'change.vasCurrencyConvert',
                    function () {
                        var selectedCurrencyId = Number(
                            $(this).val() || 0
                        );

                        refreshPopupBankAccounts(
                            selectedCurrencyId,
                            function (loaded) {
                                if (!loaded) {
                                    return;
                                }

                                updatePopupBankAccountField();
                                updatePopupConvertedPaymentAmount();
                            }
                        );
                    }
                );

            getPayField('conversionTypeId')
                .off('change.vasCurrencyConvert')
                .on(
                    'change.vasCurrencyConvert',
                    function () {
                        syncPopupAmountsFromBase();
                    }
                );

            getPayField('transactionDate')
                .off('change.vasCurrencyConvert')
                .on(
                    'change.vasCurrencyConvert',
                    function () {
                        syncPopupAmountsFromBase();
                    }
                );

            getPayField('discountAmt')
                .off('input.vasRecalculate change.vasRecalculate blur.vasRecalculate')
                .on(
                    'input.vasRecalculate change.vasRecalculate',
                    function () {
                        recalculatePopupFromDiscount();
                    }
                )
                .on(
                    'blur.vasRecalculate',
                    function () {
                        recalculatePopupFromDiscount();
                    }
                );

            getPayField('payAmt')
                .off('input.vasRecalculate change.vasRecalculate blur.vasRecalculate')
                .on(
                    'input.vasRecalculate change.vasRecalculate',
                    function () {
                        recalculatePopupFromPayAmt();
                    }
                )
                .on(
                    'blur.vasRecalculate',
                    function () {
                        recalculatePopupFromPayAmt();
                    }
                );

            getPayField('writeOffAmt')
                .off('input.vasRecalculate change.vasRecalculate blur.vasRecalculate')
                .on(
                    'input.vasRecalculate change.vasRecalculate',
                    function () {
                        recalculatePopupFromWriteOff();
                    }
                )
                .on(
                    'blur.vasRecalculate',
                    function () {
                        recalculatePopupFromWriteOff();
                    }
                );

            updateCheckFieldsVisibility();
            syncPopupAmountsFromBase();
        }

        function updatePayNotice(row) {
            var amountText;
            var documentText;
            var vendorText;

            if (
                !$payDialogNotice ||
                !row
            ) {
                return;
            }

            amountText = formatCurrencyAmount(
                firstValue(
                    row.openAmount,
                    row.amount,
                    row.payAmt,
                    0
                ),
                row.currencySymbol,
                row.currencyISO,
                row.stdPrecision
            );

            documentText = firstValue(
                row.invoiceDocumentNo,
                row.documentNo,
                ''
            );

            vendorText = firstValue(
                row.vendorName,
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
                        documentText
                            ? ' <strong>' +
                            escapeHtml(documentText) +
                            '</strong>'
                            : ''
                    ) +

                    (
                        vendorText
                            ? ' — ' +
                            escapeHtml(vendorText)
                            : ''
                    ) +

                    ' · ' +

                    escapeHtml(amountText) +

                    '. ' +

                    escapeHtml(
                        lbl(
                            'VAS_031_MessageReviewAndSave',
                            'Review and save.'
                        )
                    )
                );
        }
        function loadRunInvoiceDetails(run) {
            var cBPartnerId = Number(
                firstValue(
                    run && run.cBPartnerId,
                    run && run.vendorId,
                    0
                )
            );

            if (cBPartnerId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_BusinessPartnerRequired',
                        'Business partner is required.'
                    )
                );

                setPayDialogBusy(
                    false,
                    false
                );

                return;
            }

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetUpcomingAPRunDetails',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    runDate:
                        firstValue(
                            run.runDate,
                            run.dueDate
                        ),

                    paymentMethodId:
                        Number(
                            firstValue(
                                run.paymentMethodId,
                                run.va009PaymentMethodId,
                                0
                            )
                        ),

                    currencyId:
                        Number(
                            firstValue(
                                run.currencyId,
                                run.cCurrencyId,
                                0
                            )
                        ),

                    cBPartnerId:
                        cBPartnerId
                },

                success: function (response) {
                    var data = normalizeResponse(
                        response
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_ErrorLoading',
                                    'Could not load invoice details.'
                                )
                            )
                        );

                        return;
                    }

                    invoiceRows = $.isArray(data.rows)
                        ? data.rows
                        : [];

                    if (invoiceRows.length === 0) {
                        showPayError(
                            lbl(
                                'VAS_031_MessageNoData',
                                'No Data'
                            )
                        );

                        return;
                    }

                    selectInvoiceRow(
                        invoiceRows[0]
                    );
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load invoice details.'
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
            selectedInvoiceRow = null;
            invoiceRows = [];

            $payDialogTitle.text(
                lbl(
                    'VAS_031_MessageCreatePayment',
                    'Create Payment'
                )
            );

            $payDialogSub.text(
                lbl(
                    'VAS_031_PrefilledFromUpcoming',
                    'Pre-filled from upcoming'
                ) +
                ' · ' +
                (
                    run.paymentMethodName ||
                    lbl(
                        'VAS_031_MessageNotSpecified',
                        'Not Specified'
                    )
                )
            );

            $payDialogNotice
                .removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .text(
                    lbl(
                        'VAS_031_MessageLoadingDetails',
                        'Loading invoice details'
                    )
                );

            $invoiceList.empty();
            $payDialogGrid.empty();

            $payDialog.css(
                'display',
                'flex'
            );

            $('body').addClass(
                'vas-upcoming-ap-runs-body-lock'
            );

            setPayDialogBusy(
                true,
                false
            );

            ensurePopupLookups(
                firstPositiveValue(
                    run &&
                    run.currencyId,
                    run &&
                    run.cCurrencyId
                ),
                function (loaded) {
                    if (!loaded) {
                        setPayDialogBusy(
                            false,
                            false
                        );

                        return;
                    }

                    loadRunInvoiceDetails(run);
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
            selectedInvoiceRow = null;
            popupOriginalValues = null;
            invoiceRows = [];

            getPayField('checkNo').val('');
            getPayField('checkDate').val('');

            $payDialog.hide();

            $invoiceList.empty();
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

        function getPayField(name) {
            return $payDialogGrid.find(
                '[data-pay-field="' +
                name +
                '"]'
            );
        }

        function bindDatePickers() {
            $payDialogGrid
                .find('input[type="date"].vas-upcoming-ap-runs-edit-control')
                .off('click.vasDatePicker focus.vasDatePicker')
                .on(
                    'click.vasDatePicker focus.vasDatePicker',
                    function () {
                        if (
                            this.showPicker &&
                            !this.readOnly &&
                            !this.disabled
                        ) {
                            try {
                                this.showPicker();
                            }
                            catch (ignore) {
                                // Native date picker behavior is browser-controlled.
                            }
                        }
                    }
                );
        }

        function readPayDialogPayload() {
            var payload;
            var maximumAmount;
            var selectedPaymentMethod;

            if (!selectedInvoiceRow) {
                showPayError(
                    lbl(
                        'VAS_031_MessageInvoiceRequired',
                        'Select an invoice.'
                    )
                );

                return null;
            }

            payload = {
                invoiceId: Number(
                    firstPositiveValue(
                        selectedInvoiceRow.invoiceId,
                        selectedInvoiceRow.sourceInvoiceId,
                        selectedInvoiceRow.cInvoiceId
                    )
                ),

                invoicePayScheduleId: Number(
                    firstPositiveValue(
                        selectedInvoiceRow.invoicePayScheduleId,
                        selectedInvoiceRow.cInvoicePayScheduleId
                    )
                ),

                adOrgId: Number(
                    getPayField(
                        'adOrgId'
                    ).val() || 0
                ),

                bankAccountId: Number(
                    getPayField(
                        'bankAccountId'
                    ).val() || 0
                ),

                vendorId: Number(
                    getPayField(
                        'vendorId'
                    ).val() || 0
                ),

                currencyId: Number(
                    getPayField(
                        'currencyId'
                    ).val() || 0
                ),

                conversionTypeId: Number(
                    getPayField(
                        'conversionTypeId'
                    ).val() || 0
                ),

                docTypeId: Number(
                    getPayField(
                        'docTypeId'
                    ).val() || 0
                ),

                tenderType: String(
                    currentTenderType || ''
                ).trim(),

                paymentMethodId: Number(
                    getPayField(
                        'paymentMethodId'
                    ).val() || 0
                ),

                checkNo: String(
                    getPayField(
                        'checkNo'
                    ).val() || ''
                ).trim(),

                checkDate: String(
                    getPayField(
                        'checkDate'
                    ).val() || ''
                ).trim(),

                transactionDate: String(
                    getPayField(
                        'transactionDate'
                    ).val() || ''
                ).trim(),

                documentNo: '',

                paymentDescription: String(
                    getPayField(
                        'invoiceDescription'
                    ).val() || ''
                ).trim(),

                discountAmt: Number(
                    getPayField(
                        'discountAmt'
                    ).val() || 0
                ),

                writeOffAmt: Number(
                    getPayField(
                        'writeOffAmt'
                    ).val() || 0
                ),

                payAmt: Number(
                    getPayField(
                        'payAmt'
                    ).val() || 0
                )
            };

            if (payload.invoiceId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_SourceInvoiceRequired',
                        'Source invoice is required.'
                    )
                );

                return null;
            }

            if (payload.invoicePayScheduleId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_InvoicePayScheduleRequired',
                        'Invoice payment schedule is required.'
                    )
                );

                return null;
            }

            if (payload.adOrgId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_OrganizationRequired',
                        'Organization is required.'
                    )
                );

                return null;
            }

            if (payload.bankAccountId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_MessageBankAccountRequired',
                        'Bank account is required.'
                    )
                );

                return null;
            }

            if (payload.vendorId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_MessageVendorRequired',
                        'Vendor is required.'
                    )
                );

                return null;
            }

            if (payload.currencyId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_MessageCurrencyRequired',
                        'Currency is required.'
                    )
                );

                return null;
            }

            if (payload.conversionTypeId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_ConversionTypeRequired',
                        'Currency type is required.'
                    )
                );

                return null;
            }

            if (payload.docTypeId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_DocumentTypeRequired',
                        'Document type is required.'
                    )
                );

                return null;
            }

            if (payload.paymentMethodId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_PaymentMethodRequired',
                        'Payment method is required.'
                    )
                );

                return null;
            }

            selectedPaymentMethod =
                getSelectedLookupItem(
                    getLookup('paymentMethods'),
                    payload.paymentMethodId
                );

            if (isCheckPaymentMethodItem(selectedPaymentMethod)) {
                if (!payload.checkNo) {
                    showPayError(
                        lbl(
                            'VAS_031_MessageCheckNoRequired',
                            'Check number is required.'
                        )
                    );

                    return null;
                }

                if (!payload.checkDate) {
                    showPayError(
                        lbl(
                            'VAS_031_MessageCheckDateRequired',
                            'Check date is required.'
                        )
                    );

                    return null;
                }
            }
            else {
                payload.checkNo = '';
                payload.checkDate = '';
            }

            if (!payload.transactionDate) {
                showPayError(
                    lbl(
                        'VAS_031_TransactionDateRequired',
                        'Transaction date is required.'
                    )
                );

                return null;
            }

            if (
                isNaN(payload.discountAmt) ||
                payload.discountAmt < 0
            ) {
                showPayError(
                    lbl(
                        'VAS_031_MessageDiscountInvalid',
                        'Discount amount must be zero or greater.'
                    )
                );

                return null;
            }

            if (
                isNaN(payload.payAmt) ||
                payload.payAmt <= 0
            ) {
                showPayError(
                    lbl(
                        'VAS_031_PaymentAmountRequired',
                        'Payment amount must be greater than zero.'
                    )
                );

                return null;
            }

            maximumAmount =
                getPopupCurrentOpenAmount();

            if (
                !isNaN(maximumAmount) &&
                maximumAmount > 0 &&
                (
                    payload.payAmt >
                    maximumAmount ||
                    (
                        payload.payAmt +
                        payload.discountAmt +
                        payload.writeOffAmt
                    ) > maximumAmount
                )
            ) {
                showPayError(
                    lbl(
                        'VAS_031_PaymentExceedsOpenAmount',
                        'Payment amount and discount exceed invoice open amount.'
                    )
                );

                return null;
            }

            return payload;
        }

        function savePayDialog() {
            var payload;

            if (
                !selectedRun ||
                !selectedInvoiceRow ||
                saveInProgress
            ) {
                return;
            }

            payload = readPayDialogPayload();

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
                data: payload,

                success: function (response) {
                    var data = normalizeResponse(
                        response
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        showPayError(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_031_CouldNotSaveAPPayment',
                                    'Could not save AP payment.'
                                )
                            )
                        );

                        return;
                    }

                    saveInProgress = false;

                    setPayDialogBusy(
                        false,
                        false
                    );

                    getPayField('checkNo').val('');
                    getPayField('checkDate').val('');

                    closePayDialog();

                    popupLookups = null;

                    loadData();


                    if (
                        VIS &&
                        VIS.ADialog &&
                        VIS.ADialog.info
                    ) {

                        var msg = String(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_031_PaymentCreatedSuccessfully',
                                    'AP payment created successfully.'
                                )
                            )
                        )
                            .replace(/[\[\]]/g, '')
                            .replace(/\s{2,}/g, ' ')
                            .trim();


                        VIS.ADialog.info(
                          null , null ,    msg
                        );
                    }
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_031_CouldNotSaveAPPayment',
                                'Could not save AP payment.'
                            )
                        )
                    );
                },

                complete: function () {
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

        function createPayDialog() {
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

                '<div class="vas-upcoming-ap-runs-pay-heading">' +

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
                '">×</button>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-notice"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-content">' +

                '<section class="vas-upcoming-ap-runs-invoice-panel">' +

                '<div class="vas-upcoming-ap-runs-invoice-list-title">' +

                escapeHtml(
                    lbl(
                        'VAS_031_MessageInvoices',
                        'Invoices'
                    )
                ) +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-invoice-list"></div>' +

                '</section>' +

                '<section class="vas-upcoming-ap-runs-form-panel">' +

                '<div class="vas-upcoming-ap-runs-pay-grid"></div>' +

                '</section>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-footer">' +

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

                '<span class="vas-upcoming-ap-runs-pay-save-label">' +

                escapeHtml(
                    lbl(
                        'VAS_031_MessageSavePayment',
                        'Save payment'
                    )
                ) +

                '</span>' +

                '</button>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>' +

                '</div>' +

                '</div>'
            );

            $('body').append($payDialog);

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

            $invoiceList =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-invoice-list'
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
                    '.vas-upcoming-ap-runs-pay-save-label'
                );

            $payDialogBusy =
                $payDialog.find(
                    '.vas-upcoming-ap-runs-pay-busy'
                );

            $payDialog
                .find(
                    '.vas-upcoming-ap-runs-pay-close,' +
                    '.vas-upcoming-ap-runs-pay-cancel,' +
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
                'keydown.vas-upcoming-ap-runs-' +
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

            $payDialog.hide();
        }

        function createLayout() {
            var $head;

            if ($card && $card.length) {
                return;
            }
            var $headLeft;
            var $headText;
            var $iconBox;
            var $icon;
            var $title;
            var $sub;

            $card = $(
                '<div class="vas-upcoming-ap-runs-card">'
            );

            $head = $(
                '<div class="vas-upcoming-ap-runs-head">'
            );

            $headLeft = $(
                '<div class="vas-upcoming-ap-runs-head-left">'
            );

            $headText = $(
                '<div class="vas-upcoming-ap-runs-head-text">'
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
                'aria-hidden="true">' +

                '<circle cx="12" cy="12" r="9"></circle>' +

                '<path d="M12 7v5l3 2"></path>' +

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

            $sub = $(
                '<div class="vas-upcoming-ap-runs-sub">'
            ).text(
                lbl(
                    'VAS_031_MessageNext7Days',
                    'Next 7 days'
                )
            );

            $iconBox.append($icon);

            $headText
                .append($title)
                .append($sub);

            $headLeft
                .append($iconBox)
                .append($headText);

            $head.append($headLeft);

            $busy = $(
                '<div ' +
                'id="VAS-gljtm-busy-' +
                escapeHtml($self.AD_UserHomeWidgetID) +
                '" ' +
                'class="vas-upcoming-ap-runs-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $state = $(
                '<div class="vas-upcoming-ap-runs-state">'
            );

            $body = $(
                '<div class="vas-upcoming-ap-runs-body">'
            );

            $footer = $(
                '<div class="vas-upcoming-ap-runs-footer">'
            );

            $showingText = $(
                '<div class="vas-upcoming-ap-runs-showing">'
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

                '<svg ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2.4" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<path d="M15 18l-6-6 6-6"></path>' +

                '</svg>' +

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

                '<svg ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2.4" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<path d="M9 18l6-6-6-6"></path>' +

                '</svg>' +

                '</button>'
            );

            $pagerPrev.on(
                'click',
                function () {
                    if (pageNo > 1) {
                        pageNo--;
                        renderPage();
                    }
                }
            );

            $pagerNext.on(
                'click',
                function () {
                    if (pageNo < totalPages) {
                        pageNo++;
                        renderPage();
                    }
                }
            );

            $pager
                .append($pagerPrev)
                .append($pagerText)
                .append($pagerNext);

            $footer
                .append($showingText)
                .append($pager);

            $card
                .append($head)
                .append($busy)
                .append($state)
                .append($body)
                .append($footer);

            $root.append($card);

            createPayDialog();
        }

        this.init = function (
            windowNo,
            frame
        ) {
            $self.windowNo =
                windowNo || 0;

            $self.frame =
                frame || null;

            if (
                frame &&
                frame.widgetInfo
            ) {
                $self.AD_UserHomeWidgetID =
                    Number(
                        frame.widgetInfo
                            .AD_UserHomeWidgetID ||
                        0
                    );
            }

            createLayout();

            if (
                $self.frame &&
                $self.frame.getContentGrid &&
                !$root.parent().length
            ) {
                $self.frame
                    .getContentGrid()
                    .append($root);
            }

            startAdaptiveRowObserver();
            loadData();
        };

        /*
         * VIS widget lifecycle compatibility.
         */
        this.initialize = function () {
            createLayout();
            startAdaptiveRowObserver();
            loadData();
        };

        this.Initalize = function () {
            $self.initialize();
        };

        this.refresh = function () {
            popupLookups = null;
            loadData();
        };

        this.refreshWidget = function () {
            popupLookups = null;
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.dispose = function () {
            disposeWidget();
        };

        this.disposeComponent = function () {
            disposeWidget();
        };

        function disposeWidget() {
            if (isDisposed) {
                return;
            }

            isDisposed = true;

            stopAdaptiveRowObserver();

            $(document).off(
                'keydown.vas-upcoming-ap-runs-' +
                $self.AD_UserHomeWidgetID
            );

            selectedRun = null;
            selectedInvoiceRow = null;

            runsData = [];
            invoiceRows = [];
            popupLookups = null;

            if ($payDialog) {
                $payDialog.remove();
                $payDialog = null;
            }

            if ($root) {
                $root.remove();
            }

            $('body').removeClass(
                'vas-upcoming-ap-runs-body-lock'
            );

            $card = null;
            $body = null;
            $state = null;
            $busy = null;
            $footer = null;
            $showingText = null;
            $pager = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;

            $payDialogTitle = null;
            $payDialogSub = null;
            $payDialogNotice = null;
            $invoiceList = null;
            $payDialogGrid = null;
            $payDialogSave = null;
            $payDialogSaveLabel = null;
            $payDialogBusy = null;

            $self.frame = null;
        }
    };


    VAS.VAS_031_UpcomingAPRunsWidget.prototype.init =
        function (windowNo, frame) {
            if (
                typeof this.init === 'function' &&
                Object.prototype.hasOwnProperty.call(
                    this,
                    'init'
                )
            ) {
                return this.init(
                    windowNo,
                    frame
                );
            }
        };

    /**
     * Handles widget size changes.
     */
    VAS.VAS_031_UpcomingAPRunsWidget.prototype
        .widgetSizeChange =
        function (height, width) {
            var $root =
                this.getRoot();

            if (!$root) {
                return;
            }

            $root.toggleClass(
                'vas-upcoming-ap-runs-compact',
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
    VAS.VAS_031_UpcomingAPRunsWidget.prototype
        .refreshWidget =
        function () {
            if (
                typeof this.refresh === 'function'
            ) {
                this.refresh();
            }
        };

    /**
     * Disposes widget.
     */
    VAS.VAS_031_UpcomingAPRunsWidget.prototype.dispose =
        function () {
            if (
                typeof this.disposeComponent === 'function'
            ) {
                this.disposeComponent();
            }
        };

})(VAS, jQuery);

