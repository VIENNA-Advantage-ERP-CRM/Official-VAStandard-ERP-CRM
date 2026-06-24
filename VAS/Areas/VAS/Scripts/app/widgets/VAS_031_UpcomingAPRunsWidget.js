/**
 * VAS_031 Upcoming AP Runs Widget
 *
 * Source:
 *     C_Invoice
 *
 * Grouping:
 *     Due Date + Payment Method + Currency
 *
 * Popup:
 *     Displays individual invoices.
 *
 * Create:
 *     Creates C_Payment linked with C_Invoice_ID.
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

        var selectedRun = null;
        var selectedInvoiceRow = null;

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
            var numberValue;

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

                numberValue = Number(value);

                if (
                    !isNaN(numberValue) &&
                    numberValue > 0
                ) {
                    return numberValue;
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

            if (isNaN(numericValue)) {
                numericValue = 0;
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

            return (
                (
                    currencySymbol ||
                    currencyISO ||
                    ''
                ) +
                (
                    currencySymbol ||
                        currencyISO
                        ? ' '
                        : ''
                ) +
                amountText
            );
        }

        function normalizeNumber(value) {
            var numberValue = Number(value || 0);

            return isNaN(numberValue)
                ? '0'
                : String(numberValue);
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
            var i;
            var list;

            names = $.isArray(names)
                ? names
                : [];

            for (
                i = 0;
                i < names.length;
                i++
            ) {
                list = getLookup(names[i]);

                if (list.length > 0) {
                    return list;
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
                item.code,
                value
            );
        }

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
                itemValue = getLookupItemValue(
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
                value = getLookupItemValue(
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

        function getFirstPositiveLookupValue(items) {
            var i;
            var value;
            var numberValue;

            items = $.isArray(items)
                ? items
                : [];

            for (
                i = 0;
                i < items.length;
                i++
            ) {
                value = getLookupItemValue(
                    items[i]
                );

                numberValue = Number(value);

                if (
                    !isNaN(numberValue) &&
                    numberValue > 0
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
                '"' +
                (
                    disabled
                        ? ' disabled'
                        : ''
                ) +
                '>';

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
            var paymentMethodName =
                run.paymentMethodName ||
                lbl(
                    'VAS_031_MessageNotSpecified',
                    'Not Specified'
                );

            var paymentCount =
                Number(run.paymentCount || 0);

            var amount =
                Number(
                    firstValue(
                        run.totalAmount,
                        run.amount,
                        0
                    )
                );

            var dueDateText =
                firstValue(
                    run.dueDateText,
                    run.runDateText,
                    formatDate(
                        firstValue(
                            run.dueDate,
                            run.runDate
                        )
                    )
                );

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
            ).text(paymentMethodName);

            var $meta = $(
                '<div class="vas-upcoming-ap-runs-meta">'
            ).text(
                dueDateText +
                ' · ' +
                paymentCount.toLocaleString(
                    window.navigator.language
                ) +
                ' ' +
                (
                    paymentCount === 1
                        ? 'invoice'
                        : 'invoices'
                )
            );

            var $amount = $(
                '<span class="vas-upcoming-ap-runs-amount">'
            ).text(
                formatCurrencyAmount(
                    amount,
                    run.currencySymbol,
                    run.currencyISO,
                    run.stdPrecision
                )
            );

            var $payButton = $(
                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-btn">' +
                escapeHtml(
                    lbl(
                        'VAS_031_MessagePay',
                        'Pay'
                    )
                ) +
                '<span class="vas-upcoming-ap-runs-pay-arrow">›</span>' +
                '</button>'
            );

            $payButton.on(
                'click',
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    openPayDialog(run);
                }
            );

            /*
             * الضغط على السطر أيضًا يفتح التفاصيل.
             */
            $row.on(
                'click',
                function () {
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
                        return (
                            Number(
                                firstValue(
                                    run.totalAmount,
                                    run.amount,
                                    0
                                )
                            ) > 0
                        );
                    }
                )
                : [];

            if (runsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;

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

        function setPayDialogBusy(
            show,
            isSaving
        ) {
            var busy = !!show;
            var saving = busy && !!isSaving;

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
                            (
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            ) ||
                            'Could not load lookup data.'
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
                            'Could not load lookup data.'
                        )
                    );

                    callback(false);
                }
            });
        }

        function renderInvoiceRows(rows) {
            var html;
            var i;
            var row;
            var amountText;

            rows = $.isArray(rows)
                ? rows
                : [];

            html =
                '<div class="vas-upcoming-ap-runs-invoice-list-title">' +
                escapeHtml('Invoices') +
                '</div>' +

                '<div class="vas-upcoming-ap-runs-invoice-list-body">';

            for (
                i = 0;
                i < rows.length;
                i++
            ) {
                row = rows[i];

                amountText = formatCurrencyAmount(
                    row.openAmount ||
                    row.amount ||
                    0,

                    row.currencySymbol,

                    row.currencyISO,

                    row.stdPrecision
                );

                html +=
                    '<button ' +
                    'type="button" ' +
                    'class="vas-upcoming-ap-runs-invoice-row' +
                    (
                        selectedInvoiceRow &&
                            Number(selectedInvoiceRow.invoiceId) ===
                            Number(row.invoiceId)
                            ? ' is-selected'
                            : ''
                    ) +
                    '" ' +
                    'data-invoice-index="' +
                    i +
                    '">' +

                    '<span class="vas-upcoming-ap-runs-invoice-main">' +

                    '<strong>' +
                    escapeHtml(
                        row.invoiceDocumentNo ||
                        row.documentNo ||
                        ''
                    ) +
                    '</strong>' +

                    '<small>' +
                    escapeHtml(
                        row.vendorName || ''
                    ) +
                    '</small>' +

                    '</span>' +

                    '<span class="vas-upcoming-ap-runs-invoice-side">' +

                    '<strong>' +
                    escapeHtml(amountText) +
                    '</strong>' +

                    '<small>' +
                    escapeHtml(
                        formatDate(
                            row.dueDate
                        )
                    ) +
                    '</small>' +

                    '</span>' +

                    '</button>';
            }

            html += '</div>';

            $invoiceList.html(html);

            $invoiceList
                .find(
                    '[data-invoice-index]'
                )
                .off('click')
                .on(
                    'click',
                    function () {
                        var index = Number(
                            $(this).attr(
                                'data-invoice-index'
                            )
                        );

                        if (
                            isNaN(index) ||
                            !invoiceRows[index]
                        ) {
                            return;
                        }

                        selectInvoiceRow(
                            invoiceRows[index]
                        );
                    }
                );
        }

        function selectInvoiceRow(row) {
            selectedInvoiceRow = row;

            renderInvoiceRows(
                invoiceRows
            );

            renderPayDialogGrid(
                row
            );

            updatePayNotice(
                row
            );

            setPayDialogBusy(
                false,
                false
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

            var organizationId;
            var vendorId;
            var currencyId;
            var bankAccountId;
            var conversionTypeId;
            var docTypeId;
            var tenderType;

            var html;

            if (
                !$payDialogGrid ||
                !row
            ) {
                return;
            }

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
                getLookupAny([
                    'tenderTypes',
                    'paymentMethods'
                ]);

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

            currencyId =
                getValidLookupValue(
                    currencies,
                    firstPositiveValue(
                        row.currencyId,
                        row.cCurrencyId
                    )
                );

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

            tenderType =
                getValidLookupValue(
                    tenderTypes,
                    firstValue(
                        row.tenderType
                    )
                );

            if (!tenderType) {
                tenderType =
                    getFirstLookupValue(
                        tenderTypes
                    );
            }

            html =
                fieldHtml(
                    'Invoice',
                    inputHtml(
                        'invoiceDocumentNo',
                        'text',
                        row.invoiceDocumentNo ||
                        row.documentNo ||
                        '',
                        null,
                        true
                    ),
                    true
                ) +

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
                        true
                    ),

                    true
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
                        'VAS_031_MessageTenderType',
                        'Tender Type'
                    ),

                    selectHtml(
                        'tenderType',
                        tenderTypes,
                        tenderType,
                        false
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
                            firstValue(
                                row.openAmount,
                                row.amount,
                                row.payAmt,
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
                        'VAS_031_MessageDocumentNo',
                        'Payment Document No.'
                    ),

                    inputHtml(
                        'documentNo',
                        'text',
                        '',
                        null,
                        false
                    ),

                    false
                );

            $payDialogGrid.html(html);
        }

        function updatePayNotice(row) {
            var amountText;

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

            $payDialogNotice
                .removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                )
                .html(
                    escapeHtml(
                        'Pre-filled for invoice'
                    ) +

                    ' <strong>' +

                    escapeHtml(
                        row.invoiceDocumentNo ||
                        row.documentNo ||
                        ''
                    ) +

                    '</strong>' +

                    (
                        row.vendorName
                            ? ' — ' +
                            escapeHtml(
                                row.vendorName
                            )
                            : ''
                    ) +

                    ' · ' +

                    escapeHtml(amountText) +

                    '. ' +

                    escapeHtml(
                        'Review and save.'
                    )
                );
        }

        function loadRunInvoiceDetails(run) {
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
                            run.paymentMethodId ||
                            0
                        ),

                    currencyId:
                        Number(
                            firstPositiveValue(
                                run.currencyId,
                                run.cCurrencyId
                            )
                        )
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
                            (
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            ) ||
                            'Could not load invoice details.'
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
                            'Could not load invoice details.'
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
                        'Loading invoice details'
                    )
                );

            $invoiceList.empty();
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
                        setPayDialogBusy(
                            false,
                            false
                        );

                        return;
                    }

                    loadRunInvoiceDetails(
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
            selectedInvoiceRow = null;
            invoiceRows = [];

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

        function readPayDialogPayload() {
            var payload;

            if (!selectedInvoiceRow) {
                showPayError(
                    'Select an invoice.'
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
                    getPayField(
                        'tenderType'
                    ).val() || ''
                ).trim(),

                transactionDate: String(
                    getPayField(
                        'transactionDate'
                    ).val() || ''
                ).trim(),

                documentNo: String(
                    getPayField(
                        'documentNo'
                    ).val() || ''
                ).trim(),

                payAmt: Number(
                    getPayField(
                        'payAmt'
                    ).val() || 0
                )
            };

            if (payload.invoiceId <= 0) {
                showPayError(
                    'Source invoice is required.'
                );

                return null;
            }

            if (payload.adOrgId <= 0) {
                showPayError(
                    'Organization is required.'
                );

                return null;
            }

            if (payload.bankAccountId <= 0) {
                showPayError(
                    'Bank account is required.'
                );

                return null;
            }

            if (payload.vendorId <= 0) {
                showPayError(
                    'Vendor is required.'
                );

                return null;
            }

            if (payload.currencyId <= 0) {
                showPayError(
                    'Currency is required.'
                );

                return null;
            }

            if (payload.conversionTypeId <= 0) {
                showPayError(
                    'Currency type is required.'
                );

                return null;
            }

            if (payload.docTypeId <= 0) {
                showPayError(
                    'Document type is required.'
                );

                return null;
            }

            if (!payload.tenderType) {
                showPayError(
                    'Tender type is required.'
                );

                return null;
            }

            if (!payload.transactionDate) {
                showPayError(
                    'Transaction date is required.'
                );

                return null;
            }

            if (
                isNaN(payload.payAmt) ||
                payload.payAmt <= 0
            ) {
                showPayError(
                    'Payment amount must be greater than zero.'
                );

                return null;
            }

            if (
                payload.payAmt >
                Number(
                    selectedInvoiceRow.openAmount ||
                    selectedInvoiceRow.amount ||
                    0
                )
            ) {
                showPayError(
                    'Payment amount exceeds invoice open amount.'
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
                            (
                                data &&
                                (
                                    data.error ||
                                    data.errorText ||
                                    data.message
                                )
                            ) ||
                            'Could not save AP payment.'
                        );

                        return;
                    }

                    saveInProgress = false;

                    setPayDialogBusy(
                        false,
                        false
                    );

                    closePayDialog();

                    popupLookups = null;

                    loadData();

                    if (
                        VIS &&
                        VIS.ADialog &&
                        VIS.ADialog.info
                    ) {
                        VIS.ADialog.info(
                            (
                                data.message ||
                                'AP payment created successfully.'
                            )
                        );
                    }
                },

                error: function (xhr) {
                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            'Could not save AP payment.'
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

                '<div>' +

                '<div class="vas-upcoming-ap-runs-pay-title"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-sub"></div>' +

                '</div>' +

                '<button ' +
                'type="button" ' +
                'class="vas-upcoming-ap-runs-pay-close">×</button>' +

                '</div>' +

                '<div class="vas-upcoming-ap-runs-pay-notice"></div>' +

                '<div class="vas-upcoming-ap-runs-invoice-list"></div>' +

                '<div class="vas-upcoming-ap-runs-pay-grid"></div>' +

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

                '<div class="vas-upcoming-ap-runs-pay-busy"></div>' +

                '</div>' +

                '</div>'
            );

            $('body').append($payDialog);

            $payDialogTitle = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-title'
            );

            $payDialogSub = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-sub'
            );

            $payDialogNotice = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-notice'
            );

            $invoiceList = $payDialog.find(
                '.vas-upcoming-ap-runs-invoice-list'
            );

            $payDialogGrid = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-grid'
            );

            $payDialogSave = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-save'
            );

            $payDialogSaveLabel = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-save-label'
            );

            $payDialogBusy = $payDialog.find(
                '.vas-upcoming-ap-runs-pay-busy'
            );

            $payDialog.find(
                '.vas-upcoming-ap-runs-pay-close,' +
                '.vas-upcoming-ap-runs-pay-cancel,' +
                '.vas-upcoming-ap-runs-pay-scrim'
            ).on(
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

            $payDialog.hide();
        }

        function createLayout() {
            $card = $(
                '<div class="vas-upcoming-ap-runs-card">'
            );

            var $header = $(
                '<div class="vas-upcoming-ap-runs-header">'
            );

            var $headerText = $(
                '<div class="vas-upcoming-ap-runs-header-text">'
            );

            var $title = $(
                '<div class="vas-upcoming-ap-runs-title">'
            ).text(
                lbl(
                    'VAS_031_MessageUpcomingRuns',
                    'Upcoming runs'
                )
            );

            var $subtitle = $(
                '<div class="vas-upcoming-ap-runs-subtitle">'
            ).text(
                lbl(
                    'VAS_031_MessageNext7Days',
                    'Next 7 days'
                )
            );

            $headerText
                .append($title)
                .append($subtitle);

            $header.append($headerText);

            $body = $(
                '<div class="vas-upcoming-ap-runs-body">'
            );

            $state = $(
                '<div class="vas-upcoming-ap-runs-state">'
            );

            $busy = $(
                '<div class="vas-upcoming-ap-runs-busy">'
            );

            $pager = $(
                '<div class="vas-upcoming-ap-runs-pager">'
            );

            $pagerPrev = $(
                '<button type="button">' +
                escapeHtml(
                    lbl(
                        'VAS_Previous',
                        'Previous'
                    )
                ) +
                '</button>'
            );

            $pagerText = $(
                '<span>'
            );

            $pagerNext = $(
                '<button type="button">' +
                escapeHtml(
                    lbl(
                        'VAS_Next',
                        'Next'
                    )
                ) +
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

            $card
                .append($header)
                .append($busy)
                .append($state)
                .append($body)
                .append($pager);

            $root.append($card);

            createPayDialog();
        }

        this.init = function (windowNo, frame) {
            $self.windowNo = windowNo || 0;
            $self.frame = frame || null;

            createLayout();

            if (
                $self.frame &&
                $self.frame.getContentGrid
            ) {
                $self.frame
                    .getContentGrid()
                    .append($root);
            }

            loadData();
        };

        this.refresh = function () {
            popupLookups = null;
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.dispose = function () {
            isDisposed = true;

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

            $self.frame = null;
        };
    };

})(VAS, jQuery);