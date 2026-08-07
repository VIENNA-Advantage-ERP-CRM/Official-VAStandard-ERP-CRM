/**
 * AP Payment Match Suggestions Widget
 * Purpose - The PAYMENT (IsSOTrx = 'N') mirror of VAS_035_MatchSuggestions.
 *           Pairs each unallocated vendor payment of the last 30 days with its
 *           best-fit open purchase-invoice pay schedule (same partner, closest
 *           amount in the PAYMENT currency, converted at the payment date) and
 *           tags the pairing High / Review / Low. Every amount carries the AP
 *           cycle sign — negative against a purchase invoice (API), positive
 *           against a purchase return / credit memo (APC) — matching the
 *           MultiplierAP convention the allocation line is written with. Each
 *           row opens a match-review modal: confidence banner, payment ↔
 *           invoice two-pane compare, balance strip and the "why this match"
 *           signal list. The modal Apply
 *           button POSTs to ApplyAllocation, which creates and completes a
 *           C_AllocationHdr on the payment accounting date (same outcome as the
 *           standard Allocation form). The header "Open allocation form" link
 *           opens the standard Allocation form (AD_Form classname
 *           VAdvantage.Apps.AForms.VAllocation) via
 *           VIS.viewManager.startForm(AD_Form_ID); it stays visible and
 *           clickable at all times, including while loading and when the list
 *           has no suggestions.
 *
 * Backend - VAS_072_MatchSuggestionAPPaymentWidget/GetMatchSuggestions (paged list)
 *           VAS_072_MatchSuggestionAPPaymentWidget/GetMatchDetail       (review modal)
 *           VAS_072_MatchSuggestionAPPaymentWidget/ApplyAllocation      (create + complete allocation)
 *           All three are thin wrappers over
 *           VASLogic.Models.VAS_072_MatchSuggestionAPPaymentModel and return a
 *           JSON string inside a JSON response, so responses are parsed twice.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+---------------------------------------
 *  1  | Match Suggestions                                 | VAS_072_MatchSuggestions
 *  2  | Vendor payments paired with their best-fit...     | VAS_072_Subtitle
 *  3  | High                                              | VAS_072_High
 *  4  | Review                                            | VAS_072_Review
 *  5  | Low                                               | VAS_072_Low
 *  6  | Open                                              | VAS_072_Open
 *  7  | Due                                               | VAS_072_Due
 *  8  | suggestion                                        | VAS_072_Suggestion
 *  9  | suggestions                                       | VAS_072_Suggestions
 * 10  | Open allocation form                              | VAS_072_OpenAllocationForm
 * 10a | Could not open allocation form                    | VAS_072_OpenFormError
 * 11  | No match suggestions found                        | VAS_072_NoData
 * 12  | Could not load match suggestions                  | VAS_072_LoadError
 * 13  | Could not complete allocation                     | VAS_072_ApplyError
 * 14  | Allocation completed successfully                 | VAS_072_ApplySuccess
 * 15  | Showing                                           | VAS_Showing
 * 16  | Previous                                          | VAS_Previous
 * 17  | Next                                              | VAS_Next
 * 18  | Of                                                | VAS_Of
 * 19  | Match review                                      | VAS_072_MatchReview
 * 20  | Could not load match details                      | VAS_072_LoadDetailError
 * 21  | Strong match                                      | VAS_072_HighConfidenceMatch
 * 22  | Needs review                                      | VAS_072_NeedsReview
 * 23  | Low confidence match                              | VAS_072_LowConfidence
 * 24  | payment and invoice line up                       | VAS_072_PaymentAndInvoiceLineUp
 * 25  | Vendor, amount and timing all agree.              | VAS_072_MatchSignalsAgree
 * 26  | Confidence                                        | VAS_072_Confidence
 * 27  | Vendor payment                                    | VAS_072_VendorPayment
 * 28  | Payment date                                      | VAS_PaymentDate
 * 29  | Vendor                                            | VAS_Vendor
 * 30  | Payment method                                    | VAS_072_PaymentMethod
 * 31  | Reference                                         | VAS_072_Reference
 * 32  | Bank account                                      | VAS_072_BankAccount
 * 33  | Currency                                          | VAS_PaymentCurrency
 * 34  | Amount paid                                       | VAS_072_AmountPaid
 * 35  | Suggested invoice                                 | VAS_072_SuggestedInvoice
 * 36  | Invoice date                                      | VAS_072_InvoiceDate
 * 37  | Payment terms                                     | VAS_072_PaymentTerms
 * 38  | Due date                                          | VAS_072_DueDate
 * 39  | Grand total                                       | VAS_072_GrandTotal
 * 40  | Open amount                                       | VAS_072_OpenAmount
 * 41  | balance — fully settles the invoice               | VAS_072_FullySettles
 * 42  | still open after apply                            | VAS_072_StillOpen
 * 43  | remains unallocated on the payment after apply    | VAS_072_RemainsOnPayment
 * 44  | Why this match                                    | VAS_072_WhyThisMatch
 * 45  | Vendor matches                                    | VAS_072_VendorMatches
 * 46  | Payment and invoice belong to the same vendor     | VAS_072_VendorMatchesDetail
 * 47  | Amount matches                                    | VAS_072_AmountMatches
 * 48  | Amount differs                                    | VAS_072_AmountDiffers
 * 49  | vs                                                | VAS_072_Vs
 * 50  | Reference cited                                   | VAS_072_ReferenceCited
 * 51  | Invoice number found in the payment reference     | VAS_072_ReferenceCitedDetail
 * 52  | No reference cited                                | VAS_072_NoReferenceCited
 * 53  | Invoice number not found in the payment reference | VAS_072_NoReferenceCitedDetail
 * 54  | Within due window                                 | VAS_072_WithinDueWindow
 * 55  | Outside due window                                | VAS_072_OutsideDueWindow
 * 56  | days from due date                                | VAS_072_DaysFromDueDate
 * 57  | Apply as part-payment                             | VAS_072_ApplyPartPayment
 * 58  | Apply allocation                                  | VAS_072_ApplyAllocation
 * 59  | Close                                             | VAS_Close
 * 60  | Skip                                              | VAS_072_Skip
 * 61  | Document type                                     | VAS_072_DocumentType
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
        var $reviewFootnote = null;
        var $reviewApply = null;

        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 0;
        var totalRecords = 0;

        /* AD_Form_ID of the standard Allocation form (resolved server-side from
           AD_Form.ClassName = 'VAdvantage.Apps.AForms.VAllocation'). */
        var allocationFormId = 0;

        var currentRows = [];

        /* Pairing currently shown in the modal — the Apply target. The pay
           schedule id pins the allocation to exactly the suggested schedule. */
        var currentReviewRow = null;

        /* Only a pairing whose detail actually came back may be applied — the
           busy overlay clearing must not re-enable Apply over an error state. */
        var reviewDetailLoaded = false;

        var isLoading = false;
        var isApplying = false;
        var isDisposed = false;

        var activeListRequest = null;
        var activeActionRequest = null;
        var activeDetailRequest = null;
        var resizeObserver = null;
        var adaptiveResizeHandler = null;
        var adaptiveResizeFrame = null;
        var widgetRowHeight = 54;
        var widgetMinimumRows = 2;
        var adaptiveAdjustCount = 0;

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

        /*
         * The endpoints serialize the model to a JSON string and then return
         * that string as the JSON payload, so a response can need parsing
         * twice before it is an object.
         */
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

        function setBusy(show) {
            isLoading = Boolean(show);

            if ($busy) {
                $busy.toggleClass(
                    "is-visible",
                    isLoading
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

        /**
         * True when the session runs a right-to-left culture
         * (Arabic/Farsi); the login sets dir on <html>.
         */
        function isRtl() {
            return (
                document.documentElement &&
                document.documentElement.dir ===
                "rtl"
            );
        }

        /**
         * Arrow that points the way the eye travels, so
         * "payment leads to invoice" keeps agreeing with the
         * order the two documents are laid out in. A fixed
         * "→" contradicts itself once the UI flips.
         */
        function flowArrow() {
            return isRtl()
                ? "←"
                : "→";
        }

        function formatNumber(value, precision) {
            var amount =
                toNumber(value, 0);

            var decimalPrecision =
                normalizePrecision(precision);

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

        /*
         * The sign is carried outside the symbol so a refund cycle reads
         * "-$120.00" rather than "$-120.00".
         */
        function formatAmount(value, symbol, precision) {
            var numericValue =
                toNumber(value, 0);

            var sign =
                numericValue < 0
                    ? "-"
                    : "";

            var amount =
                formatNumber(
                    Math.abs(numericValue),
                    precision
                );

            var currencySymbol =
                symbol
                    ? String(symbol)
                    : "";

            /* 3-char ISO codes read better after the amount; glyph symbols before. */
            if (currencySymbol.length === 3) {
                return sign + amount + " " + currencySymbol;
            }

            return currencySymbol
                ? sign + currencySymbol + amount
                : sign + amount;
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

        /* AP display sign, keyed off the invoice document base type: a purchase
           invoice (API) reads NEGATIVE — money leaving — and a purchase return
           / credit memo (APC) reads POSITIVE. This is the same MultiplierAP
           convention the allocation line is written with, so what the card
           shows agrees in sign with the document it creates. The server keeps
           its own signing for the matching maths (both legs must agree in sign
           to pair at all); this is presentation only, hence the magnitude.  */
        function cycleAmount(value, invoiceDocBaseType) {
            var sign =
                invoiceDocBaseType === "APC"
                    ? 1
                    : -1;

            return sign * Math.abs(toNumber(value, 0));
        }

        /* Both list amounts arrive in the PAYMENT currency (the invoice open
           amount is converted server-side at the payment accounting date), so
           one symbol/precision pair covers the whole row. */
        function rowAmount(row, value) {
            return formatAmount(
                value,
                row.PaymentCurrencySymbol ||
                row.PaymentCurrency,
                row.PaymentPrecision
            );
        }

        function confidenceOf(value) {
            var confidence =
                String(value || "")
                    .toUpperCase();

            return confidence === "HIGH" ||
                confidence === "REVIEW"
                ? confidence
                : "LOW";
        }

        function confidenceLabel(confidence) {
            if (confidence === "HIGH") {
                return lbl("VAS_072_High", "High");
            }

            if (confidence === "REVIEW") {
                return lbl("VAS_072_Review", "Review");
            }

            return lbl("VAS_072_Low", "Low");
        }

        function confidenceSuffix(confidence) {
            if (confidence === "HIGH") {
                return "high";
            }

            if (confidence === "REVIEW") {
                return "review";
            }

            return "low";
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
                    confidenceOf(row.Confidence);

                /* APC = purchase credit memo: the refund cycle, where both
                   legs are negative and the allocation line flips sign. */
                var isReturnCycle =
                    row.InvoiceDocBaseType === "APC";

                var rowClass =
                    classPrefix +
                    "row" +
                    (
                        confidence === "HIGH"
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

                var confidenceClass =
                    classPrefix +
                    "confidence " +
                    classPrefix +
                    "confidence-" +
                    confidenceSuffix(confidence);

                var paymentAmount =
                    rowAmount(
                        row,
                        cycleAmount(
                            row.PaymentAmount,
                            row.InvoiceDocBaseType
                        )
                    );

                var invoiceOpenAmount =
                    rowAmount(
                        row,
                        cycleAmount(
                            row.OpenAmount,
                            row.InvoiceDocBaseType
                        )
                    );

                var dueDate =
                    formatDate(row.DueDate);

                /*
                 * Built as markup rather than plain text so each
                 * value can be isolated on its own: the separators
                 * and labels then follow the reading direction while
                 * the amount and the date keep their internal order.
                 */
                var metaHtml =
                    " · " +
                    escapeHtml(
                        lbl(
                            "VAS_072_Open",
                            "Open"
                        )
                    ) +
                    " <bdi>" +
                    escapeHtml(
                        invoiceOpenAmount
                    ) +
                    "</bdi>";

                if (dueDate) {
                    metaHtml +=
                        " · " +
                        escapeHtml(
                            lbl(
                                "VAS_072_Due",
                                "Due"
                            )
                        ) +
                        " <bdi>" +
                        escapeHtml(
                            dueDate
                        ) +
                        "</bdi>";
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
                        row.Vendor || ""
                    ) +

                    /*
                     * The separator belongs to the line, not to the
                     * figure: keeping it outside the isolate lets it
                     * sit between the vendor and the amount the way
                     * the sentence reads, while the amount itself
                     * stays a left-to-right token.
                     */
                    '<span class="' +
                    classPrefix +
                    'amount"> · ' +

                    "<bdi>" +
                    escapeHtml(
                        paymentAmount
                    ) +
                    "</bdi>" +

                    "</span>" +

                    "</div>" +

                    '<div class="' +
                    classPrefix +
                    'link">' +

                    '<span class="' +
                    classPrefix +
                    'mono">' +

                    escapeHtml(
                        row.PaymentNo || ""
                    ) +

                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    'arrow">' +
                    flowArrow() +
                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    "mono " +
                    classPrefix +
                    'invoice">' +

                    escapeHtml(
                        row.InvoiceNo || ""
                    ) +

                    "</span>" +

                    '<span class="' +
                    classPrefix +
                    'meta">' +

                    metaHtml +

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
                        confidenceLabel(confidence)
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

            // Real rows exist now, so re-check the fit against their height.
            updateAdaptivePageSize(true);
        }

        function renderResult(data) {
            data = data || {};

            var rows =
                Array.isArray(data.Rows)
                    ? data.Rows
                    : [];

            pageNo =
                Math.max(
                    1,
                    toNumber(
                        data.PageNo,
                        pageNo
                    )
                );

            totalPages =
                Math.max(
                    0,
                    toNumber(
                        data.TotalPages,
                        0
                    )
                );

            totalRecords =
                Math.max(
                    0,
                    toNumber(
                        data.TotalRecords,
                        0
                    )
                );

            /* Keep the last id that actually resolved: an empty page or a
               failed reload must not take the Allocation form link away. */
            var resolvedFormId =
                Math.max(
                    0,
                    toNumber(
                        data.AllocationFormId,
                        0
                    )
                );

            if (resolvedFormId > 0) {
                allocationFormId = resolvedFormId;
            }

            renderRows(rows);
            updatePager();
        }

        function updatePager() {
            /* Left helper: "Showing X-Y of Z suggestions" result range. */
            if ($showingText) {
                if (totalRecords > 0) {
                    var startIndex =
                        ((pageNo - 1) * pageSize) + 1;

                    var endIndex =
                        Math.min(pageNo * pageSize, totalRecords);

                    var suggestionLabel =
                        totalRecords === 1
                            ? lbl(
                                "VAS_072_Suggestion",
                                "suggestion"
                            )
                            : lbl(
                                "VAS_072_Suggestions",
                                "suggestions"
                            );

                    $showingText.text(
                        lbl("VAS_Showing", "Showing") + " " +
                        startIndex + "–" + endIndex + " " +
                        lbl("VAS_Of", "of") + " " + totalRecords + " " +
                        suggestionLabel
                    );
                }
                else {
                    $showingText.text("");
                }
            }

            /* The indicator always reads, down to "1 of 1" on an empty or
               single-page list: blanking it left the two arrows framing a gap,
               which looks like a pager that failed to load rather than one
               with nowhere to go. Being disabled is what says that. */
            if ($pagerText) {
                $pagerText.text(
                    pageNo +
                    " " +
                    lbl(
                        "VAS_Of",
                        "of"
                    ) +
                    " " +
                    Math.max(1, totalPages)
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

                    if (!data) {
                        renderResult({});

                        showState(
                            lbl(
                                "VAS_072_LoadError",
                                "Could not load match suggestions"
                            )
                        );

                        return;
                    }

                    renderResult(data);
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

                    console.error(
                        "VAS_072 GetMatchSuggestions AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    showState(
                        lbl(
                            "VAS_072_LoadError",
                            "Could not load match suggestions"
                        )
                    );
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
                        !currentReviewRow ||
                        !reviewDetailLoaded
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

        /* ── Allocation form (VAdvantage.Apps.AForms.VAllocation) ─────── */

        /* Always live — the link is not gated on the list having loaded or on
           there being any suggestion to show. It only fails when the standard
           Allocation form is not registered, and that is worth telling the
           user rather than swallowing as a dead click. */
        function openAllocationForm() {
            if (allocationFormId <= 0) {
                VIS.ADialog.error(
                    null,
                    null,
                    lbl(
                        "VAS_072_OpenFormError",
                        "Could not open allocation form"
                    )
                );

                return;
            }

            closeReviewDialog();
            VIS.viewManager.startForm(allocationFormId);
        }

        /* ── Match-review modal ───────────────────────────────────────── */

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
            reviewDetailLoaded = false;
            isApplying = false;

            if ($reviewTitle) {
                $reviewTitle.text(
                    lbl(
                        "VAS_072_MatchReview",
                        "Match review"
                    ) +
                    (
                        row.Vendor
                            ? " · " + row.Vendor
                            : ""
                    )
                );
            }

            if ($reviewSub) {
                $reviewSub.text(
                    (
                        row.PaymentNo || ""
                    ) +
                    (
                        row.InvoiceNo
                            ? " " +
                              flowArrow() +
                              " " +
                              row.InvoiceNo
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

            if ($reviewFootnote) {
                $reviewFootnote.text("");
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
                            row.PaymentId,
                            0
                        ),

                    invoiceId:
                        toNumber(
                            row.InvoiceId,
                            0
                        ),

                    payScheduleId:
                        toNumber(
                            row.InvoicePayScheduleId,
                            0
                        )
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    if (!data) {
                        renderReviewEmpty(
                            lbl(
                                "VAS_072_LoadDetailError",
                                "Could not load match details"
                            )
                        );

                        return;
                    }

                    renderReviewDetail(data);
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

                    console.error(
                        "VAS_072 GetMatchDetail AJAX error:",
                        status,
                        errorThrown,
                        xhr
                            ? xhr.responseText
                            : ""
                    );

                    renderReviewEmpty(
                        lbl(
                            "VAS_072_LoadDetailError",
                            "Could not load match details"
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
            reviewDetailLoaded = false;

            if ($reviewBody) {
                $reviewBody.empty();
            }
        }

        function renderReviewEmpty(message) {
            reviewDetailLoaded = false;

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

            if ($reviewApply) {
                $reviewApply.prop("disabled", true);
            }
        }

        function paymentAmountText(detail, value) {
            return formatAmount(
                value,
                detail.PaymentCurrencySymbol ||
                detail.PaymentCurrency,
                detail.PaymentPrecision
            );
        }

        function invoiceAmountText(detail, value) {
            return formatAmount(
                value,
                detail.InvoiceCurrencySymbol ||
                detail.InvoiceCurrency,
                detail.InvoicePrecision
            );
        }

        function formatBankAccount(detail) {
            var bankName =
                detail && detail.BankName
                    ? String(detail.BankName).trim()
                    : "";

            var accountNo =
                detail && detail.AccountNo
                    ? String(detail.AccountNo).trim()
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
                confidenceOf(detail.Confidence);

            /* Every amount on the card carries the AP cycle sign (API negative,
               APC positive), so both panes, the balance strip and the amount
               signal read the same way round. */
            var invoiceDocBaseType =
                detail.InvoiceDocBaseType;

            var paymentMagnitude =
                Math.abs(toNumber(detail.PaymentAmount, 0));

            var openPayMagnitude =
                Math.abs(toNumber(detail.OpenAmountPay, 0));

            /* Short = the payment covers less than the schedule, so the
               remainder stays open on the schedule (a part-payment).
               Over = the payment exceeds it and the surplus stays
               unallocated on the payment. Compared on magnitudes so the
               refund cycle, where both legs are negative, reads the same. */
            var isShort =
                openPayMagnitude - paymentMagnitude > 0;

            var isOver =
                paymentMagnitude - openPayMagnitude > 0;

            var bannerClass =
                classPrefix +
                "review-banner-" +
                confidenceSuffix(confidence);

            var verdict =
                confidence === "HIGH"
                    ? lbl(
                        "VAS_072_HighConfidenceMatch",
                        "Strong match"
                    )
                    : (
                        confidence === "REVIEW"
                            ? lbl(
                                "VAS_072_NeedsReview",
                                "Needs review"
                            )
                            : lbl(
                                "VAS_072_LowConfidence",
                                "Low confidence match"
                            )
                    );

            var score =
                Math.max(
                    0,
                    toNumber(
                        detail.Score,
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
                paymentAmountText(
                    detail,
                    cycleAmount(
                        detail.PaymentAmount,
                        invoiceDocBaseType
                    )
                );

            var invoiceGrand =
                invoiceAmountText(
                    detail,
                    cycleAmount(
                        detail.GrandTotal,
                        invoiceDocBaseType
                    )
                );

            var invoiceOpen =
                invoiceAmountText(
                    detail,
                    cycleAmount(
                        detail.OpenAmount,
                        invoiceDocBaseType
                    )
                );

            /* Open amount converted to the payment currency at the payment
               accounting date — basis of the compare, the balance line and
               the amount signal. */
            var invoiceOpenPay =
                paymentAmountText(
                    detail,
                    cycleAmount(
                        detail.OpenAmountPay,
                        invoiceDocBaseType
                    )
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
                    detail.PaymentNo || ""
                ) +
                "</strong>" +
                "</div>" +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DocumentType",
                        "Document type"
                    ),
                    detail.PaymentDocType
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_PaymentDate",
                        "Payment date"
                    ),
                    formatDate(detail.PaymentDate)
                ) +
                reviewPaneRow(
                    lbl("VAS_Vendor", "Vendor"),
                    detail.PaymentVendor
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_PaymentMethod",
                        "Payment method"
                    ),
                    detail.PaymentMethod
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_Reference",
                        "Reference"
                    ),
                    detail.Reference
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
                    detail.PaymentCurrency
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
                    detail.InvoiceNo || ""
                ) +
                "</strong>" +
                "</div>" +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DocumentType",
                        "Document type"
                    ),
                    detail.InvoiceDocType
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_InvoiceDate",
                        "Invoice date"
                    ),
                    formatDate(detail.InvoiceDate)
                ) +
                reviewPaneRow(
                    lbl("VAS_Vendor", "Vendor"),
                    detail.InvoiceVendor
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_PaymentTerms",
                        "Payment terms"
                    ),
                    detail.PaymentTerms
                ) +
                reviewPaneRow(
                    lbl(
                        "VAS_072_DueDate",
                        "Due date"
                    ),
                    formatDate(detail.DueDate)
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
                    detail.InvoiceCurrency
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

            /* Balance strip (payment currency): exact / short / over, carrying
               the same AP cycle sign as the two panes above it. */
            var balanceClass;
            var balanceLabel;
            var balanceText;

            if (isShort) {
                balanceClass =
                    classPrefix +
                    "review-balance-open";

                balanceLabel =
                    lbl(
                        "VAS_072_StillOpen",
                        "still open after apply"
                    );

                balanceText =
                    paymentAmountText(
                        detail,
                        cycleAmount(
                            detail.BalanceAfterApply,
                            invoiceDocBaseType
                        )
                    );
            }
            else if (isOver) {
                balanceClass =
                    classPrefix +
                    "review-balance-open";

                balanceLabel =
                    lbl(
                        "VAS_072_RemainsOnPayment",
                        "remains unallocated on the payment after apply"
                    );

                balanceText =
                    paymentAmountText(
                        detail,
                        cycleAmount(
                            detail.BalanceAfterApply,
                            invoiceDocBaseType
                        )
                    );
            }
            else {
                balanceClass =
                    classPrefix +
                    "review-balance-exact";

                balanceLabel =
                    lbl(
                        "VAS_072_FullySettles",
                        "balance — fully settles the invoice"
                    );

                balanceText =
                    paymentAmountText(detail, 0);
            }

            var gapDays =
                toNumber(detail.DateGapDays, -1);

            var gapText =
                gapDays >= 0
                    ? gapDays +
                      " " +
                      lbl(
                          "VAS_072_DaysFromDueDate",
                          "days from due date"
                      )
                    : "-";

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
                    !!detail.PartnerOk,
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
                    !!detail.AmountOk,
                    detail.AmountOk
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
                    !!detail.RefOk,
                    detail.RefOk
                        ? lbl(
                            "VAS_072_ReferenceCited",
                            "Reference cited"
                        )
                        : lbl(
                            "VAS_072_NoReferenceCited",
                            "No reference cited"
                        ),
                    detail.RefOk
                        ? lbl(
                            "VAS_072_ReferenceCitedDetail",
                            "Invoice number found in the payment reference"
                        )
                        : lbl(
                            "VAS_072_NoReferenceCitedDetail",
                            "Invoice number not found in the payment reference"
                        )
                ) +
                reviewCheckRow(
                    !!detail.DateOk,
                    detail.DateOk
                        ? lbl(
                            "VAS_072_WithinDueWindow",
                            "Within due window"
                        )
                        : lbl(
                            "VAS_072_OutsideDueWindow",
                            "Outside due window"
                        ),
                    gapText
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

            /* The footnote states the verdict rather than always claiming the
               match is safe — the same modal serves REVIEW and LOW pairings. */
            if ($reviewFootnote) {
                $reviewFootnote.text(verdict);
            }

            reviewDetailLoaded = true;

            if ($reviewApply) {
                $reviewApply
                    .find("span")
                    .text(
                        isShort
                            ? lbl(
                                "VAS_072_ApplyPartPayment",
                                "Apply as part-payment"
                            )
                            : lbl(
                                "VAS_072_ApplyAllocation",
                                "Apply allocation"
                            )
                    );

                $reviewApply.prop("disabled", false);
            }
        }

        /* The allocation DocumentNo is what lets the user find the document
           that was just created, so the confirmation names it. Composed from
           the widget's own label rather than echoing the server message: that
           one is built on the platform-wide AllocationIsCreated key and shows
           the raw key wherever it is not seeded. The server message is still
           the fallback for the (unexpected) case of a completed allocation
           that came back without a number. */
        function applySuccessMessage(data) {
            var documentNo =
                data && data.DocumentNo
                    ? String(data.DocumentNo).trim()
                    : "";

            var successText =
                lbl(
                    "VAS_072_ApplySuccess",
                    "Allocation completed successfully"
                );

            if (!documentNo) {
                return (data && data.Message) || successText;
            }

            return successText + ": " + documentNo;
        }

        /* POSTs the pairing to ApplyAllocation: the server creates and
           completes a C_AllocationHdr dated on the payment accounting date
           (part-payment when the payment is short), then the list reloads. */
        function applyCurrentReview() {
            if (
                isDisposed ||
                isApplying ||
                !currentReviewRow
            ) {
                return;
            }

            var row = currentReviewRow;

            var paymentId =
                toNumber(row.PaymentId, 0);

            var invoiceId =
                toNumber(row.InvoiceId, 0);

            var payScheduleId =
                toNumber(row.InvoicePayScheduleId, 0);

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

            isApplying = true;
            showReviewBusy(true, true);

            activeActionRequest = $.ajax({
                url:
                    controllerUrl +
                    "ApplyAllocation",

                type: "POST",
                cache: false,

                data: {
                    paymentId: paymentId,
                    invoiceId: invoiceId,
                    payScheduleId: payScheduleId
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    /* ADialog.info translates its FIRST arg as a message key;
                       dynamic server text goes verbatim in the THIRD arg. */
                    if (
                        !data ||
                        data.Success !== true
                    ) {
                        VIS.ADialog.error(
                            null,
                            null,
                            (data && data.Message) ||
                            lbl(
                                "VAS_072_ApplyError",
                                "Could not complete allocation"
                            )
                        );

                        return;
                    }

                    closeReviewDialog();

                    VIS.ADialog.info(
                        null,
                        null,
                        applySuccessMessage(data)
                    );

                    /* The applied row leaves the result set; stepping back a
                       page keeps the user on rows that still exist. */
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
                        lbl(
                            "VAS_072_ApplyError",
                            "Could not complete allocation"
                        )
                    );
                },

                complete: function () {
                    activeActionRequest = null;
                    isApplying = false;

                    if (isDisposed) {
                        return;
                    }

                    showReviewBusy(false, true);

                    if (reloadAfterComplete) {
                        loadData();
                    }
                }
            });
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
                'review-footnote"></span>' +
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

            $reviewFootnote =
                $reviewDialog.find(
                    "." +
                    classPrefix +
                    "review-footnote"
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

                '<a href="javascript:void(0)" class="' +
                classPrefix +
                'open-form">' +

                '<span class="' +
                classPrefix +
                'open-form-text">' +

                escapeHtml(
                    lbl(
                        "VAS_072_OpenAllocationForm",
                        "Open Allocation Form"
                    )
                ) +

                "</span>" +

                '<span class="' +
                classPrefix +
                'open-form-arrow">' +
                flowArrow() +
                "</span>" +

                "</a>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'rows"></div>' +

                /* Footer: "Showing X-Y of Z suggestions" helper (left) +
                   compact pager (right), on one line. */
                '<div class="' +
                classPrefix +
                'footer">' +

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
                "</div>"
            );

            $rows =
                $card.find(
                    "." +
                    classPrefix +
                    "rows"
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

            /* The header button lands the user on the standard Allocation form
               where every suggestion is one click away. */
            $openForm.on(
                "click",
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    openAllocationForm();
                }
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

            $stateText =
                $state.find(
                    "." +
                    classPrefix +
                    "state-text"
                );

            $root.append($busy);
            $root.append($state);

            updatePager();
        }

        /*
         * The fixed height is only a starting guess; once a row is on screen
         * its real height is used so the count matches what actually fits.
         */
        function measureWidgetRowHeight() {
            var $row = $rows
                ? $rows.find("." + classPrefix + "row").first()
                : null;

            var measured = $row && $row.length
                ? $row.outerHeight(true)
                : 0;

            return measured > 0 ? measured : widgetRowHeight;
        }

        function measureWidgetRowGap() {
            if (!$rows || !$rows[0] || !window.getComputedStyle) {
                return 0;
            }

            var rowsStyle = window.getComputedStyle($rows[0]);
            var measuredGap = parseFloat(
                rowsStyle.rowGap || rowsStyle.gap
            );

            return isNaN(measuredGap) ? 0 : measuredGap;
        }

        function updateAdaptivePageSize(shouldReload) {
            if (!$rows || !$rows[0]) {
                return;
            }

            var rowHeight = measureWidgetRowHeight();
            var rowGap = measureWidgetRowGap();
            var nextPageSize = Math.max(
                widgetMinimumRows,
                Math.floor(
                    ($rows[0].clientHeight + rowGap) /
                    (rowHeight + rowGap)
                )
            );

            if (nextPageSize === pageSize) {
                adaptiveAdjustCount = 0;
                return;
            }

            /*
             * Each render re-checks the fit, so cap the corrections to stop a
             * layout that never settles from looping.
             */
            if (adaptiveAdjustCount >= 4) {
                return;
            }

            adaptiveAdjustCount++;

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

            if (!adaptiveResizeHandler) {
                adaptiveResizeHandler = function () {
                    if (adaptiveResizeFrame !== null) {
                        return;
                    }

                    adaptiveResizeFrame = window.requestAnimationFrame(function () {
                        adaptiveResizeFrame = null;
                        adaptiveAdjustCount = 0;
                        updateAdaptivePageSize(true);
                    });
                };

                window.addEventListener("resize", adaptiveResizeHandler);

                if (window.visualViewport) {
                    window.visualViewport.addEventListener(
                        "resize",
                        adaptiveResizeHandler
                    );
                }
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

            if (adaptiveResizeHandler) {
                window.removeEventListener(
                    "resize",
                    adaptiveResizeHandler
                );

                if (window.visualViewport) {
                    window.visualViewport.removeEventListener(
                        "resize",
                        adaptiveResizeHandler
                    );
                }

                adaptiveResizeHandler = null;
            }

            if (adaptiveResizeFrame !== null) {
                window.cancelAnimationFrame(
                    adaptiveResizeFrame
                );
                adaptiveResizeFrame = null;
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
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $openForm = null;
            $reviewBody = null;
            $reviewBanner = null;
            $reviewBusy = null;
            $reviewTitle = null;
            $reviewSub = null;
            $reviewFootnote = null;
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
