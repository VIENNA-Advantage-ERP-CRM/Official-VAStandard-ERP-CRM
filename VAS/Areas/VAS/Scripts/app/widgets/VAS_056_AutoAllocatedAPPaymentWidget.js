
/**
 * Auto Allocated AP Payment Widget
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+------------------------------
 *  1  | No payments in this period                        | VAS_056_NoPaymentsThisPeriod
 *  2  | payment                                           | VAS_056_Payment
 *  3  | payments                                          | VAS_056_Payments
 *  4  | Showing                                           | VAS_Showing
 *  5  | of                                                | VAS_Of
 *  6  | Payment allocation                                | VAS_056_PaymentAllocation
 *  7  | Allocated vs unallocated payments in the last...  | VAS_056_AllocatedVsUnallocated
 *  8  | Close                                             | VAS_Close
 *  9  | Allocated                                         | VAS_Allocated
 * 10  | Unallocated                                       | VAS_Unallocated
 * 11  | Date                                              | VAS_Date
 * 12  | Payment No.                                       | VAS_056_PaymentNo
 * 13  | Vendor                                            | VAS_Vendor
 * 14  | Bank account                                      | VAS_BankAccount
 * 15  | Payment Currency                                  | VAS_PaymentCurrency
 * 16  | Amount                                            | VAS_Amount
 * 17  | Previous                                          | VAS_Previous
 * 18  | Next                                              | VAS_Next
 * 19  | Auto-allocated AP                                 | VAS_056_AutoAllocatedAPPayments
 * 20  | Payment Match to Invoice                          | VAS_056_PaymentMatchToInvoice
 * ──────────────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_056_AutoAllocatedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var self = this;

        var classPrefix =
            "VAS-056-AutoAllocatedAPPayment-";

        var $root = $(
            '<div class="' +
            classPrefix +
            'root"></div>'
        );

        var $metricEl = null;
        var $busy = null;

        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;

        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var $tabAllocated = null;
        var $tabUnallocated = null;
        var $tabAllocatedCount = null;
        var $tabUnallocatedCount = null;

        var kpiRequest = null;
        var rowsRequest = null;

        var rowsLoading = false;
        var pageSize = 10;
        var activeFilter = "allocated";
        var isDisposed = false;

        var tabState = {
            allocated: {
                pageNo: 1,
                totalPages: 0,
                totalRecords: 0,
                loaded: false
            },

            unallocated: {
                pageNo: 1,
                totalPages: 0,
                totalRecords: 0,
                loaded: false
            }
        };

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
            var data = response;

            for (var index = 0; index < 2; index++) {
                if (typeof data !== "string") {
                    break;
                }

                try {
                    data = JSON.parse(data);
                }
                catch (e) {
                    return null;
                }
            }

            return data;
        }

        function isDialogVisible() {
            return Boolean(
                $dialog &&
                $dialog.length &&
                $dialog.is(":visible")
            );
        }

        function showBusy(show) {
            if (!$busy) {
                return;
            }

            $busy.toggleClass(
                "is-visible",
                Boolean(show)
            );
        }

        function showDialogBusy(show) {
            if (
                !$dialogBusy ||
                !$dialogBusy.length
            ) {
                return;
            }

            $dialogBusy.toggleClass(
                "is-visible",
                Boolean(show)
            );
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            if (isDisposed) {
                return;
            }

            if (
                kpiRequest &&
                kpiRequest.readyState !== 4
            ) {
                kpiRequest.abort();
            }

            showBusy(true);

            kpiRequest = $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPayments",

                type: "GET",
                dataType: "json",
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    if (
                        !data ||
                        data.error ||
                        data.errorText
                    ) {
                        setNoData();
                        return;
                    }

                    renderMetric(data);
                },

                error: function (
                    xhr,
                    textStatus
                ) {
                    if (
                        isDisposed ||
                        textStatus === "abort"
                    ) {
                        return;
                    }

                    setNoData();
                },

                complete: function () {
                    kpiRequest = null;

                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function loadRows() {
            if (
                isDisposed ||
                !$dialogTbody ||
                !$dialogTbody.length ||
                rowsLoading
            ) {
                return;
            }

            var filterAtRequest =
                activeFilter;

            var state =
                tabState[filterAtRequest];

            if (!state) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);

            if ($pagerPrev) {
                $pagerPrev.prop(
                    "disabled",
                    true
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    "disabled",
                    true
                );
            }

            rowsRequest = $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPaymentRows",

                type: "GET",
                dataType: "json",
                cache: false,

                data: {
                    pageNo:
                        state.pageNo,

                    pageSize:
                        pageSize,

                    filter:
                        filterAtRequest
                },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        parseResponse(response);

                    if (
                        !data ||
                        data.error ||
                        data.errorText
                    ) {
                        tabState[
                            filterAtRequest
                        ].loaded = false;

                        if (
                            filterAtRequest ===
                            activeFilter
                        ) {
                            renderPageResult(
                                filterAtRequest,
                                {
                                    rows: [],
                                    pageNo:
                                        state.pageNo,

                                    pageSize:
                                        pageSize,

                                    totalRecords:
                                        0,

                                    totalPages:
                                        0
                                }
                            );
                        }

                        return;
                    }

                    tabState[
                        filterAtRequest
                    ].loaded = true;

                    /*
                     * Only render this response when it
                     * belongs to the currently active tab.
                     */
                    if (
                        filterAtRequest ===
                        activeFilter
                    ) {
                        renderPageResult(
                            filterAtRequest,
                            data
                        );
                    }
                },

                error: function (
                    xhr,
                    textStatus
                ) {
                    if (
                        isDisposed ||
                        textStatus === "abort"
                    ) {
                        return;
                    }

                    tabState[
                        filterAtRequest
                    ].loaded = false;

                    if (
                        filterAtRequest ===
                        activeFilter
                    ) {
                        renderPageResult(
                            filterAtRequest,
                            {
                                rows: [],
                                pageNo:
                                    state.pageNo,

                                pageSize:
                                    pageSize,

                                totalRecords:
                                    0,

                                totalPages:
                                    0
                            }
                        );
                    }
                },

                complete: function () {
                    rowsRequest = null;

                    if (isDisposed) {
                        return;
                    }

                    /*
                     * Always release the loading flag.
                     * Do not return before this block.
                     */
                    rowsLoading = false;
                    showDialogBusy(false);

                    /*
                     * The user may switch tabs while the
                     * previous request is still running.
                     */
                    if (
                        filterAtRequest !==
                        activeFilter
                    ) {
                        if (
                            isDialogVisible() &&
                            !tabState[
                                activeFilter
                            ].loaded
                        ) {
                            loadRows();
                        }
                        else {
                            updatePagerControlsForCurrentState();
                        }

                        return;
                    }

                    updatePagerControlsForCurrentState();
                }
            });
        }

        function renderMetric(data) {
            var percent = Number(
                data.autoAllocatedPercent || 0
            );

            if (isNaN(percent)) {
                percent = 0;
            }

            if ($metricEl) {
                $metricEl.text(
                    formatPercent(percent)
                );
            }

            var matched = Number(
                data.matchedPayments || 0
            );

            var unmatched = Number(
                data.unmatchedPayments || 0
            );

            if (isNaN(matched) || matched < 0) {
                matched = 0;
            }

            if (
                isNaN(unmatched) ||
                unmatched < 0
            ) {
                unmatched = 0;
            }

            tabState.allocated.totalRecords =
                matched;

            tabState.unallocated.totalRecords =
                unmatched;

            tabState.allocated.totalPages =
                pageSize > 0
                    ? Math.ceil(
                        matched / pageSize
                    )
                    : 0;

            tabState.unallocated.totalPages =
                pageSize > 0
                    ? Math.ceil(
                        unmatched / pageSize
                    )
                    : 0;

            updateTabCounts();

            if (isDialogVisible()) {
                tabState[
                    activeFilter
                ].loaded = false;

                if (!rowsLoading) {
                    loadRows();
                }
            }
        }

        function renderPageResult(
            filter,
            data
        ) {
            var state =
                tabState[filter];

            if (!state) {
                return;
            }

            var rows =
                data &&
                Array.isArray(data.rows)
                    ? data.rows
                    : [];

            var totalRecords = Number(
                data &&
                data.totalRecords || 0
            );

            var totalPages = Number(
                data &&
                data.totalPages || 0
            );

            if (
                isNaN(totalRecords) ||
                totalRecords < 0
            ) {
                totalRecords = 0;
            }

            if (
                isNaN(totalPages) ||
                totalPages < 0
            ) {
                totalPages = 0;
            }

            state.totalRecords =
                totalRecords;

            state.totalPages =
                totalPages;

            if (
                data &&
                typeof data.pageNo !==
                "undefined"
            ) {
                var responsePageNo =
                    Number(data.pageNo);

                if (
                    !isNaN(responsePageNo) &&
                    responsePageNo > 0
                ) {
                    state.pageNo =
                        responsePageNo;
                }
            }

            if (
                state.totalPages > 0 &&
                state.pageNo >
                state.totalPages
            ) {
                state.pageNo =
                    state.totalPages;
            }

            if (
                !state.pageNo ||
                state.pageNo < 1
            ) {
                state.pageNo = 1;
            }

            renderRows(rows);

            var from =
                state.totalRecords === 0
                    ? 0
                    : (
                        state.pageNo - 1
                    ) * pageSize;

            var to = Math.min(
                from + rows.length,
                state.totalRecords
            );

            updatePagerControls(
                from,
                to
            );

            updateTabCounts();
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (
                !rows ||
                rows.length === 0
            ) {
                $dialogTbody.html(
                    "<tr>" +

                    '<td class="' +
                    classPrefix +
                    'dialog-empty" colspan="6">' +

                    escapeHtml(
                        lbl(
                            "VAS_056_NoPaymentsThisPeriod",
                            "No payments in this period"
                        )
                    ) +

                    "</td>" +

                    "</tr>"
                );

                return;
            }

            for (
                var index = 0;
                index < rows.length;
                index++
            ) {
                var row =
                    rows[index] || {};

                var dateText =
                    formatDate(row.date);

                var documentNo =
                    row.documentNo || "";

                var vendor =
                    row.vendor ||
                    row.vendorName ||
                    "";

                var bankText =
                    formatBankAccount(row);

                var currencyCode =
                    row.paymentCurrency ||
                    row.currencyISO ||
                    "";

                var amountText =
                    formatAmount(
                        row.amount
                    );

                /*
                 * Amount is numeric only.
                 * Currency is already displayed in
                 * a separate table column.
                 */
                var amountHtml =
                    escapeHtml(amountText);

                var rowHtml =
                    "<tr>" +

                    '<td class="' +
                    classPrefix +
                    'td-date" title="' +
                    escapeHtml(dateText) +
                    '">' +
                    escapeHtml(dateText) +
                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-doc" title="' +
                    escapeHtml(documentNo) +
                    '">' +

                    '<span class="' +
                    classPrefix +
                    'truncate">' +
                    escapeHtml(documentNo) +
                    "</span>" +

                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-customer" title="' +
                    escapeHtml(vendor) +
                    '">' +

                    '<span class="' +
                    classPrefix +
                    'truncate">' +
                    escapeHtml(vendor) +
                    "</span>" +

                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-bank" title="' +
                    escapeHtml(bankText) +
                    '">' +

                    '<span class="' +
                    classPrefix +
                    'truncate">' +
                    escapeHtml(bankText) +
                    "</span>" +

                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-currency" title="' +
                    escapeHtml(currencyCode) +
                    '">' +
                    escapeHtml(currencyCode) +
                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-amount" title="' +
                    escapeHtml(amountText) +
                    '">' +
                    amountHtml +
                    "</td>" +

                    "</tr>";

                $dialogTbody.append(
                    rowHtml
                );
            }
        }

        function updatePagerControlsForCurrentState() {
            var state =
                tabState[activeFilter];

            if (!state) {
                return;
            }

            var from =
                state.totalRecords === 0
                    ? 0
                    : (
                        state.pageNo - 1
                    ) * pageSize;

            var to = Math.min(
                from + pageSize,
                state.totalRecords
            );

            updatePagerControls(
                from,
                to
            );
        }

        function updatePagerControls(
            from,
            to
        ) {
            var state =
                tabState[activeFilter];

            if (!state) {
                return;
            }

            updateTabCounts();

            if ($pagerHelper) {
                if (
                    state.totalRecords > 0
                ) {
                    var paymentLabel =
                        state.totalRecords === 1
                            ? lbl(
                                "VAS_056_Payment",
                                "payment"
                            )
                            : lbl(
                                "VAS_056_Payments",
                                "payments"
                            );

                    var firstDisplayed =
                        Math.min(
                            from + 1,
                            state.totalRecords
                        );

                    $pagerHelper.text(
                        lbl(
                            "VAS_Showing",
                            "Showing"
                        ) +
                        " " +
                        firstDisplayed +
                        "-" +
                        to +
                        " " +
                        lbl(
                            "VAS_Of",
                            "of"
                        ) +
                        " " +
                        state.totalRecords +
                        " " +
                        paymentLabel
                    );
                }
                else {
                    $pagerHelper.text("");
                }
            }

            if ($pagerText) {
                $pagerText.text(
                    state.totalPages > 0
                        ? (
                            state.pageNo +
                            " " +
                            lbl(
                                "VAS_Of",
                                "of"
                            ) +
                            " " +
                            state.totalPages
                        )
                        : ""
                );
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    "disabled",
                    rowsLoading ||
                    state.pageNo <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    "disabled",
                    rowsLoading ||
                    state.totalPages <= 1 ||
                    state.pageNo >=
                    state.totalPages
                );
            }
        }

        function updateTabCounts() {
            if ($tabAllocatedCount) {
                $tabAllocatedCount.text(
                    tabState
                        .allocated
                        .totalRecords
                );
            }

            if ($tabUnallocatedCount) {
                $tabUnallocatedCount.text(
                    tabState
                        .unallocated
                        .totalRecords
                );
            }
        }

        function switchTab(filter) {
            if (
                filter !== "allocated" &&
                filter !== "unallocated"
            ) {
                return;
            }

            if (
                filter === activeFilter &&
                tabState[filter].loaded
            ) {
                updatePagerControlsForCurrentState();
                return;
            }

            activeFilter = filter;

            updateActiveTabStyles();
            updatePagerControlsForCurrentState();

            if (
                !tabState[filter].loaded &&
                !rowsLoading
            ) {
                loadRows();
            }
        }

        function updateActiveTabStyles() {
            if (
                !$tabAllocated ||
                !$tabUnallocated
            ) {
                return;
            }

            $tabAllocated.toggleClass(
                classPrefix + "tab-active",
                activeFilter === "allocated"
            );

            $tabAllocated.attr(
                "aria-selected",
                activeFilter === "allocated"
                    ? "true"
                    : "false"
            );

            $tabUnallocated.toggleClass(
                classPrefix + "tab-active",
                activeFilter === "unallocated"
            );

            $tabUnallocated.attr(
                "aria-selected",
                activeFilter === "unallocated"
                    ? "true"
                    : "false"
            );
        }

        function setNoData() {
            if ($metricEl) {
                $metricEl.text(
                    formatPercent(0)
                );
            }

            tabState.allocated.totalRecords =
                0;

            tabState.allocated.totalPages =
                0;

            tabState.allocated.loaded =
                false;

            tabState.unallocated.totalRecords =
                0;

            tabState.unallocated.totalPages =
                0;

            tabState.unallocated.loaded =
                false;

            updateTabCounts();

            if (isDialogVisible()) {
                renderRows([]);
                updatePagerControlsForCurrentState();
            }
        }

        function formatPercent(value) {
            var numericValue = Number(
                value || 0
            );

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            var precision =
                getStdPrecision();

            return numericValue.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits:
                        precision,

                    maximumFractionDigits:
                        precision
                }
            ) + "%";
        }

        function formatAmount(value) {
            var numericValue = Number(
                value || 0
            );

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            var precision =
                getStdPrecision();

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

        function getStdPrecision() {
            var stdPrecision = 2;

            try {
                if (
                    VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env
                        .getCtx()
                        .getStdPrecision
                ) {
                    stdPrecision = Number(
                        VIS.Env
                            .getCtx()
                            .getStdPrecision()
                    );
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            if (
                isNaN(stdPrecision) ||
                stdPrecision < 0
            ) {
                return 2;
            }

            return stdPrecision;
        }

        function formatDate(value) {
            if (!value) {
                return "";
            }

            if (
                typeof value === "string" &&
                /^\d{4}-\d{2}-\d{2}$/
                    .test(value)
            ) {
                var parts =
                    value.split("-");

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
                    return localDate
                        .toLocaleDateString(
                            window.navigator.language,
                            {
                                day: "2-digit",
                                month: "short",
                                year: "numeric"
                            }
                        );
                }
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

        function formatBankAccount(row) {
            row = row || {};

            var bankName =
                row.bankName
                    ? String(
                        row.bankName
                    ).trim()
                    : "";

            var bankAccountName =
                row.bankAccountName
                    ? String(
                        row.bankAccountName
                    ).trim()
                    : "";

            var accountNo =
                row.accountNo
                    ? String(
                        row.accountNo
                    ).trim()
                    : "";

            var displayName =
                bankName ||
                bankAccountName;

            var last4 = "";

            if (accountNo) {
                last4 =
                    accountNo.length > 4
                        ? accountNo.slice(-4)
                        : accountNo;
            }

            if (
                displayName &&
                last4
            ) {
                return (
                    displayName +
                    " - ****" +
                    last4
                );
            }

            if (displayName) {
                return displayName;
            }

            if (last4) {
                return "****" + last4;
            }

            return "";
        }

        function openDialog() {
            if (
                !$dialog ||
                isDisposed
            ) {
                return;
            }

            $dialog.show();

            $("body").addClass(
                classPrefix +
                "body-lock"
            );

            updateActiveTabStyles();
            updateTabCounts();

            if (
                !tabState[
                    activeFilter
                ].loaded
            ) {
                if (!rowsLoading) {
                    loadRows();
                }
            }
            else {
                updatePagerControlsForCurrentState();
            }
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.hide();

            $("body").removeClass(
                classPrefix +
                "body-lock"
            );

            activeFilter =
                "allocated";

            tabState.allocated.pageNo =
                1;

            tabState.allocated.loaded =
                false;

            tabState.unallocated.pageNo =
                1;

            tabState.unallocated.loaded =
                false;

            updateActiveTabStyles();
        }

        function createDialog() {
            $dialog = $(
                '<div class="' +
                classPrefix +
                'dialog" role="dialog" aria-modal="true">' +

                '<div class="' +
                classPrefix +
                'dialog-scrim"></div>' +

                '<div class="' +
                classPrefix +
                'dialog-card">' +

                '<div class="' +
                classPrefix +
                'dialog-header">' +

                '<div class="' +
                classPrefix +
                'dialog-icon">' +

                '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<path d="M9 11l3 3L22 4"></path>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path>' +
                "</svg>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'dialog-title-group">' +

                '<div class="' +
                classPrefix +
                'dialog-title">' +

                escapeHtml(
                    lbl(
                        "VAS_056_PaymentAllocation",
                        "Payment allocation"
                    )
                ) +

                "</div>" +

                '<div class="' +
                classPrefix +
                'dialog-subtitle">' +

                escapeHtml(
                    lbl(
                        "VAS_056_AllocatedVsUnallocated",
                        "Allocated vs unallocated payments in the last 30 days"
                    )
                ) +

                "</div>" +

                "</div>" +

                '<button type="button" class="' +
                classPrefix +
                'dialog-close" aria-label="' +

                escapeHtml(
                    lbl(
                        "VAS_Close",
                        "Close"
                    )
                ) +

                '">' +

                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                "</svg>" +

                "</button>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'tabs" role="tablist">' +

                '<button type="button" class="' +
                classPrefix +
                "tab " +
                classPrefix +
                "tab-allocated " +
                classPrefix +
                'tab-active" role="tab" aria-selected="true">' +

                '<span class="' +
                classPrefix +
                'tab-label">' +

                escapeHtml(
                    lbl(
                        "VAS_Allocated",
                        "Allocated"
                    )
                ) +

                "</span>" +

                '<span class="' +
                classPrefix +
                "tab-count " +
                classPrefix +
                'tab-count-allocated">0</span>' +

                "</button>" +

                '<button type="button" class="' +
                classPrefix +
                "tab " +
                classPrefix +
                'tab-unallocated" role="tab" aria-selected="false">' +

                '<span class="' +
                classPrefix +
                'tab-label">' +

                escapeHtml(
                    lbl(
                        "VAS_Unallocated",
                        "Unallocated"
                    )
                ) +

                "</span>" +

                '<span class="' +
                classPrefix +
                "tab-count " +
                classPrefix +
                'tab-count-unallocated">0</span>' +

                "</button>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'dialog-body">' +

                '<div class="' +
                classPrefix +
                'dialog-busy">' +

                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                "</div>" +

                "</div>" +

                '<table class="' +
                classPrefix +
                'dialog-table">' +

                "<thead>" +
                "<tr>" +

                '<th class="' +
                classPrefix +
                'th-date">' +

                escapeHtml(
                    lbl(
                        "VAS_Date",
                        "Date"
                    )
                ) +

                "</th>" +

                '<th class="' +
                classPrefix +
                'th-doc">' +

                escapeHtml(
                    lbl(
                        "VAS_056_PaymentNo",
                        "Payment No."
                    )
                ) +

                "</th>" +

                '<th class="' +
                classPrefix +
                'th-customer">' +

                escapeHtml(
                    lbl(
                        "VAS_Vendor",
                        "Vendor"
                    )
                ) +

                "</th>" +

                '<th class="' +
                classPrefix +
                'th-bank">' +

                escapeHtml(
                    lbl(
                        "VAS_BankAccount",
                        "Bank account"
                    )
                ) +

                "</th>" +

                '<th class="' +
                classPrefix +
                'th-currency">' +

                escapeHtml(
                    lbl(
                        "VAS_PaymentCurrency",
                        "Payment Currency"
                    )
                ) +

                "</th>" +

                '<th class="' +
                classPrefix +
                'th-amount">' +

                escapeHtml(
                    lbl(
                        "VAS_Amount",
                        "Amount"
                    )
                ) +

                "</th>" +

                "</tr>" +
                "</thead>" +

                '<tbody class="' +
                classPrefix +
                'dialog-tbody"></tbody>' +

                "</table>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'dialog-footer">' +

                '<span class="' +
                classPrefix +
                'pager-helper"></span>' +

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

                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
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

                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="9 18 15 12 9 6"></polyline>' +
                "</svg>" +

                "</button>" +

                "</div>" +

                "</div>" +

                "</div>" +
                "</div>"
            );

            $dialog.hide();

            $dialogTbody = $dialog.find(
                "." +
                classPrefix +
                "dialog-tbody"
            );

            $dialogBusy = $dialog.find(
                "." +
                classPrefix +
                "dialog-busy"
            );

            showDialogBusy(false);

            $pagerHelper = $dialog.find(
                "." +
                classPrefix +
                "pager-helper"
            );

            $pagerPrev = $dialog.find(
                "." +
                classPrefix +
                "pager-prev"
            );

            $pagerNext = $dialog.find(
                "." +
                classPrefix +
                "pager-next"
            );

            $pagerText = $dialog.find(
                "." +
                classPrefix +
                "pager-text"
            );

            $tabAllocated = $dialog.find(
                "." +
                classPrefix +
                "tab-allocated"
            );

            $tabUnallocated = $dialog.find(
                "." +
                classPrefix +
                "tab-unallocated"
            );

            $tabAllocatedCount =
                $dialog.find(
                    "." +
                    classPrefix +
                    "tab-count-allocated"
                );

            $tabUnallocatedCount =
                $dialog.find(
                    "." +
                    classPrefix +
                    "tab-count-unallocated"
                );

            $dialog.find(
                "." +
                classPrefix +
                "dialog-close, ." +
                classPrefix +
                "dialog-scrim"
            ).on(
                "click",
                closeDialog
            );

            $tabAllocated.on(
                "click",
                function () {
                    switchTab(
                        "allocated"
                    );
                }
            );

            $tabUnallocated.on(
                "click",
                function () {
                    switchTab(
                        "unallocated"
                    );
                }
            );

            $pagerPrev.on(
                "click",
                function () {
                    var state =
                        tabState[
                            activeFilter
                        ];

                    if (
                        rowsLoading ||
                        state.pageNo <= 1
                    ) {
                        return;
                    }

                    state.pageNo--;
                    state.loaded = false;

                    loadRows();
                }
            );

            $pagerNext.on(
                "click",
                function () {
                    var state =
                        tabState[
                            activeFilter
                        ];

                    if (
                        rowsLoading ||
                        state.totalPages <= 0 ||
                        state.pageNo >=
                        state.totalPages
                    ) {
                        return;
                    }

                    state.pageNo++;
                    state.loaded = false;

                    loadRows();
                }
            );

            $(document).on(
                "keydown." +
                classPrefix +
                self.AD_UserHomeWidgetID,

                function (event) {
                    if (
                        event.key ===
                        "Escape" &&
                        isDialogVisible()
                    ) {
                        closeDialog();
                    }
                }
            );

            $("body").append(
                $dialog
            );
        }

        function createWidget() {
            var $card = $(
                '<div class="' +
                classPrefix +
                'card" role="button" tabindex="0">' +

                '<div class="' +
                classPrefix +
                'header">' +

                '<div class="' +
                classPrefix +
                'icon-box">' +

                '<svg class="' +
                classPrefix +
                'icon" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<path d="M9 11l3 3L22 4"></path>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"></path>' +
                "</svg>" +

                "</div>" +

                '<span class="' +
                classPrefix +
                'title">' +

                escapeHtml(
                    lbl(
                        "VAS_056_AutoAllocatedAPPayments",
                        "Auto-allocated AP"
                    )
                ) +

                "</span>" +

                "</div>" +

                '<div class="' +
                classPrefix +
                'body">' +

                '<span class="' +
                classPrefix +
                'value">-</span>' +

                "</div>" +

                '<div class="' +
                classPrefix +
                'footer">' +

                '<span class="' +
                classPrefix +
                'desc">' +

                escapeHtml(
                    lbl(
                        "VAS_056_PaymentMatchToInvoice",
                        "Payment Match to Invoice"
                    )
                ) +

                "</span>" +

                "</div>" +

                "</div>"
            );

            $metricEl = $card.find(
                "." +
                classPrefix +
                "value"
            );

            $card.on(
                "click",
                openDialog
            );

            $card.on(
                "keydown",
                function (event) {
                    if (
                        event.key ===
                        "Enter" ||
                        event.key === " "
                    ) {
                        event.preventDefault();
                        openDialog();
                    }
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

            $root.append($busy);

            createDialog();
        }

        this.refreshData = function () {
            tabState.allocated.loaded =
                false;

            tabState.allocated.pageNo =
                1;

            tabState.unallocated.loaded =
                false;

            tabState.unallocated.pageNo =
                1;

            loadKpi();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (
                kpiRequest &&
                kpiRequest.readyState !== 4
            ) {
                kpiRequest.abort();
            }

            if (
                rowsRequest &&
                rowsRequest.readyState !== 4
            ) {
                rowsRequest.abort();
            }

            kpiRequest = null;
            rowsRequest = null;
            rowsLoading = false;

            $(document).off(
                "keydown." +
                classPrefix +
                this.AD_UserHomeWidgetID
            );

            $("body").removeClass(
                classPrefix +
                "body-lock"
            );

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            if ($root) {
                $root.remove();
            }

            $metricEl = null;
            $busy = null;
            $dialogTbody = null;
            $dialogBusy = null;
            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $tabAllocated = null;
            $tabUnallocated = null;
            $tabAllocatedCount = null;
            $tabUnallocatedCount = null;
        };
    };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.init =
        function (windowNo, frame) {
            this.frame = frame;
            this.windowNo = windowNo;

            if (
                frame &&
                frame.widgetInfo
            ) {
                this.AD_UserHomeWidgetID =
                    frame
                        .widgetInfo
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

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.widgetSizeChange =
        function () {
        };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.refreshWidget =
        function () {
            this.refreshData();
        };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.dispose =
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

