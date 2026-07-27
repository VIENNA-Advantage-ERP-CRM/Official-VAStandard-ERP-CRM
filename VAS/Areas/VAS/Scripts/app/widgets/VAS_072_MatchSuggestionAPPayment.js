/**
 * AP Payment Match Suggestions Widget
 * Purpose - Shows the best purchase-invoice match for each available
 *           AP payment and allows individual or high-confidence allocation.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+---------------------------------------
 *  1  | Match Suggestions                                 | VAS_072_MatchSuggestions
 *  2  | Vendor payments paired with their best-fit...     | VAS_072_Subtitle
 *  3  | Apply high-confidence                             | VAS_072_ApplyHighConfidence
 *  4  | High                                              | VAS_072_High
 *  5  | Review                                            | VAS_072_Review
 *  6  | Open                                              | VAS_072_Open
 *  7  | Due                                               | VAS_072_Due
 *  8  | Suggestions                                       | VAS_072_Suggestions
 *  9  | Ready to allocate                                 | VAS_072_ReadyToAllocate
 * 10  | Open allocation form                              | VAS_072_OpenAllocationForm
 * 11  | No match suggestions found                        | VAS_072_NoData
 * 12  | Could not load match suggestions                  | VAS_072_LoadError
 * 13  | Could not complete allocation                     | VAS_072_ApplyError
 * 14  | Allocation completed successfully                 | VAS_072_ApplySuccess
 * 15  | Could not open allocation form                    | VAS_072_OpenFormError
 * 16  | Previous                                          | VAS_Previous
 * 17  | Next                                              | VAS_Next
 * 18  | Of                                                | VAS_Of
 * 19  | Match review                                      | VAS_072_MatchReview
 * 20  | Could not load match details                       | VAS_072_LoadDetailError
 * 21  | Strong match                                      | VAS_072_HighConfidenceMatch
 * 22  | Needs review                                      | VAS_072_NeedsReview
 * 23  | payment and invoice line up                       | VAS_072_PaymentAndInvoiceLineUp
 * 24  | Vendor, amount and timing all agree.              | VAS_072_MatchSignalsAgree
 * 25  | Confidence                                        | VAS_072_Confidence
 * 26  | Vendor payment                                    | VAS_072_VendorPayment
 * 27  | Payment date                                      | VAS_PaymentDate
 * 28  | Vendor                                            | VAS_Vendor
 * 29  | Payment method                                    | VAS_072_PaymentMethod
 * 30  | Reference                                         | VAS_072_Reference
 * 31  | Bank account                                      | VAS_072_BankAccount
 * 32  | Currency                                          | VAS_PaymentCurrency
 * 33  | Amount paid                                       | VAS_072_AmountPaid
 * 34  | Suggested invoice                                 | VAS_072_SuggestedInvoice
 * 35  | Invoice date                                      | VAS_072_InvoiceDate
 * 36  | Payment terms                                     | VAS_072_PaymentTerms
 * 37  | Due date                                          | VAS_072_DueDate
 * 38  | Grand total                                       | VAS_072_GrandTotal
 * 39  | Open amount                                       | VAS_072_OpenAmount
 * 40  | balance — fully settles the invoice               | VAS_072_FullySettles
 * 41  | still open after apply                            | VAS_072_StillOpen
 * 42  | Why this match                                    | VAS_072_WhyThisMatch
 * 43  | Vendor matches                                    | VAS_072_VendorMatches
 * 44  | Payment and invoice belong to the same vendor     | VAS_072_VendorMatchesDetail
 * 45  | Amount matches                                    | VAS_072_AmountMatches
 * 46  | Amount differs                                    | VAS_072_AmountDiffers
 * 47  | vs                                                | VAS_072_Vs
 * 48  | Reference cited                                   | VAS_072_ReferenceCited
 * 49  | No reference cited                                | VAS_072_NoReferenceCited
 * 50  | Within due window                                 | VAS_072_WithinDueWindow
 * 51  | Outside due window                                | VAS_072_OutsideDueWindow
 * 52  | Apply as part-payment                             | VAS_072_ApplyPartPayment
 * 53  | Apply allocation                                  | VAS_072_ApplyAllocation
 * 54  | Close                                             | VAS_Close
 * 55  | High-confidence — safe to apply                   | VAS_072_HighConfidenceSafe
 * 56  | Skip                                              | VAS_072_Skip
 * 57  | Document type                                     | VAS_072_DocumentType
 * 58  | Return                                            | VAS_072_ReturnCycle
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

        var controllerUrl =
            VIS.Application.contextUrl +
            "VAS_072_MatchSuggestionAPPaymentWidget/";

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
        var $showingText = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $openForm = null;
        var $reviewDialog = null;
        var $reviewBody = null;
        var $reviewBanner = null;
        var $reviewBusy = null;
        var $reviewTitle = null;
        var $reviewSub = null;
        var $reviewApply = null;

        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 0;
        var totalRecords = 0;
        var totalReadyAmount = 0;
        var highConfidenceCount = 0;

        var allocationFormId = 0;
        var serverStdPrecision = 2;

        var currentRows = [];
        var currentReviewRow = null;

        var isLoading = false;
        var isDisposed = false;

        var activeListRequest = null;
        var activeActionRequest = null;
        var activeDetailRequest = null;
        var resizeObserver = null;
        var widgetRowHeight = 54;
        var widgetMinimumRows = 2;

        function lbl(key, fallback) {
            var text = null;

            try {
                text = VIS.Msg.getMsg(key);
            }
            catch (error) {
                text = null;
            }

            return text &&
                text !== key &&
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

        function toNumber(value, fallback) {
            var numberValue = Number(value);

            return isNaN(numberValue)
                ? Number(fallback || 0)
                : numberValue;
        }

        function normalizePrecision(value) {
            var precision = Number(value);

            if (
                isNaN(precision) ||
                precision < 0 ||
                precision > 10
            ) {
                return 2;
            }

            return Math.floor(precision);
        }

        function parseResponse(response) {
            if (
                response == null ||
                response === ""
            ) {
                return null;
            }

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
                var jsonData =
                    parseResponse(
                        xhr.responseJSON
                    );

                if (jsonData) {
                    return getServerMessage(
                        jsonData,
                        errorThrown
                    );
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
                    return getServerMessage(
                        responseData,
                        xhr.responseText
                    );
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
            var key;

            if (!data) {
                return fallback || "";
            }

            key =
                data.errorKey ||
                data.messageKey;

            if (key) {
                return lbl(
                    key,
                    data.error ||
                    data.errorText ||
                    data.message ||
                    fallback ||
                    ""
                );
            }

            if (data.error) {
                return String(data.error);
            }

            if (data.errorText) {
                return String(data.errorText);
            }

            if (data.message) {
                return String(data.message);
            }

            return fallback || "";
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

            if ($openForm) {
                $openForm.prop(
                    "disabled",
                    isLoading ||
                    allocationFormId <= 0
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

        function getContextPrecision() {
            var precision = 2;

            try {
                if (
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
            catch (error) {
                precision = 2;
            }

            return normalizePrecision(
                precision
            );
        }

        function getAmountPrecision(row) {
            if (
                row &&
                row.stdPrecision != null
            ) {
                return normalizePrecision(
                    row.stdPrecision
                );
            }

            if (serverStdPrecision != null) {
                return normalizePrecision(
                    serverStdPrecision
                );
            }

            return getContextPrecision();
        }

        function formatAmount(
            value,
            precision
        ) {
            var amount =
                toNumber(
                    value,
                    0
                );

            var decimalPrecision =
                normalizePrecision(
                    precision
                );

            try {
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
            }
            catch (error) {
                amount =
                    toNumber(
                        value,
                        0
                    );
            }

            try {
                return amount.toLocaleString(
                    window.navigator.language,
                    {
                        minimumFractionDigits:
                            decimalPrecision,

                        maximumFractionDigits:
                            decimalPrecision
                    }
                );
            }
            catch (error) {
                return amount.toFixed(
                    decimalPrecision
                );
            }
        }

        function formatDate(value) {
            if (!value) {
                return "";
            }

            var stringValue =
                String(value);

            var dateOnlyMatch =
                /^(\d{4})-(\d{2})-(\d{2})$/
                    .exec(stringValue);

            var date;

            if (dateOnlyMatch) {
                date = new Date(
                    Number(dateOnlyMatch[1]),
                    Number(dateOnlyMatch[2]) - 1,
                    Number(dateOnlyMatch[3])
                );
            }
            else {
                date = new Date(value);
            }

            if (isNaN(date.getTime())) {
                return stringValue;
            }

            try {
                return date.toLocaleDateString(
                    window.navigator.language,
                    {
                        day: "2-digit",
                        month: "short",
                        year: "numeric"
                    }
                );
            }
            catch (error) {
                return stringValue;
            }
        }

        function buildAmountText(
            row,
            value
        ) {
            var symbol =
                row.currencySymbol ||
                row.currencyISOCode ||
                "";

            var numericValue =
                toNumber(value, 0);

            var sign =
                numericValue < 0
                    ? "-"
                    : "";

            var amount =
                formatAmount(
                    Math.abs(numericValue),
                    getAmountPrecision(row)
                );

            return symbol
                ? sign + symbol + amount
                : sign + amount;
        }

        function renderRows(rows) {
            currentRows =
                Array.isArray(rows)
                    ? rows
                    : [];

            if (!$rows) {
                return;
            }

            $rows.empty();

            if (currentRows.length === 0) {
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

                var confidence =
                    String(
                        row.confidence || ""
                    ).toUpperCase();

                var isHigh =
                    confidence === "HIGH";

                var isReturnCycle =
                    !!row.isReturnCycle;

                var rowClass =
                    classPrefix +
                    "row" +
                    (
                        isHigh
                            ? ""
                            : " " +
                            classPrefix +
                            "row-review"
                    ) +
                    (
                        isReturnCycle
                            ? " " +
                            classPrefix +
                            "row-return"
                            : ""
                    );

                var returnTagHtml =
                    isReturnCycle
                        ? '<span class="' +
                        classPrefix +
                        'return-tag">' +
                        escapeHtml(
                            lbl(
                                "VAS_072_ReturnCycle",
                                "Return"
                            )
                        ) +
                        "</span>"
                        : "";

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

                var invoiceOpenAmount =
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
                        "Open"
                    ) +
                    " " +
                    invoiceOpenAmount;

                if (dueDate) {
                    metaText +=
                        " · " +
                        lbl(
                            "VAS_072_Due",
                            "Due"
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

                    escapeHtml(
                        metaText
                    ) +

                    "</span>" +

                    "</div>" +
                    "</div>" +

                    '<div class="' +
                    classPrefix +
                    'actions">' +

                    returnTagHtml +

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
                        openReviewDialog(
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
                            event.key === "Enter" ||
                            event.key === " "
                        ) {
                            event.preventDefault();

                            openReviewDialog(
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
            data = data || {};

            var rows =
                Array.isArray(data.rows)
                    ? data.rows
                    : [];

            pageNo =
                Math.max(
                    1,
                    toNumber(
                        data.pageNo,
                        pageNo
                    )
                );

            totalPages =
                Math.max(
                    0,
                    toNumber(
                        data.totalPages,
                        0
                    )
                );

            totalRecords =
                Math.max(
                    0,
                    toNumber(
                        data.totalRecords,
                        0
                    )
                );

            totalReadyAmount =
                toNumber(
                    data.totalReadyAmount,
                    0
                );

            highConfidenceCount =
                Math.max(
                    0,
                    toNumber(
                        data.highConfidenceCount,
                        0
                    )
                );

            serverStdPrecision =
                normalizePrecision(
                    data.stdPrecision != null
                        ? data.stdPrecision
                        : serverStdPrecision
                );

            allocationFormId =
                Math.max(
                    0,
                    toNumber(
                        data.allocationFormId,
                        0
                    )
                );

            if ($openForm) {
                $openForm.toggle(
                    allocationFormId > 0
                );

                $openForm.prop(
                    "disabled",
                    isLoading ||
                    allocationFormId <= 0
                );
            }

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

            data = data || {};

            var symbol =
                data.currencySymbol ||
                data.currencyISOCode ||
                "";

            var displayAmount =
                (
                    Number(totalReadyAmount || 0) < 0
                        ? "-"
                        : ""
                ) +
                (
                    symbol
                        ? symbol
                        : ""
                ) +
                formatAmount(
                    Math.abs(
                        Number(totalReadyAmount || 0)
                    ),
                    serverStdPrecision
                );

            $footerSummary.html(
                "<strong>" +
                escapeHtml(
                    totalRecords
                ) +
                "</strong> " +

                escapeHtml(
                    lbl(
                        "VAS_072_Suggestions",
                        "Suggestions"
                    )
                ) +

                " · <strong>" +
                escapeHtml(
                    displayAmount
                ) +
                "</strong> " +

                escapeHtml(
                    lbl(
                        "VAS_072_ReadyToAllocate",
                        "Ready to allocate"
                    )
                )
            );
        }

        function updatePager() {
            if ($showingText) {
                var startIndex =
                    totalRecords > 0
                        ? ((pageNo - 1) * pageSize) + 1
                        : 0;

                var endIndex =
                    totalRecords > 0
                        ? Math.min(pageNo * pageSize, totalRecords)
                        : 0;

                $showingText.text(
                    totalRecords > 0
                        ? lbl("VAS_Showing", "Showing") + " " +
                          startIndex + "–" + endIndex + " " +
                          lbl("VAS_Of", "of") + " " + totalRecords
                        : ""
                );
            }

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

            activeListRequest = $.ajax({
                url:
                    controllerUrl +
                    "GetMatchSuggestions",

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
                        "VAS_072 GetMatchSuggestions response:",
                        data
                    );

                    if (
                        !data ||
                        data.success !== true ||
                        data.error
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
                            "VAS_072 GetMatchSuggestions error:",
                            message,
                            data
                        );

                        showState(message);

                        return;
                    }

                    renderResult(data);

                    if (
                        data.hasData === false ||
                        !Array.isArray(data.rows) ||
                        data.rows.length === 0
                    ) {
                        showState(
                            lbl(
                                "VAS_072_NoData",
                                "No match suggestions found"
                            )
                        );
                    }
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (
                        isDisposed ||
                        status === "abort"
                    ) {
                        return;
                    }

                    renderResult({});

                    var message =
                        extractAjaxError(
                            xhr,
                            errorThrown
                        );

                    console.error(
                        "VAS_072 GetMatchSuggestions AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    showState(message);
                },

                complete: function () {
                    activeListRequest = null;

                    if (!isDisposed) {
                        setBusy(false);
                    }
                }
            });
        }

        function showReviewBusy(show, showApplySpinner) {
            if (
                $reviewBusy &&
                $reviewBusy[0]
            ) {
                $reviewBusy.toggleClass(
                    "is-visible",
                    Boolean(show)
                );

                $reviewBusy[0].style.visibility =
                    show
                        ? "visible"
                        : "hidden";
            }

            if ($reviewApply) {
                $reviewApply
                    .prop(
                        "disabled",
                        Boolean(show) ||
                        !currentReviewRow
                    )
                    .toggleClass(
                        "is-loading",
                        Boolean(show) &&
                        Boolean(showApplySpinner)
                    )
                    .attr(
                        "aria-busy",
                        Boolean(show) &&
                        Boolean(showApplySpinner)
                            ? "true"
                            : "false"
                    );
            }
        }

        function openReviewDialog(row) {
            if (
                isDisposed ||
                isLoading ||
                !row ||
                !$reviewDialog
            ) {
                return;
            }

            currentReviewRow = row;

            if ($reviewTitle) {
                $reviewTitle.text(
                    lbl(
                        "VAS_072_MatchReview",
                        "Match review"
                    ) +
                    (
                        row.vendorName
                            ? " · " + row.vendorName
                            : ""
                    )
                );
            }

            if ($reviewSub) {
                $reviewSub.text(
                    (
                        row.paymentDocumentNo || ""
                    ) +
                    (
                        row.invoiceDocumentNo
                            ? " → " +
                              row.invoiceDocumentNo
                            : ""
                    )
                );
            }

            $reviewDialog.show();
            $("body").addClass(
                classPrefix +
                "body-lock"
            );

            if ($reviewBody) {
                $reviewBody.empty();
            }

            if ($reviewBanner) {
                $reviewBanner
                    .attr(
                        "class",
                        classPrefix +
                        "review-banner"
                    )
                    .empty();
            }

            showReviewBusy(true);

            abortRequest(activeDetailRequest);

            activeDetailRequest = $.ajax({
                url:
                    controllerUrl +
                    "GetMatchDetail",

                type: "GET",
                cache: false,

                data: {
                    paymentId:
                        toNumber(
                            row.paymentId,
                            0
                        ),

                    invoiceId:
                        toNumber(
                            row.invoiceId,
                            0
                        ),

                    payScheduleId:
                        toNumber(
                            row.payScheduleId,
                            0
                        )
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    if (
                        !data ||
                        data.success !== true ||
                        data.error ||
                        !data.detail
                    ) {
                        renderReviewEmpty(
                            getServerMessage(
                                data,
                                lbl(
                                    "VAS_072_LoadDetailError",
                                    "Could not load match details"
                                )
                            )
                        );

                        return;
                    }

                    renderReviewDetail(data.detail);
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (
                        isDisposed ||
                        status === "abort"
                    ) {
                        return;
                    }

                    renderReviewEmpty(
                        extractAjaxError(
                            xhr,
                            errorThrown
                        )
                    );
                },

                complete: function () {
                    activeDetailRequest = null;

                    if (!isDisposed) {
                        showReviewBusy(false);
                    }
                }
            });
        }

        function closeReviewDialog() {
            if (!$reviewDialog) {
                return;
            }

            abortRequest(activeDetailRequest);
            activeDetailRequest = null;

            $reviewDialog.hide();
            $("body").removeClass(
                classPrefix +
                "body-lock"
            );

            currentReviewRow = null;

            if ($reviewBody) {
                $reviewBody.empty();
            }
        }

        function renderReviewEmpty(message) {
            if (!$reviewBody) {
                return;
            }

            $reviewBody.html(
                '<div class="' +
                classPrefix +
                'review-empty">' +
                escapeHtml(
                    message ||
                    lbl(
                        "VAS_072_NoData",
                        "No match suggestions found"
                    )
                ) +
                "</div>"
            );
        }

        function formatDetailAmount(
            detail,
            value,
            side
        ) {
            var symbol =
                side === "invoice"
                    ? (
                        detail.invoiceCurrencySymbol ||
                        detail.invoiceCurrencyISOCode ||
                        ""
                    )
                    : (
                        detail.paymentCurrencySymbol ||
                        detail.paymentCurrencyISOCode ||
                        ""
                    );

            var precision =
                side === "invoice"
                    ? detail.invoicePrecision
                    : detail.paymentPrecision;

            var numericValue =
                toNumber(value, 0);

            var sign =
                numericValue < 0
                    ? "-"
                    : "";

            var amount =
                formatAmount(
                    Math.abs(numericValue),
                    normalizePrecision(precision)
                );

            return symbol
                ? sign + symbol + amount
                : sign + amount;
        }

        function formatBankAccount(detail) {
            var bankName =
                detail && detail.bankName
                    ? String(detail.bankName).trim()
                    : "";

            var accountNo =
                detail && detail.accountNo
                    ? String(detail.accountNo).trim()
                    : "";

            var last4 =
                accountNo
                    ? (
                        accountNo.length > 4
                            ? accountNo.slice(-4)
                            : accountNo
                    )
                    : "";

            if (
                bankName &&
                last4
            ) {
                return bankName + " ****" + last4;
            }

            if (bankName) {
                return bankName;
            }

            return last4
                ? "****" + last4
                : "";
        }

        function reviewPaneRow(
            label,
            value,
            valueClass
        ) {
            return (
                '<div class="' +
                classPrefix +
                'review-pane-row">' +
                '<span class="' +
                classPrefix +
                'review-pane-label">' +
                escapeHtml(label) +
                "</span>" +
                '<span class="' +
                classPrefix +
                "review-pane-value" +
                (
                    valueClass
                        ? " " + valueClass
                        : ""
                ) +
                '">' +
                escapeHtml(value || "-") +
                "</span>" +
                "</div>"
            );
        }

        function reviewCheckRow(
            ok,
            title,
            detail
        ) {
            return (
                '<div class="' +
                classPrefix +
                "review-check " +
                (
                    ok
                        ? classPrefix +
                          "review-check-ok"
                        : classPrefix +
                          "review-check-warn"
                ) +
                '">' +
                '<span class="' +
                classPrefix +
                'review-check-icon">' +
                '<svg viewBox="0 0 24 24">' +
                (
                    ok
                        ? '<polyline points="20 6 9 17 4 12"></polyline>'
                        : '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>'
                ) +
                "</svg>" +
                "</span>" +
                '<span class="' +
                classPrefix +
                'review-check-text">' +
                '<strong>' +
                escapeHtml(title) +
                "</strong>" +
                '<span>' +
                escapeHtml(detail || "") +
                "</span>" +
                "</span>" +
                "</div>"
            );
        }

        function renderReviewDetail(detail) {
            detail = detail || {};

            if (
                !$reviewBody ||
                !$reviewBanner
            ) {
                return;
            }

            var confidence =
                String(
                    detail.confidence || ""
                ).toUpperCase();

            var bannerClass =
                confidence === "HIGH"
                    ? classPrefix +
                      "review-banner-high"
                    : classPrefix +
                      "review-banner-review";

            var verdict =
                confidence === "HIGH"
                    ? lbl(
                        "VAS_072_HighConfidenceMatch",
                        "Strong match"
                    )
                    : lbl(
                        "VAS_072_NeedsReview",
                        "Needs review"
                    );

            var score =
                Math.max(
                    0,
                    toNumber(
                        detail.score,
                        0
                    )
                );

            $reviewBanner
                .attr(
                    "class",
                    classPrefix +
                    "review-banner " +
                    bannerClass
                )
                .html(
                    '<span class="' +
                    classPrefix +
                    'review-banner-icon">' +
                    '<svg viewBox="0 0 24 24">' +
                    '<polyline points="20 6 9 17 4 12"></polyline>' +
                    "</svg>" +
                    "</span>" +
                    '<span class="' +
                    classPrefix +
                    'review-banner-copy">' +
                    '<strong>' +
                    escapeHtml(
                        verdict +
                        " — " +
                        lbl(
                            "VAS_072_PaymentAndInvoiceLineUp",
                            "payment and invoice line up"
                        )
                    ) +
                    "</strong>" +
                    '<span>' +
                    escapeHtml(
                        lbl(
                            "VAS_072_MatchSignalsAgree",
                            "Vendor, amount and timing all agree."
                        )
                    ) +
                    "</span>" +
                    "</span>" +
                    '<span class="' +
                    classPrefix +
                    'review-score">' +
                    '<strong>' +
                    escapeHtml(score + "%") +
                    "</strong>" +
                    '<span>' +
                    escapeHtml(
                        lbl(
                            "VAS_072_Confidence",
                            "Confidence"
                        )
                    ) +
                    "</span>" +
                    "</span>"
                );

            var paymentAmount =
                formatDetailAmount(
                    detail,
                    detail.paymentOpenAmount,
                    "payment"
                );

            var invoiceGrand =
                formatDetailAmount(
                    detail,
                    detail.invoiceOriginalAmount,
                    "invoice"
                );

            var invoiceOpen =
                formatDetailAmount(
                    detail,
                    detail.invoiceOpenAmount,
                    "invoice"
                );

            var invoiceOpenPay =
                formatDetailAmount(
                    detail,
                    detail.invoiceOpenAmountPaymentCurrency,
                    "payment"
                );

            var balance =
                toNumber(
                    detail.balanceAfterApply,
                    0
                );

            var balanceText =
                formatDetailAmount(
                    detail,
                    detail.isReturnCycle
                        ? -Math.abs(balance)
                        : Math.abs(balance),
                    "payment"
                );

            var paymentPane =
                '<div class="' +
                classPrefix +
                'review-pane">' +
                '<div class="' +
                classPrefix +
                'review-pane-head">' +
                '<span>' +
                escapeHtml(
                    lbl(
                        "VAS_072_VendorPayment",
                        "Vendor payment"
                    )
                ) +
                "</span>" +
                '<strong>' +
                escapeHtml(
                    detail.paymentDocumentNo || ""
                ) +
                "</strong>" +
                "</div>" +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DocumentType",
                        "Document type"
                    ),
                    detail.paymentDocTypeName
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_PaymentDate",
                        "Payment date"
                    ),
                    formatDate(detail.paymentDate)
                ) +
                reviewPaneRow(
                    lbl("VAS_Vendor", "Vendor"),
                    detail.vendorName
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_PaymentMethod",
                        "Payment method"
                    ),
                    detail.paymentMethodName
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_Reference",
                        "Reference"
                    ),
                    detail.reference
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_BankAccount",
                        "Bank account"
                    ),
                    formatBankAccount(detail)
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_PaymentCurrency",
                        "Currency"
                    ),
                    detail.paymentCurrencyISOCode
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_AmountPaid",
                        "Amount paid"
                    ),
                    paymentAmount,
                    classPrefix +
                    "review-paid"
                ) +
                "</div>";

            var invoicePane =
                '<div class="' +
                classPrefix +
                'review-pane">' +
                '<div class="' +
                classPrefix +
                'review-pane-head">' +
                '<span>' +
                escapeHtml(
                    lbl(
                        "VAS_072_SuggestedInvoice",
                        "Suggested invoice"
                    )
                ) +
                "</span>" +
                '<strong>' +
                escapeHtml(
                    detail.invoiceDocumentNo || ""
                ) +
                "</strong>" +
                "</div>" +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DocumentType",
                        "Document type"
                    ),
                    detail.invoiceDocTypeName
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_InvoiceDate",
                        "Invoice date"
                    ),
                    formatDate(detail.invoiceDate)
                ) +
                reviewPaneRow(
                    lbl("VAS_Vendor", "Vendor"),
                    detail.vendorName
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_PaymentTerms",
                        "Payment terms"
                    ),
                    detail.paymentTerms
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DueDate",
                        "Due date"
                    ),
                    formatDate(detail.dueDate)
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_GrandTotal",
                        "Grand total"
                    ),
                    invoiceGrand
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_PaymentCurrency",
                        "Currency"
                    ),
                    detail.invoiceCurrencyISOCode
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_OpenAmount",
                        "Open amount"
                    ),
                    invoiceOpen,
                    classPrefix +
                    "review-open"
                ) +
                "</div>";

            var balanceClass =
                balance === 0
                    ? classPrefix +
                      "review-balance-exact"
                    : classPrefix +
                      "review-balance-open";

            var balanceLabel =
                balance === 0
                    ? lbl(
                        "VAS_072_FullySettles",
                        "balance — fully settles the invoice"
                    )
                    : lbl(
                        "VAS_072_StillOpen",
                        "still open after apply"
                    );

            var compare =
                '<div class="' +
                classPrefix +
                'review-compare">' +
                paymentPane +
                '<span class="' +
                classPrefix +
                'review-arrow">' +
                '<svg viewBox="0 0 24 24">' +
                '<line x1="5" y1="12" x2="19" y2="12"></line>' +
                '<polyline points="12 5 19 12 12 19"></polyline>' +
                "</svg>" +
                "</span>" +
                invoicePane +
                "</div>";

            var why =
                '<div class="' +
                classPrefix +
                'review-why">' +
                '<div class="' +
                classPrefix +
                'review-why-title">' +
                escapeHtml(
                    lbl(
                        "VAS_072_WhyThisMatch",
                        "Why this match"
                    )
                ) +
                "</div>" +
                reviewCheckRow(
                    !!detail.partnerOk,
                    lbl(
                        "VAS_072_VendorMatches",
                        "Vendor matches"
                    ),
                    lbl(
                        "VAS_072_VendorMatchesDetail",
                        "Payment and invoice belong to the same vendor"
                    )
                ) +
                reviewCheckRow(
                    !!detail.amountOk,
                    detail.amountOk
                        ? lbl(
                            "VAS_072_AmountMatches",
                            "Amount matches"
                        )
                        : lbl(
                            "VAS_072_AmountDiffers",
                            "Amount differs"
                        ),
                    paymentAmount +
                    " " +
                    lbl("VAS_072_Vs", "vs") +
                    " " +
                    invoiceOpenPay
                ) +
                reviewCheckRow(
                    !!detail.referenceOk,
                    detail.referenceOk
                        ? lbl(
                            "VAS_072_ReferenceCited",
                            "Reference cited"
                        )
                        : lbl(
                            "VAS_072_NoReferenceCited",
                            "No reference cited"
                        ),
                    detail.reference || ""
                ) +
                reviewCheckRow(
                    !!detail.dateOk,
                    detail.dateOk
                        ? lbl(
                            "VAS_072_WithinDueWindow",
                            "Within due window"
                        )
                        : lbl(
                            "VAS_072_OutsideDueWindow",
                            "Outside due window"
                        ),
                    (
                        formatDate(detail.paymentDate) ||
                        ""
                    ) +
                    (
                        detail.dueDate
                            ? " → " +
                              formatDate(detail.dueDate)
                            : ""
                    )
                ) +
                "</div>";

            $reviewBody.html(
                compare +
                '<div class="' +
                classPrefix +
                "review-balance " +
                balanceClass +
                '">' +
                '<span>' +
                escapeHtml(balanceLabel) +
                "</span>" +
                '<strong>' +
                escapeHtml(balanceText) +
                "</strong>" +
                "</div>" +
                why
            );

            if ($reviewApply) {
                $reviewApply
                    .text(
                        Math.abs(balance) > 0
                            ? lbl(
                                "VAS_072_ApplyPartPayment",
                                "Apply as part-payment"
                            )
                            : lbl(
                                "VAS_072_ApplyAllocation",
                                "Apply allocation"
                            )
                    )
                    .prop("disabled", false);
            }
        }

        function applyCurrentReview() {
            if (
                !currentReviewRow ||
                isLoading
            ) {
                return;
            }

            applySingleSuggestion(
                currentReviewRow
            );
        }

        function applySingleSuggestion(row) {
            if (
                isDisposed ||
                isLoading ||
                !row
            ) {
                return;
            }

            var paymentId =
                toNumber(
                    row.paymentId,
                    0
                );

            var invoiceId =
                toNumber(
                    row.invoiceId,
                    0
                );

            var payScheduleId =
                toNumber(
                    row.payScheduleId,
                    0
                );

            if (
                paymentId <= 0 ||
                invoiceId <= 0 ||
                payScheduleId <= 0
            ) {
                VIS.ADialog.error(
                    null,
                    null,
                    lbl(
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    )
                );

                return;
            }

            var reloadAfterComplete = false;

            setBusy(true);
            showReviewBusy(true, true);

            activeActionRequest = $.ajax({
                url:
                    controllerUrl +
                    "ApplyAllocation",

                type: "POST",
                cache: false,

                data: {
                    paymentId:
                        paymentId,

                    invoiceId:
                        invoiceId,

                    payScheduleId:
                        payScheduleId
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
                        data.success !== true ||
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
                            "VAS_072 ApplyAllocation error:",
                            message,
                            data
                        );

                        VIS.ADialog.error(
                            null,
                            null,
                            message
                        );

                        return;
                    }

                    VIS.ADialog.info(
                        null,
                        null,
                        getServerMessage(
                            data,
                            lbl(
                                "VAS_072_ApplySuccess",
                                "Allocation completed successfully"
                            )
                        )
                    );

                    closeReviewDialog();

                    if (
                        pageNo > 1 &&
                        currentRows.length <= 1
                    ) {
                        pageNo--;
                    }

                    reloadAfterComplete = true;
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (
                        isDisposed ||
                        status === "abort"
                    ) {
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
                        null,
                        null,
                        message
                    );
                },

                complete: function () {
                    activeActionRequest = null;

                    if (isDisposed) {
                        return;
                    }

                    setBusy(false);
                    showReviewBusy(false, true);

                    if (reloadAfterComplete) {
                        loadData();
                    }
                }
            });
        }

        function applyHighConfidence() {
            if (
                isDisposed ||
                isLoading ||
                highConfidenceCount <= 0
            ) {
                return;
            }

            var reloadAfterComplete = false;

            setBusy(true);

            activeActionRequest = $.ajax({
                url:
                    controllerUrl +
                    "ApplyHighConfidence",

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
                        data.success !== true ||
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
                            "VAS_072 ApplyHighConfidence error:",
                            message,
                            data
                        );

                        VIS.ADialog.error(
                            null,
                            null,
                            message
                        );

                        return;
                    }

                    VIS.ADialog.info(
                        null,
                        null,
                        getServerMessage(
                            data,
                            lbl(
                                "VAS_072_ApplySuccess",
                                "Allocation completed successfully"
                            )
                        )
                    );

                    pageNo = 1;
                    reloadAfterComplete = true;
                },

                error: function (
                    xhr,
                    status,
                    errorThrown
                ) {
                    if (
                        isDisposed ||
                        status === "abort"
                    ) {
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
                        null,
                        null,
                        message
                    );
                },

                complete: function () {
                    activeActionRequest = null;

                    if (isDisposed) {
                        return;
                    }

                    setBusy(false);

                    if (reloadAfterComplete) {
                        loadData();
                    }
                }
            });
        }

        function startFormSmart(formId) {
            var numericFormId =
                toNumber(
                    formId,
                    0
                );

            if (numericFormId <= 0) {
                return false;
            }

            if (
                VIS.viewManager &&
                typeof VIS.viewManager.startForm ===
                "function"
            ) {
                try {
                    var viewManagerArgCount =
                        VIS.viewManager
                            .startForm.length || 0;

                    if (viewManagerArgCount === 2) {
                        VIS.viewManager.startForm(
                            numericFormId,
                            0
                        );

                        return true;
                    }

                    if (viewManagerArgCount >= 3) {
                        VIS.viewManager.startForm(
                            numericFormId,
                            0,
                            ""
                        );

                        return true;
                    }

                    VIS.viewManager.startForm(
                        numericFormId
                    );

                    return true;
                }
                catch (error) {
                    console.error(
                        "VAS_072 viewManager.startForm error:",
                        error
                    );
                }
            }

            if (
                VIS.AEnv &&
                typeof VIS.AEnv.startForm ===
                "function"
            ) {
                try {
                    var aEnvArgCount =
                        VIS.AEnv
                            .startForm.length || 0;

                    if (aEnvArgCount === 2) {
                        VIS.AEnv.startForm(
                            numericFormId,
                            ""
                        );

                        return true;
                    }

                    if (aEnvArgCount >= 3) {
                        VIS.AEnv.startForm(
                            numericFormId,
                            0,
                            ""
                        );

                        return true;
                    }

                    VIS.AEnv.startForm(
                        numericFormId
                    );

                    return true;
                }
                catch (error) {
                    console.error(
                        "VAS_072 AEnv.startForm error:",
                        error
                    );
                }
            }

            return false;
        }

        function openAllocationForm() {
            if (
                isLoading ||
                allocationFormId <= 0
            ) {
                return;
            }

            var opened =
                startFormSmart(
                    allocationFormId
                );

            if (!opened) {
                VIS.ADialog.error(
                    null,
                    null,
                    lbl(
                        "VAS_072_OpenFormError",
                        "Could not open allocation form"
                    )
                );
            }
        }

        function createReviewDialog() {
            $reviewDialog = $(
                '<div class="' +
                classPrefix +
                'review-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="' +
                classPrefix +
                'review-scrim"></div>' +
                '<div class="' +
                classPrefix +
                'review-card">' +
                '<div class="' +
                classPrefix +
                'review-header">' +
                '<div class="' +
                classPrefix +
                'review-htext">' +
                '<span class="' +
                classPrefix +
                'review-hicon">' +
                '<svg viewBox="0 0 24 24">' +
                '<path d="M9 11l3 3L22 4"></path>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path>' +
                "</svg>" +
                "</span>" +
                '<span class="' +
                classPrefix +
                'review-title-group">' +
                '<span class="' +
                classPrefix +
                'review-title">' +
                escapeHtml(
                    lbl(
                        "VAS_072_MatchReview",
                        "Match review"
                    )
                ) +
                "</span>" +
                '<span class="' +
                classPrefix +
                'review-sub"></span>' +
                "</span>" +
                "</div>" +
                '<button type="button" class="' +
                classPrefix +
                'review-close" aria-label="' +
                escapeHtml(
                    lbl("VAS_Close", "Close")
                ) +
                '">' +
                '<svg viewBox="0 0 24 24">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                "</svg>" +
                "</button>" +
                "</div>" +
                '<div class="' +
                classPrefix +
                'review-dialog-body">' +
                '<div class="' +
                classPrefix +
                'review-banner"></div>' +
                '<div class="' +
                classPrefix +
                'review-content"></div>' +
                "</div>" +
                '<div class="' +
                classPrefix +
                'review-footer">' +
                '<span class="' +
                classPrefix +
                'review-footnote">' +
                escapeHtml(
                    lbl(
                        "VAS_072_HighConfidenceSafe",
                        "High-confidence — safe to apply"
                    )
                ) +
                "</span>" +
                '<div class="' +
                classPrefix +
                'review-actions">' +
                '<button type="button" class="' +
                classPrefix +
                'review-skip">' +
                escapeHtml(
                    lbl("VAS_072_Skip", "Skip")
                ) +
                "</button>" +
                '<button type="button" class="' +
                classPrefix +
                'review-apply">' +
                '<svg viewBox="0 0 24 24">' +
                '<polyline points="20 6 9 17 4 12"></polyline>' +
                "</svg>" +
                '<span>' +
                escapeHtml(
                    lbl(
                        "VAS_072_ApplyAllocation",
                        "Apply allocation"
                    )
                ) +
                "</span>" +
                "</button>" +
                "</div>" +
                "</div>" +
                /*
                 * The busy overlay is a direct child of the card so it
                 * covers the whole dialog (header, body and footer)
                 * instead of only the scrollable body.
                 */
                '<div class="' +
                classPrefix +
                'review-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                "</div>" +
                "</div>" +
                "</div>" +
                "</div>"
            );

            $reviewBody =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-content"
                );

            $reviewBanner =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-banner"
                );

            $reviewBusy =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-busy"
                );

            $reviewTitle =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-title"
                );

            $reviewSub =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-sub"
                );

            $reviewApply =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-apply"
                );

            if (
                $reviewBusy &&
                $reviewBusy[0]
            ) {
                $reviewBusy[0].style.visibility =
                    "hidden";
            }

            $reviewDialog
                .find(
                    "." +
                    classPrefix +
                    "review-close"
                )
                .on(
                    "click",
                    function (event) {
                        event.preventDefault();
                        event.stopPropagation();

                        closeReviewDialog();
                    }
                );

            $reviewDialog
                .find(
                    "." +
                    classPrefix +
                    "review-scrim"
                )
                .on(
                    "click",
                    function () {
                        closeReviewDialog();
                    }
                );

            $reviewDialog
                .find(
                    "." +
                    classPrefix +
                    "review-skip"
                )
                .on(
                    "click",
                    function (event) {
                        event.preventDefault();
                        closeReviewDialog();
                    }
                );

            $reviewApply.on(
                "click",
                function (event) {
                    event.preventDefault();
                    applyCurrentReview();
                }
            );

            $(document).on(
                "keydown." +
                classPrefix +
                "review",
                function (event) {
                    if (
                        event.key === "Escape" &&
                        $reviewDialog &&
                        $reviewDialog.is(":visible")
                    ) {
                        closeReviewDialog();
                    }
                }
            );

            $("body").append($reviewDialog);
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
                'open-form">' +

                escapeHtml(
                    lbl(
                        "VAS_072_OpenAllocationForm",
                        "Open Allocation Form"
                    )
                ) +

                " →</button>" +

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

                '<span class="' +
                classPrefix +
                'showing"></span>' +

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

              
                "</div>" +
                "</div>" +
                "</div>"
            );

            $rows =
                $card.find(
                    "." +
                    classPrefix +
                    "rows"
                );

            $applyHigh =
                $card.find(
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

            $pagerPrev =
                $card.find(
                    "." +
                    classPrefix +
                    "pager-prev"
                );

            $pagerNext =
                $card.find(
                    "." +
                    classPrefix +
                    "pager-next"
                );

            $pagerText =
                $card.find(
                    "." +
                    classPrefix +
                    "pager-text"
                );

            $showingText =
                $card.find(
                    "." +
                    classPrefix +
                    "showing"
                );

            $openForm =
                $card.find(
                    "." +
                    classPrefix +
                    "open-form"
                );

            $applyHigh.on(
                "click",
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    applyHighConfidence();
                }
            );

            $pagerPrev.on(
                "click",
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

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
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    if (
                        isLoading ||
                        totalPages <= 0 ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;
                    loadData();
                }
            );

            $openForm.on(
                "click",
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    openAllocationForm();
                }
            );

            $openForm.hide();

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

            $stateText =
                $state.find(
                    "." +
                    classPrefix +
                    "state-text"
                );

            $root.append($busy);
            $root.append($state);

            updatePager();

            $applyHigh.prop(
                "disabled",
                true
            );
        }

        function updateAdaptivePageSize(shouldReload) {
            if (!$rows || !$rows[0]) {
                return;
            }

            var nextPageSize = Math.max(
                widgetMinimumRows,
                Math.floor($rows[0].clientHeight / widgetRowHeight)
            );

            if (nextPageSize === pageSize) {
                return;
            }

            var firstVisibleRecord =
                ((pageNo - 1) * pageSize) + 1;

            pageSize = nextPageSize;
            pageNo = Math.max(
                1,
                Math.ceil(firstVisibleRecord / pageSize)
            );

            if (shouldReload) {
                /*
                 * A request kicked off with the pre-layout (wrong)
                 * pageSize may still be in flight here. Without
                 * aborting it first, loadData()'s isLoading guard
                 * silently drops this corrective reload and the
                 * widget is stuck showing the wrong row count until
                 * the user manually refreshes.
                 */
                abortRequest(activeListRequest);
                activeListRequest = null;

                if (!activeActionRequest) {
                    setBusy(false);
                }

                loadData();
            }
        }

        function setupAdaptivePagination() {
            if (!$rows || !$rows[0]) {
                return;
            }

            updateAdaptivePageSize(false);

            var scheduleReflow = window.requestAnimationFrame
                ? window.requestAnimationFrame.bind(window)
                : function (callback) {
                    window.setTimeout(callback, 0);
                };

            scheduleReflow(function () {
                scheduleReflow(function () {
                    updateAdaptivePageSize(true);
                });
            });

            if (window.ResizeObserver) {
                resizeObserver = new ResizeObserver(function () {
                    updateAdaptivePageSize(true);
                });

                resizeObserver.observe($rows[0]);
            }
        }

        function abortRequest(request) {
            if (
                request &&
                request.readyState !== 4 &&
                typeof request.abort ===
                "function"
            ) {
                try {
                    request.abort();
                }
                catch (error) {
                    console.error(
                        "VAS_072 request abort error:",
                        error
                    );
                }
            }
        }

        this.Initalize = function () {
            createWidget();
            setupAdaptivePagination();
            createReviewDialog();
            loadData();
        };

        this.refreshData = function () {
            if (isDisposed) {
                return;
            }

            abortRequest(
                activeListRequest
            );

            activeListRequest = null;

            if (!activeActionRequest) {
                setBusy(false);
            }

            pageNo = 1;
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            abortRequest(
                activeListRequest
            );

            abortRequest(
                activeActionRequest
            );

            abortRequest(
                activeDetailRequest
            );

            if (resizeObserver) {
                resizeObserver.disconnect();
                resizeObserver = null;
            }

            activeListRequest = null;
            activeActionRequest = null;
            activeDetailRequest = null;

            $(document).off(
                "keydown." +
                classPrefix +
                "review"
            );

            closeReviewDialog();

            if ($reviewDialog) {
                $reviewDialog.remove();
                $reviewDialog = null;
            }

            if ($root) {
                $root
                    .find("*")
                    .off();

                $root.off();
                $root.remove();
            }

            currentRows = [];

            $rows = null;
            $busy = null;
            $state = null;
            $stateText = null;
            $applyHigh = null;
            $footerSummary = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $openForm = null;
            $reviewBody = null;
            $reviewBanner = null;
            $reviewBusy = null;
            $reviewTitle = null;
            $reviewSub = null;
            $reviewApply = null;
            $root = null;
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
