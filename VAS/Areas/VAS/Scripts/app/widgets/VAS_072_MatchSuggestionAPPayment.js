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
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $openForm = null;

        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var totalReadyAmount = 0;
        var highConfidenceCount = 0;

        var allocationFormId = 0;
        var serverStdPrecision = 2;

        var currentRows = [];

        var isLoading = false;
        var isDisposed = false;

        var activeListRequest = null;
        var activeActionRequest = null;

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
            if (!data) {
                return fallback || "";
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

            var amount =
                formatAmount(
                    value,
                    getAmountPrecision(row)
                );

            return symbol
                ? symbol + " " + amount
                : amount;
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
                        applySingleSuggestion(
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

                            applySingleSuggestion(
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
                    symbol
                        ? symbol + " "
                        : ""
                ) +
                formatAmount(
                    totalReadyAmount,
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
                    lbl(
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    )
                );

                return;
            }

            var reloadAfterComplete = false;

            setBusy(true);

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
                    lbl(
                        "VAS_072_OpenFormError",
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

            activeListRequest = null;
            activeActionRequest = null;

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