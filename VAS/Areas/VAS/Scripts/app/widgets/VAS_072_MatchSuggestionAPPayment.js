
/**
 * Auto Allocated AP Payment Widget
 * Purpose - Shows the percentage of automatically allocated AP payments
 *           and displays allocated and unallocated payment details.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                           | Message Key
 * ----+--------------------------------------------------------+----------------------------------------
 *  1  | Auto-allocated AP                                      | VAS_056_AutoAllocatedAPPayments
 *  2  | Last 30 days                                           | VAS_Last30Days
 *  3  | Payment Match to Invoice                               | VAS_056_PaymentMatchToInvoice
 *  4  | Payment allocation                                     | VAS_056_PaymentAllocation
 *  5  | Allocated vs unallocated payments in the last 30 days  | VAS_056_AllocatedVsUnallocated
 *  6  | Allocated                                              | VAS_Allocated
 *  7  | Unallocated                                            | VAS_Unallocated
 *  8  | Date                                                   | VAS_Date
 *  9  | Payment No.                                            | VAS_056_PaymentNo
 * 10  | Vendor                                                 | VAS_Vendor
 * 11  | Bank account                                           | VAS_BankAccount
 * 12  | Payment Currency                                       | VAS_PaymentCurrency
 * 13  | Amount                                                 | VAS_Amount
 * 14  | No payments in this period                             | VAS_056_NoPaymentsThisPeriod
 * 15  | payment                                                | VAS_056_Payment
 * 16  | payments                                               | VAS_056_Payments
 * 17  | Showing                                                | VAS_Showing
 * 18  | of                                                     | VAS_Of
 * 19  | Previous                                               | VAS_Previous
 * 20  | Next                                                   | VAS_Next
 * 21  | Close                                                  | VAS_Close
 * ──────────────────────────────────────────────────────────────────────────────
 */




; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_072_MatchSuggestionAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var classPrefix =
            "VAS-072-MatchSuggestionAPPayment-";

        var $root = $(
            '<div class="' +
            classPrefix +
            'root"></div>'
        );

        var $rows = null;
        var $busy = null;
        var $state = null;
        var $stateText = null;
        var $applyHigh = null;
        var $footerSummary = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var totalReadyAmount = 0;
        var highConfidenceCount = 0;
        var allocationWindowId = 0;

        var currentRows = [];
        var isLoading = false;
        var isDisposed = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== "[" + key + "]"
                ? text
                : fallback;
        }

        function escapeHtml(value) {
            return String(
                value == null
                    ? ""
                    : value
            )
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(response) {
            if (typeof response !== "string") {
                return response;
            }

            try {
                var data = JSON.parse(response);

                if (typeof data === "string") {
                    return JSON.parse(data);
                }

                return data;
            }
            catch (error) {
                console.error(
                    "VAS_072 response parsing error:",
                    error,
                    response
                );

                return null;
            }
        }

        function extractAjaxError(
            xhr,
            errorThrown
        ) {
            if (
                xhr &&
                xhr.responseJSON
            ) {
                if (xhr.responseJSON.error) {
                    return xhr.responseJSON.error;
                }

                if (xhr.responseJSON.errorText) {
                    return xhr.responseJSON.errorText;
                }

                if (xhr.responseJSON.message) {
                    return xhr.responseJSON.message;
                }
            }

            if (
                xhr &&
                xhr.responseText
            ) {
                var responseData =
                    parseResponse(
                        xhr.responseText
                    );

                if (responseData) {
                    if (responseData.error) {
                        return responseData.error;
                    }

                    if (responseData.errorText) {
                        return responseData.errorText;
                    }

                    if (responseData.message) {
                        return responseData.message;
                    }
                }

                return xhr.responseText;
            }

            if (errorThrown) {
                return errorThrown;
            }

            return lbl(
                "VAS_072_LoadError",
                "Could not load match suggestions"
            );
        }

        function getServerMessage(
            data,
            fallback
        ) {
            if (!data) {
                return fallback;
            }

            if (data.error) {
                return data.error;
            }

            if (data.errorText) {
                return data.errorText;
            }

            if (data.message) {
                return data.message;
            }

            return fallback;
        }

        function setBusy(show) {
            isLoading = Boolean(show);

            if ($busy) {
                $busy.toggleClass(
                    "is-visible",
                    isLoading
                );
            }

            if ($applyHigh) {
                $applyHigh.prop(
                    "disabled",
                    isLoading ||
                    highConfidenceCount <= 0
                );
            }

            updatePager();
        }

        function showState(message) {
            if (
                !$state ||
                !$stateText
            ) {
                return;
            }

            $stateText.text(
                message || ""
            );

            $state.addClass(
                "is-visible"
            );
        }

        function hideState() {
            if ($state) {
                $state.removeClass(
                    "is-visible"
                );
            }
        }

        function getStdPrecision() {
            var precision = 2;

            try {
                if (
                    VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env
                        .getCtx()
                        .getStdPrecision
                ) {
                    precision = Number(
                        VIS.Env
                            .getCtx()
                            .getStdPrecision()
                    );
                }
            }
            catch (error) {
                precision = 2;
            }

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                return 2;
            }

            return precision;
        }

        function formatAmount(value) {
            var amount = Number(
                value || 0
            );

            if (
                VIS.Utility &&
                VIS.Utility.Util &&
                VIS.Utility.Util
                    .getValueOfDecimal
            ) {
                amount =
                    VIS.Utility.Util
                        .getValueOfDecimal(
                            amount
                        );
            }

            return amount.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits:
                        getStdPrecision(),

                    maximumFractionDigits:
                        getStdPrecision()
                }
            );
        }

        function formatDate(value) {
            if (!value) {
                return "";
            }

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return String(value);
            }

            return date.toLocaleDateString(
                window.navigator.language,
                {
                    day: "2-digit",
                    month: "short",
                    year: "numeric"
                }
            );
        }

        function buildAmountText(
            row,
            value
        ) {
            var symbol =
                row.currencySymbol ||
                row.currencyISOCode ||
                "";

            var amount =
                formatAmount(value);

            return symbol
                ? symbol + " " + amount
                : amount;
        }

        function renderRows(rows) {
            currentRows = rows || [];

            $rows.empty();

            if (
                currentRows.length === 0
            ) {
                showState(
                    lbl(
                        "VAS_072_NoData",
                        "No match suggestions found"
                    )
                );

                return;
            }

            hideState();

            for (
                var i = 0;
                i < currentRows.length;
                i++
            ) {
                var row =
                    currentRows[i] || {};

                var isHigh =
                    String(
                        row.confidence || ""
                    ).toUpperCase() ===
                    "HIGH";

                var rowClass =
                    classPrefix +
                    "row" +
                    (
                        isHigh
                            ? ""
                            : " " +
                            classPrefix +
                            "row-review"
                    );

                var confidenceClass =
                    classPrefix +
                    "confidence " +
                    (
                        isHigh
                            ? classPrefix +
                            "confidence-high"
                            : classPrefix +
                            "confidence-review"
                    );

                var confidenceText =
                    isHigh
                        ? lbl(
                            "VAS_072_High",
                            "High"
                        )
                        : lbl(
                            "VAS_072_Review",
                            "Review"
                        );

                var paymentAmount =
                    buildAmountText(
                        row,
                        row.paymentAmount
                    );

                var openAmount =
                    buildAmountText(
                        row,
                        row.invoiceOpenAmount
                    );

                var dueDate =
                    formatDate(
                        row.dueDate
                    );

                var metaText =
                    " · " +
                    lbl(
                        "VAS_072_Open",
                        "open"
                    ) +
                    " " +
                    openAmount;

                if (dueDate) {
                    metaText +=
                        " · " +
                        lbl(
                            "VAS_072_Due",
                            "due"
                        ) +
                        " " +
                        dueDate;
                }

                var $row = $(
                    '<div class="' +
                    rowClass +
                    '" tabindex="0" role="button">' +

                    '<span class="' +
                    classPrefix +
                    'rail"></span>' +

                    '<div class="' +
                    classPrefix +
                    'info">' +

                    '<div class="' +
                    classPrefix +
                    'row-title">' +

                    escapeHtml(
                        row.vendorName || ""
                    ) +

                    '<span class="' +
                    classPrefix +
                    'amount"> · ' +

                    escapeHtml(
                        paymentAmount
                    ) +

                    "</span>" +
                    "</div>" +

                    '<div class="' +
                    classPrefix +
                    'link">' +

                    '<span class="' +
                    classPrefix +
                    'mono">' +

                    escapeHtml(
                        row.paymentDocumentNo ||
                        ""
                    ) +

                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    'arrow">→</span>' +

                    '<span class="' +
                    classPrefix +
                    "mono " +
                    classPrefix +
                    'invoice">' +

                    escapeHtml(
                        row.invoiceDocumentNo ||
                        ""
                    ) +

                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    'meta">' +

                    escapeHtml(metaText) +

                    "</span>" +
                    "</div>" +
                    "</div>" +

                    '<div class="' +
                    classPrefix +
                    'actions">' +

                    '<span class="' +
                    confidenceClass +
                    '">' +

                    escapeHtml(
                        confidenceText
                    ) +

                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    'chevron">' +

                    '<svg viewBox="0 0 24 24">' +
                    '<polyline points="9 18 15 12 9 6"></polyline>' +
                    "</svg>" +

                    "</span>" +
                    "</div>" +
                    "</div>"
                );

                $row.data(
                    "match-row",
                    row
                );

                $row.on(
                    "click",
                    function () {
                        openMatchDetail(
                            $(this).data(
                                "match-row"
                            )
                        );
                    }
                );

                $row.on(
                    "keydown",
                    function (event) {
                        if (
                            event.key ===
                            "Enter" ||
                            event.key === " "
                        ) {
                            event.preventDefault();

                            openMatchDetail(
                                $(this).data(
                                    "match-row"
                                )
                            );
                        }
                    }
                );

                $rows.append($row);
            }
        }

        function renderResult(data) {
            var rows =
                data &&
                    Array.isArray(data.rows)
                    ? data.rows
                    : [];

            pageNo = Number(
                data.pageNo || 1
            );

            totalPages = Number(
                data.totalPages || 0
            );

            totalRecords = Number(
                data.totalRecords || 0
            );

            totalReadyAmount = Number(
                data.totalReadyAmount || 0
            );

            highConfidenceCount = Number(
                data.highConfidenceCount || 0
            );

            allocationWindowId = Number(
                data.allocationWindowId || 0
            );

            renderRows(rows);
            updateFooter(data);
            updatePager();

            if ($applyHigh) {
                $applyHigh.prop(
                    "disabled",
                    isLoading ||
                    highConfidenceCount <= 0
                );
            }
        }

        function updateFooter(data) {
            if (!$footerSummary) {
                return;
            }

            var displayAmount = "";

            if (data) {
                var symbol =
                    data.currencySymbol ||
                    data.currencyISOCode ||
                    "";

                displayAmount =
                    (
                        symbol
                            ? symbol + " "
                            : ""
                    ) +
                    formatAmount(
                        totalReadyAmount
                    );
            }

            $footerSummary.html(
                "<strong>" +
                escapeHtml(totalRecords) +
                "</strong> " +

                escapeHtml(
                    lbl(
                        "VAS_072_Suggestions",
                        "suggestions"
                    )
                ) +

                " · <strong>" +
                escapeHtml(displayAmount) +
                "</strong> " +

                escapeHtml(
                    lbl(
                        "VAS_072_ReadyToAllocate",
                        "ready to allocate"
                    )
                )
            );
        }

        function updatePager() {
            if ($pagerText) {
                $pagerText.text(
                    totalPages > 0
                        ? (
                            pageNo +
                            " " +
                            lbl(
                                "VAS_Of",
                                "of"
                            ) +
                            " " +
                            totalPages
                        )
                        : ""
                );
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    "disabled",
                    isLoading ||
                    pageNo <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    "disabled",
                    isLoading ||
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }
        }

        function loadData() {
            if (
                isLoading ||
                isDisposed
            ) {
                return;
            }

            hideState();
            setBusy(true);

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_072_MatchSuggestionAPPaymentWidget/GetMatchSuggestions",

                type: "GET",
                cache: false,

                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    console.log(
                        "VAS_072 Match Suggestions response:",
                        data
                    );

                    if (
                        !data ||
                        data.error ||
                        data.success === false
                    ) {
                        renderResult({});

                        var message =
                            getServerMessage(
                                data,
                                lbl(
                                    "VAS_072_LoadError",
                                    "Could not load match suggestions"
                                )
                            );

                        console.error(
                            "VAS_072 server error:",
                            message,
                            data
                        );

                        showState(message);

                        return;
                    }

                    renderResult(data);
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (isDisposed) {
                        return;
                    }

                    renderResult({});

                    var message =
                        extractAjaxError(
                            xhr,
                            errorThrown
                        );

                    console.error(
                        "VAS_072 AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    showState(message);
                },

                complete: function () {
                    if (!isDisposed) {
                        setBusy(false);
                    }
                }
            });
        }

        function openMatchDetail(row) {
            if (!row) {
                return;
            }

            applySingleSuggestion(row);
        }

        function applySingleSuggestion(row) {
            if (
                !row.paymentId ||
                !row.invoiceId ||
                !row.payScheduleId
            ) {
                return;
            }

            setBusy(true);

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_072_MatchSuggestionAPPaymentWidget/ApplyAllocation",

                type: "POST",
                cache: false,

                data: {
                    paymentId:
                        row.paymentId,

                    invoiceId:
                        row.invoiceId,

                    payScheduleId:
                        row.payScheduleId
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    console.log(
                        "VAS_072 ApplyAllocation response:",
                        data
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        var message =
                            getServerMessage(
                                data,
                                lbl(
                                    "VAS_072_ApplyError",
                                    "Could not complete allocation"
                                )
                            );

                        console.error(
                            "VAS_072 allocation error:",
                            message,
                            data
                        );

                        VIS.ADialog.error(
                            message
                        );

                        return;
                    }

                    VIS.ADialog.info(
                        data.message ||
                        lbl(
                            "VAS_072_ApplySuccess",
                            "Allocation completed successfully"
                        )
                    );

                    loadCurrentPageAfterApply();
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (isDisposed) {
                        return;
                    }

                    var message =
                        extractAjaxError(
                            xhr,
                            errorThrown
                        );

                    console.error(
                        "VAS_072 ApplyAllocation AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    VIS.ADialog.error(
                        message
                    );
                },

                complete: function () {
                    if (!isDisposed) {
                        setBusy(false);
                    }
                }
            });
        }

        function applyHighConfidence() {
            if (
                isLoading ||
                highConfidenceCount <= 0
            ) {
                return;
            }

            setBusy(true);

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_072_MatchSuggestionAPPaymentWidget/ApplyHighConfidence",

                type: "POST",
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    console.log(
                        "VAS_072 ApplyHighConfidence response:",
                        data
                    );

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        var message =
                            getServerMessage(
                                data,
                                lbl(
                                    "VAS_072_ApplyError",
                                    "Could not complete allocation"
                                )
                            );

                        console.error(
                            "VAS_072 high-confidence error:",
                            message,
                            data
                        );

                        VIS.ADialog.error(
                            message
                        );

                        return;
                    }

                    VIS.ADialog.info(
                        data.message ||
                        lbl(
                            "VAS_072_ApplySuccess",
                            "Allocation completed successfully"
                        )
                    );

                    pageNo = 1;

                    loadCurrentPageAfterApply();
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (isDisposed) {
                        return;
                    }

                    var message =
                        extractAjaxError(
                            xhr,
                            errorThrown
                        );

                    console.error(
                        "VAS_072 ApplyHighConfidence AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    VIS.ADialog.error(
                        message
                    );
                },

                complete: function () {
                    if (!isDisposed) {
                        setBusy(false);
                    }
                }
            });
        }

        function loadCurrentPageAfterApply() {
            if (
                pageNo > 1 &&
                currentRows.length <= 1
            ) {
                pageNo--;
            }

            window.setTimeout(
                function () {
                    loadData();
                },
                100
            );
        }

        function openAllocationForm() {
            if (
                allocationWindowId <= 0
            ) {
                return;
            }

            try {
                var zoomQuery =
                    new VIS.Query();

                VIS.viewManager.startWindow(
                    allocationWindowId,
                    zoomQuery
                );
            }
            catch (error) {
                console.error(
                    "VAS_072 open allocation form error:",
                    error
                );

                VIS.ADialog.error(
                    lbl(
                        "VAS_072_ApplyError",
                        "Could not open allocation form"
                    )
                );
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="' +
                classPrefix +
                'card">' +

                '<div class="' +
                classPrefix +
                'header">' +

                '<div class="' +
                classPrefix +
                'header-left">' +

                '<div class="' +
                classPrefix +
                'icon-box">' +

                '<svg class="' +
                classPrefix +
                'icon" viewBox="0 0 24 24">' +

                '<path d="M9 11l3 3L22 4"></path>' +

                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path>' +

                "</svg>" +
                "</div>" +

                '<div class="' +
                classPrefix +
                'head-text">' +

                '<div class="' +
                classPrefix +
                'title">' +

                escapeHtml(
                    lbl(
                        "VAS_072_MatchSuggestions",
                        "Match Suggestions"
                    )
                ) +

                "</div>" +

                '<div class="' +
                classPrefix +
                'subtitle">' +

                escapeHtml(
                    lbl(
                        "VAS_072_Subtitle",
                        "Vendor payments paired with their best-fit purchase invoice"
                    )
                ) +

                "</div>" +
                "</div>" +
                "</div>" +

                '<button type="button" class="' +
                classPrefix +
                'apply-high">' +

                '<svg viewBox="0 0 24 24">' +
                '<polyline points="20 6 9 17 4 12"></polyline>' +
                "</svg>" +

                "<span>" +

                escapeHtml(
                    lbl(
                        "VAS_072_ApplyHighConfidence",
                        "Apply high-confidence"
                    )
                ) +

                "</span>" +
                "</button>" +
                "</div>" +

                '<div class="' +
                classPrefix +
                'rows"></div>' +

                '<div class="' +
                classPrefix +
                'footer">' +

                '<span class="' +
                classPrefix +
                'footer-summary"></span>' +

                '<div class="' +
                classPrefix +
                'footer-right">' +

                '<div class="' +
                classPrefix +
                'pager">' +

                '<button type="button" class="' +
                classPrefix +
                "pager-btn " +
                classPrefix +
                'pager-prev" aria-label="' +

                escapeHtml(
                    lbl(
                        "VAS_Previous",
                        "Previous"
                    )
                ) +

                '">' +

                '<svg viewBox="0 0 24 24">' +
                '<polyline points="15 18 9 12 15 6"></polyline>' +
                "</svg>" +

                "</button>" +

                '<span class="' +
                classPrefix +
                'pager-text"></span>' +

                '<button type="button" class="' +
                classPrefix +
                "pager-btn " +
                classPrefix +
                'pager-next" aria-label="' +

                escapeHtml(
                    lbl(
                        "VAS_Next",
                        "Next"
                    )
                ) +

                '">' +

                '<svg viewBox="0 0 24 24">' +
                '<polyline points="9 18 15 12 9 6"></polyline>' +
                "</svg>" +

                "</button>" +
                "</div>" +

                '<button type="button" class="' +
                classPrefix +
                'open-form">' +

                escapeHtml(
                    lbl(
                        "VAS_072_OpenAllocationForm",
                        "Open allocation form"
                    )
                ) +

                " →</button>" +
                "</div>" +
                "</div>" +
                "</div>"
            );

            $rows = $card.find(
                "." +
                classPrefix +
                "rows"
            );

            $applyHigh = $card.find(
                "." +
                classPrefix +
                "apply-high"
            );

            $footerSummary =
                $card.find(
                    "." +
                    classPrefix +
                    "footer-summary"
                );

            $pagerPrev = $card.find(
                "." +
                classPrefix +
                "pager-prev"
            );

            $pagerNext = $card.find(
                "." +
                classPrefix +
                "pager-next"
            );

            $pagerText = $card.find(
                "." +
                classPrefix +
                "pager-text"
            );

            $applyHigh.on(
                "click",
                applyHighConfidence
            );

            $pagerPrev.on(
                "click",
                function () {
                    if (
                        isLoading ||
                        pageNo <= 1
                    ) {
                        return;
                    }

                    pageNo--;
                    loadData();
                }
            );

            $pagerNext.on(
                "click",
                function () {
                    if (
                        isLoading ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;
                    loadData();
                }
            );

            $card.find(
                "." +
                classPrefix +
                "open-form"
            ).on(
                "click",
                openAllocationForm
            );

            $root.append($card);

            $busy = $(
                '<div class="' +
                classPrefix +
                'busy">' +

                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                "</div>" +
                "</div>"
            );

            $state = $(
                '<div class="' +
                classPrefix +
                'state">' +

                '<span class="' +
                classPrefix +
                'state-text"></span>' +

                "</div>"
            );

            $stateText = $state.find(
                "." +
                classPrefix +
                "state-text"
            );

            $root.append($busy);
            $root.append($state);
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshData = function () {
            pageNo = 1;
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent =
            function () {
                isDisposed = true;

                if ($root) {
                    $root
                        .find("*")
                        .off();

                    $root.remove();
                }

                $rows = null;
                $busy = null;
                $state = null;
                $stateText = null;
                $applyHigh = null;
                $footerSummary = null;
                $pagerPrev = null;
                $pagerNext = null;
                $pagerText = null;
                currentRows = [];
            };
    };

    VAS.VAS_072_MatchSuggestionAPPaymentWidget.prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.windowNo = windowNo;
            this.frame = frame;

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

    VAS.VAS_072_MatchSuggestionAPPaymentWidget.prototype.widgetSizeChange =
        function () {
        };

    VAS.VAS_072_MatchSuggestionAPPaymentWidget.prototype.refreshWidget =
        function () {
            this.refreshData();
        };

    VAS.VAS_072_MatchSuggestionAPPaymentWidget.prototype.dispose =
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