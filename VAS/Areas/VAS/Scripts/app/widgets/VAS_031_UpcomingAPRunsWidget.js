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
 * 19  | Vendor                               | VAS_032_MessageVendor
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

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_031_UpcomingAPRunsWidget = function () {

        var $self = this;

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-upcoming-ap-runs-root">');
        var $card;
        var $body;
        var $pager;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        var $busy;
        var $state;

        var $payDialog;
        var $payDialogTitle;
        var $payDialogSub;
        var $payDialogNotice;
        var $payDialogGrid;
        var $payDialogSave;
        var $payDialogSaveLabel;
        var $payDialogBusy;

        var isDisposed = false;
        var runsData = [];
        var selectedRun = null;
        var selectedPaymentRow = null;
        var paymentRows = [];
        var popupLookups = null;
        var saveInProgress = false;

        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text && text !== '[' + key + ']'
                ? text
                : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-upcoming-ap-runs-card">');

            var $head = $('<div class="vas-upcoming-ap-runs-head">');

            var $headLeft = $(
                '<div class="vas-upcoming-ap-runs-head-left">'
            );

            var $titleRow = $(
                '<div class="vas-upcoming-ap-runs-title-row">'
            );

            var $iconBox = $(
                '<span class="vas-upcoming-ap-runs-icon-box">'
            );

            var $icon = $(
                '<svg class="vas-upcoming-ap-runs-icon" ' +
                'viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round" ' +
                'aria-hidden="true" ' +
                'focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            var $title = $(
                '<div class="vas-upcoming-ap-runs-title">'
            ).text(
                lbl(
                    'VAS_031_MessageUpcomingRuns',
                    'Upcoming runs'
                )
            );

            var $sub = $(
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
                '<button type="button" ' +
                'class="vas-upcoming-ap-runs-page-btn" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Previous',
                        'Previous'
                    )
                ) +
                '">‹</button>'
            );

            $pagerText = $(
                '<span class="vas-upcoming-ap-runs-page-text">'
            );

            $pagerNext = $(
                '<button type="button" ' +
                'class="vas-upcoming-ap-runs-page-btn" ' +
                'aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Next',
                        'Next'
                    )
                ) +
                '">›</button>'
            );

            $pager
                .append($pagerPrev)
                .append($pagerText)
                .append($pagerNext);

            $iconBox.append($icon);

            $titleRow
                .append($iconBox)
                .append($title);

            $headLeft
                .append($titleRow)
                .append($sub);

            $head.append($headLeft);

            $body = $(
                '<div class="vas-upcoming-ap-runs-body">'
            );

            var $foot = $(
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

            $foot.append($pager);

            $card
                .append($head)
                .append($body)
                .append($foot)
                .append($busy)
                .append($state);

            $root.empty().append($card);

            createPayDialog();

            $pagerPrev.on('click', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                renderPage();
            });

            $pagerNext.on('click', function () {
                if (
                    totalPages <= 1 ||
                    pageNo >= totalPages
                ) {
                    return;
                }

                pageNo++;
                renderPage();
            });
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
                    'VAS_033_UpcomingAPRunsWidget/GetUpcomingAPRuns',

                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

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

        function renderData(data) {
            runsData = $.isArray(data.runs)
                ? $.grep(
                    data.runs,
                    function (run) {
                        var amount = Number(
                            pickRunValue(
                                run,
                                'totalAmount',
                                'amount'
                            ) || 0
                        );

                        var paymentCount = Number(
                            run.paymentCount || 0
                        );

                        return (
                            !isNaN(amount) &&
                            amount > 0
                        ) || (
                                !isNaN(paymentCount) &&
                                paymentCount > 0
                            );
                    }
                )
                : [];

            if (runsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;

            totalPages = Math.ceil(
                runsData.length / pageSize
            );

            renderPage();
        }

        function renderPage() {
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
                    runsData.length / pageSize
                )
            );

            if (pageNo < 1) {
                pageNo = 1;
            }

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            showState(false, '');

            $body.empty();

            var startIndex =
                (pageNo - 1) * pageSize;

            var pageItems = runsData.slice(
                startIndex,
                startIndex + pageSize
            );

            for (
                var i = 0;
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

        function updatePager() {
            if (!$pager) {
                return;
            }

            if ($pagerText) {
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
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    'disabled',
                    pageNo <= 1 ||
                    totalPages <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    'disabled',
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }
        }

        function createRunRow(run) {
            var paymentMethodName =
                run.paymentMethodName ||
                lbl(
                    'VAS_031_MessageNotSpecified',
                    'Not Specified'
                );

            var paymentCount = Number(
                run.paymentCount || 0
            );

            var amount = Number(
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

            var titleText = getRunTitle(
                paymentMethodName,
                run.vendorName,
                paymentCount
            );

            var metaParts = [];

            var barClass =
                getPaymentMethodClass(
                    paymentMethodName
                );

            if (dueDateText) {
                metaParts.push(
                    dueDateText
                );
            }

            metaParts.push(
                paymentCount.toLocaleString(
                    window.navigator.language
                ) +
                ' ' +
                getPaymentLabel(
                    paymentCount
                )
            );

            var $row = $(
                '<div class="vas-upcoming-ap-runs-row">'
            );

            var $bar = $(
                '<span class="vas-upcoming-ap-runs-bar">'
            ).addClass(barClass);

            var $info = $(
                '<div class="vas-upcoming-ap-runs-info">'
            );

            var $title = $(
                '<div class="vas-upcoming-ap-runs-run-title">'
            ).text(titleText);

            var $meta = $(
                '<div class="vas-upcoming-ap-runs-meta">'
            ).text(
                metaParts.join(' · ')
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

            var $actions = $(
                '<div class="vas-upcoming-ap-runs-actions">'
            );

            var $payBtn = $(
                '<button type="button" ' +
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

            $info
                .append($title)
                .append($meta);

            $actions
                .append($amount)
                .append($payBtn);

            $row
                .append($bar)
                .append($info)
                .append($actions);

            $payBtn.on(
                'click',
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    openPayDialog(run);
                }
            );

            return $row;
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

            if ($payDialogTitle) {
                $payDialogTitle.text(
                    lbl(
                        'VAS_031_MessageCreatePayment',
                        'Create Payment'
                    )
                );
            }

            if ($payDialogSub) {
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
            }

            if ($payDialogNotice) {
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
            }

            if ($payDialogGrid) {
                $payDialogGrid.empty();
            }

            setPayDialogBusy(
                true,
                false
            );

            $payDialog.show();

            $('body').addClass(
                'vas-upcoming-ap-runs-body-lock'
            );

            ensurePopupLookups(
                function () {
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

            $('body').removeClass(
                'vas-upcoming-ap-runs-body-lock'
            );

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
        }

        function ensurePopupLookups(callback) {
            if (popupLookups) {
                callback();
                return;
            }

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_033_UpcomingAPRunsWidget/GetPaymentPopupLookups',

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
                        popupLookups = {};

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

                        callback();
                        return;
                    }

                    popupLookups = data;

                    callback();
                },

                error: function (xhr) {
                    popupLookups = {};

                    showPayError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load lookup data'
                            )
                        )
                    );

                    callback();
                }
            });
        }

        function loadRunPaymentDetails(run) {
            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_033_UpcomingAPRunsWidget/GetUpcomingAPRunDetails',

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

        function normalizeResponse(response) {
            var data = response;

            for (
                var i = 0;
                i < 2;
                i++
            ) {
                if (
                    typeof data !== 'string'
                ) {
                    break;
                }

                try {
                    data = JSON.parse(data);
                }
                catch (error) {
                    data = null;
                    break;
                }
            }

            return data;
        }

        function getAjaxErrorMessage(
            xhr,
            fallback
        ) {
            if (!xhr) {
                return fallback;
            }

            var response =
                normalizeResponse(
                    xhr.responseText
                );

            if (response) {
                return (
                    response.error ||
                    response.errorText ||
                    response.message ||
                    fallback
                );
            }

            return fallback;
        }

        function renderPayDialogGrid(row) {
            if (
                !$payDialogGrid ||
                !row
            ) {
                return;
            }

            var bankText =
                getBankAccountDisplay(
                    row
                );

            var currencyText =
                (row.currencyISO || '') +
                (
                    row.currencyISO &&
                        row.currencySymbol
                        ? ' · '
                        : ''
                ) +
                (row.currencySymbol || '');

            var documentTypes =
                getLookupAny([
                    'documentTypes',
                    'docTypes'
                ]);

            var tenderTypes =
                getLookupAny([
                    'tenderTypes',
                    'paymentMethods'
                ]);

            var selectedDocTypeId =
                firstPositiveValue(
                    row.docTypeId,
                    row.cDocTypeId,

                    selectedRun &&
                    selectedRun.docTypeId,

                    selectedRun &&
                    selectedRun.cDocTypeId,

                    getFirstPositiveLookupValue(
                        documentTypes
                    )
                );

            var selectedTenderType =
                firstValue(
                    row.tenderType,
                    row.paymentMethodValue,

                    selectedRun &&
                    selectedRun.tenderType,

                    selectedRun &&
                    selectedRun.paymentMethodValue,

                    getFirstLookupValue(
                        tenderTypes
                    )
                );

            var selectedOrganizationId =
                firstPositiveValue(
                    row.organizationId,
                    row.adOrgId,

                    selectedRun &&
                    selectedRun.organizationId,

                    selectedRun &&
                    selectedRun.adOrgId
                );

            var selectedBankAccountId =
                firstPositiveValue(
                    row.bankAccountId,

                    selectedRun &&
                    selectedRun.bankAccountId
                );

            var selectedVendorId =
                firstPositiveValue(
                    row.vendorId,
                    row.cBPartnerId,

                    selectedRun &&
                    selectedRun.vendorId
                );

            var selectedCurrencyId =
                firstPositiveValue(
                    row.cCurrencyId,
                    row.currencyId,

                    selectedRun &&
                    selectedRun.cCurrencyId,

                    selectedRun &&
                    selectedRun.currencyId
                );

            var selectedConversionTypeId =
                firstPositiveValue(
                    row.conversionTypeId,
                    row.cConversionTypeId,

                    selectedRun &&
                    selectedRun.conversionTypeId
                );

            var transactionDate =
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

            var paymentAmount =
                firstValue(
                    row.amount,
                    row.payAmt,

                    selectedRun &&
                    selectedRun.amount,

                    selectedRun &&
                    selectedRun.totalAmount,

                    0
                );

            var paymentDocumentNo =
                firstValue(
                    row.paymentDocumentNo,
                    row.newPaymentDocumentNo,
                    row.documentNo,
                    ''
                );

            var html =
                fieldHtml(
                    lbl(
                        'VAS_031_MessageOrganization',
                        'Organization'
                    ),

                    selectHtml(
                        'adOrgId',
                        getLookup(
                            'organizations'
                        ),
                        selectedOrganizationId,
                        null,
                        null,
                        row.organizationName ||
                        ''
                    ),

                    false,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageBankAccount',
                        'Bank Account'
                    ),

                    selectHtml(
                        'bankAccountId',
                        getLookup(
                            'bankAccounts'
                        ),
                        selectedBankAccountId,
                        null,
                        null,
                        bankText
                    ),

                    true,
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

                    false,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_032_MessageVendor',
                        'Vendor'
                    ),

                    selectHtml(
                        'vendorId',
                        getLookup(
                            'vendors'
                        ),
                        selectedVendorId,
                        null,
                        null,
                        row.vendorName ||
                        ''
                    ),

                    true,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_PaymentCurrency',
                        'Currency'
                    ),

                    selectHtml(
                        'currencyId',
                        getLookup(
                            'currencies'
                        ),
                        selectedCurrencyId,
                        null,
                        null,
                        currencyText
                    ),

                    true,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageCurrencyType',
                        'Currency Type'
                    ),

                    selectHtml(
                        'conversionTypeId',
                        getLookup(
                            'conversionTypes'
                        ),
                        selectedConversionTypeId,
                        null,
                        null,

                        row.currencyTypeName ||
                        lbl(
                            'VAS_031_MessageSpot',
                            'Spot'
                        )
                    ),

                    false,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageDocumentType',
                        'Document Type'
                    ),

                    selectHtml(
                        'docTypeId',
                        documentTypes,
                        selectedDocTypeId,
                        null,
                        null,
                        row.docTypeName ||
                        ''
                    ),

                    false,
                    true
                ) +

                fieldHtml(
                    lbl(
                        'VAS_031_MessageTenderType',
                        'Tender Type'
                    ),

                    selectHtml(
                        'tenderType',
                        tenderTypes,
                        selectedTenderType,
                        null,
                        null,

                        row.tenderTypeName ||
                        row.paymentMethodName ||
                        (
                            selectedRun &&
                            selectedRun.paymentMethodName
                        ) ||
                        ''
                    ),

                    false,
                    true
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

                    true,
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

                    false,
                    true
                );

            $payDialogGrid.html(html);
        }

        function fieldHtml(
            label,
            value,
            prefilled,
            rawValue
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
                    rawValue
                        ? value || ''
                        : escapeHtml(
                            value || ''
                        )
                ) +

                '</div>' +

                '</div>'
            );
        }

        function getLookup(name) {
            return (
                popupLookups &&
                $.isArray(
                    popupLookups[name]
                )
            )
                ? popupLookups[name]
                : [];
        }

        function getLookupAny(names) {
            names = $.isArray(names)
                ? names
                : [];

            for (
                var i = 0;
                i < names.length;
                i++
            ) {
                var items =
                    getLookup(
                        names[i]
                    );

                if (items.length > 0) {
                    return items;
                }
            }

            return [];
        }

        function firstValue() {
            for (
                var i = 0;
                i < arguments.length;
                i++
            ) {
                var value =
                    arguments[i];

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
            for (
                var i = 0;
                i < arguments.length;
                i++
            ) {
                var value =
                    arguments[i];

                if (
                    value === undefined ||
                    value === null ||
                    String(value).trim() === ''
                ) {
                    continue;
                }

                var numericValue =
                    Number(value);

                if (
                    !isNaN(numericValue) &&
                    numericValue > 0
                ) {
                    return value;
                }
            }

            return '';
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
                item.label,
                item.text,
                item.description,
                item.documentNo,
                item.code,
                value
            );
        }

        function getFirstLookupValue(items) {
            items = $.isArray(items)
                ? items
                : [];

            for (
                var i = 0;
                i < items.length;
                i++
            ) {
                var value =
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
            items = $.isArray(items)
                ? items
                : [];

            for (
                var i = 0;
                i < items.length;
                i++
            ) {
                var value =
                    getLookupItemValue(
                        items[i]
                    );

                var numericValue =
                    Number(value);

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
            textProp,
            valueProp,
            fallbackText
        ) {
            items = $.isArray(items)
                ? items
                : [];

            if (
                selectedValue !== undefined &&
                selectedValue !== null &&
                String(
                    selectedValue
                ).trim() === '0'
            ) {
                selectedValue = '';
            }

            var html =
                '<select ' +
                'class="vas-upcoming-ap-runs-edit-control" ' +
                'data-pay-field="' +
                escapeHtml(fieldName) +
                '">';

            var hasSelected = false;

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
                var i = 0;
                i < items.length;
                i++
            ) {
                var item =
                    items[i] || {};

                var value =
                    getLookupItemValue(
                        item,
                        valueProp
                    );

                var text =
                    getLookupItemText(
                        item,
                        textProp,
                        value
                    );

                if (
                    value === undefined ||
                    value === null ||
                    String(value).trim() === ''
                ) {
                    continue;
                }

                var selected =
                    String(value) ===
                    String(selectedValue);

                if (selected) {
                    hasSelected = true;
                }

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
                        text ||
                        value ||
                        ''
                    ) +

                    '</option>';
            }

            if (
                !hasSelected &&
                selectedValue !== undefined &&
                selectedValue !== null &&
                String(
                    selectedValue
                ).trim() !== '' &&
                String(
                    selectedValue
                ).trim() !== '0'
            ) {
                html +=
                    '<option value="' +
                    escapeHtml(
                        selectedValue
                    ) +
                    '" selected>' +

                    escapeHtml(
                        fallbackText ||
                        selectedValue
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
            step,
            readonly
        ) {
            return (
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
                '"' +

                (
                    step
                        ? ' step="' +
                        escapeHtml(step) +
                        '"'
                        : ''
                ) +

                (
                    readonly
                        ? ' readonly'
                        : ''
                ) +

                '>'
            );
        }

        function updatePayNotice(row) {
            if (
                !$payDialogNotice ||
                !row
            ) {
                return;
            }

            var amountText =
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

            var sourceDocumentNo =
                firstValue(
                    row.documentNo,
                    row.invoiceDocumentNo,
                    row.invoiceNo,
                    ''
                );

            var vendorName =
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

        function getBankAccountDisplay(row) {
            row = row || {};

            var bankName =
                row.bankName || '';

            var accountName =
                row.bankAccountName || '';

            var accountNo =
                row.bankAccountNo || '';

            var tail = accountNo
                ? String(accountNo).slice(-4)
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

        function savePayDialog() {
            if (
                !selectedRun ||
                !selectedPaymentRow ||
                saveInProgress
            ) {
                return;
            }

            var payload =
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
                    'VAS_033_UpcomingAPRunsWidget/CreateUpcomingAPPayment',

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

        function readPayDialogPayload() {
            var $fields =
                $payDialogGrid
                    ? $payDialogGrid.find(
                        '[data-pay-field]'
                    )
                    : $();

            var payload = {};

            $fields.each(function () {
                var fieldName =
                    $(this).attr(
                        'data-pay-field'
                    );

                payload[fieldName] =
                    $(this).val();
            });

            payload.adOrgId = Number(
                firstPositiveValue(
                    payload.adOrgId,

                    selectedPaymentRow.organizationId,
                    selectedPaymentRow.adOrgId,

                    selectedRun &&
                    selectedRun.organizationId,

                    selectedRun &&
                    selectedRun.adOrgId,

                    0
                )
            );

            payload.bankAccountId = Number(
                firstPositiveValue(
                    payload.bankAccountId,

                    selectedPaymentRow.bankAccountId,

                    selectedRun &&
                    selectedRun.bankAccountId,

                    0
                )
            );

            payload.vendorId = Number(
                firstPositiveValue(
                    payload.vendorId,

                    selectedPaymentRow.vendorId,
                    selectedPaymentRow.cBPartnerId,

                    selectedRun &&
                    selectedRun.vendorId,

                    0
                )
            );

            payload.currencyId = Number(
                firstPositiveValue(
                    payload.currencyId,

                    selectedPaymentRow.cCurrencyId,
                    selectedPaymentRow.currencyId,

                    selectedRun &&
                    selectedRun.cCurrencyId,

                    selectedRun &&
                    selectedRun.currencyId,

                    0
                )
            );

            payload.conversionTypeId = Number(
                firstPositiveValue(
                    payload.conversionTypeId,

                    selectedPaymentRow.conversionTypeId,
                    selectedPaymentRow.cConversionTypeId,

                    selectedRun &&
                    selectedRun.conversionTypeId,

                    0
                )
            );

            payload.docTypeId = Number(
                firstPositiveValue(
                    payload.docTypeId,

                    selectedPaymentRow.docTypeId,
                    selectedPaymentRow.cDocTypeId,

                    selectedRun &&
                    selectedRun.docTypeId,

                    selectedRun &&
                    selectedRun.cDocTypeId,

                    getFirstPositiveLookupValue(
                        getLookupAny([
                            'documentTypes',
                            'docTypes'
                        ])
                    ),

                    0
                )
            );

            payload.tenderType = String(
                firstValue(
                    payload.tenderType,

                    selectedPaymentRow.tenderType,
                    selectedPaymentRow.paymentMethodValue,

                    selectedRun &&
                    selectedRun.tenderType,

                    selectedRun &&
                    selectedRun.paymentMethodValue,

                    ''
                )
            ).trim();

            payload.transactionDate =
                formatDateForInput(
                    firstValue(
                        payload.transactionDate,

                        selectedPaymentRow.transactionDate,
                        selectedPaymentRow.dateTrx,

                        selectedRun &&
                        selectedRun.transactionDate,

                        selectedRun &&
                        selectedRun.runDate,

                        selectedRun &&
                        selectedRun.dueDate,

                        ''
                    )
                );

            payload.documentNo = String(
                payload.documentNo || ''
            ).trim();

            payload.payAmt = Number(
                firstValue(
                    payload.payAmt,

                    selectedPaymentRow.amount,
                    selectedPaymentRow.payAmt,

                    selectedRun &&
                    selectedRun.totalAmount,

                    selectedRun &&
                    selectedRun.amount,

                    0
                )
            );

            if (payload.adOrgId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_MessageOrganizationRequired',
                        'Organization is required.'
                    )
                );

                return null;
            }

            if (
                payload.bankAccountId <= 0
            ) {
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

            if (
                payload.conversionTypeId <= 0
            ) {
                showPayError(
                    lbl(
                        'VAS_031_MessageConversionTypeRequired',
                        'Currency type is required.'
                    )
                );

                return null;
            }

            if (payload.docTypeId <= 0) {
                showPayError(
                    lbl(
                        'VAS_031_MessageDocumentTypeRequired',
                        'Document type is required.'
                    )
                );

                return null;
            }

            if (!payload.tenderType) {
                showPayError(
                    lbl(
                        'VAS_031_MessageTenderTypeRequired',
                        'Tender type is required.'
                    )
                );

                return null;
            }

            if (!payload.transactionDate) {
                showPayError(
                    lbl(
                        'VAS_031_MessageTransactionDateRequired',
                        'Transaction date is required.'
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
                        'VAS_031_MessagePaymentAmountRequired',
                        'Payment amount must be greater than zero.'
                    )
                );

                return null;
            }

            return payload;
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
                        'Could not save data'
                    )
                );
        }

        function setPayDialogBusy(
            show,
            isSaving
        ) {
            var busy = !!show;
            var saving =
                !!isSaving && busy;

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

            if (
                busy &&
                $payDialogNotice
            ) {
                $payDialogNotice.removeClass(
                    'vas-upcoming-ap-runs-pay-error'
                );
            }
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

            $payDialog
                .find(
                    '.vas-upcoming-ap-runs-pay-close, ' +
                    '.vas-upcoming-ap-runs-pay-cancel'
                )
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

        function getRunTitle(
            paymentMethodName,
            vendorName,
            paymentCount
        ) {
            if (
                paymentCount === 1 &&
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

        function getPaymentLabel(
            paymentCount
        ) {
            return paymentCount === 1
                ? lbl(
                    'VAS_031_MessagePayment',
                    'payment'
                )
                : lbl(
                    'VAS_031_MessagePayments',
                    'payments'
                );
        }

        function getPaymentMethodClass(
            paymentMethodName
        ) {
            var method = (
                paymentMethodName || ''
            ).toLowerCase();

            if (
                method.indexOf(
                    'rtgs'
                ) >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-rtgs'
                );
            }

            if (
                method.indexOf(
                    'upi'
                ) >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-upi'
                );
            }

            if (
                method.indexOf(
                    'card'
                ) >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-card'
                );
            }

            if (
                method.indexOf(
                    'cheque'
                ) >= 0 ||
                method.indexOf(
                    'check'
                ) >= 0
            ) {
                return (
                    'vas-upcoming-ap-runs-bar-cheque'
                );
            }

            return (
                'vas-upcoming-ap-runs-bar-neft'
            );
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date =
                parseDateValue(
                    value
                );

            if (!date) {
                return value;
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

            var date =
                parseDateValue(
                    value
                );

            if (!date) {
                return '';
            }

            var year =
                date.getFullYear();

            var month = String(
                date.getMonth() + 1
            ).padStart(
                2,
                '0'
            );

            var day = String(
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

        function parseDateValue(value) {
            if (!value) {
                return null;
            }

            if (
                typeof value === 'string' &&
                value.indexOf(
                    '/Date('
                ) === 0
            ) {
                var timestamp = Number(
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
                var parts =
                    value.split('-');

                var localDate =
                    new Date(
                        Number(parts[0]),
                        Number(parts[1]) - 1,
                        Number(parts[2])
                    );

                if (
                    !isNaN(
                        localDate.getTime()
                    )
                ) {
                    return localDate;
                }
            }

            var date =
                new Date(value);

            if (
                isNaN(
                    date.getTime()
                )
            ) {
                return null;
            }

            return date;
        }

        function formatCurrencyAmount(
            value,
            currencySymbol,
            currencyISO,
            stdPrecision
        ) {
            var numericValue =
                Number(value || 0);

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            var precision =
                Number(stdPrecision);

            if (
                isNaN(precision) &&
                VIS &&
                VIS.Env &&
                VIS.Env.getCtx &&
                VIS.Env.getCtx()
            ) {
                var context =
                    VIS.Env.getCtx();

                if (
                    context.getStdPrecision
                ) {
                    precision = Number(
                        context.getStdPrecision()
                    );
                }
            }

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                precision = 2;
            }

            return numericValue.toLocaleString(
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

            if (isNaN(numericValue)) {
                return '0';
            }

            return String(
                numericValue
            );
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
                    .text(
                        message || ''
                    )
                    .toggleClass(
                        'is-visible',
                        !!show
                    );
            }

            if ($body) {
                $body.toggle(
                    !show
                );
            }
        }

        function setNoData() {
            totalPages = 0;
            pageNo = 1;

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

            if ($payDialogSave) {
                $payDialogSave.off();
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

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.init =
        function (
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

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);