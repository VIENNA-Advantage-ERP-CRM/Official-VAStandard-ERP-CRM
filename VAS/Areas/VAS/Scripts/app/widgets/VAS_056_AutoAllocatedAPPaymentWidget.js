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
 *  7  | Allocated vs unallocated payments in the last...   | VAS_056_AllocatedVsUnallocated
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
 * 21  | Un Allocated                               | VAS_UnAllocatedAmount
 * ──────────────────────────────────────────────────────────────────────────────
 */

/*
 * Labels / Message Keys
 * #  | Current Text                                      | Message Key
 * ---+---------------------------------------------------+------------------------------
 * 1  | No payments in this period                        | VAS_056_NoPaymentsThisPeriod
 * 2  | payment                                           | VAS_056_Payment
 * 3  | payments                                          | VAS_056_Payments
 * 4  | Showing                                           | VAS_Showing
 * 5  | of                                                | VAS_Of
 * 6  | Payment allocation                                | VAS_056_PaymentAllocation
 * 7  | Allocated vs unallocated payments in the last...   | VAS_056_AllocatedVsUnallocated
 * 8  | Close                                             | VAS_Close
 * 9  | Allocated                                         | VAS_Allocated
 * 10 | Unallocated                                       | VAS_Unallocated
 * 11 | Date                                              | VAS_Date
 * 12 | Payment No.                                       | VAS_056_PaymentNo
 * 13 | Vendor                                            | VAS_Vendor
 * 14 | Bank account                                      | VAS_BankAccount
 * 15 | Payment Currency                                  | VAS_PaymentCurrency
 * 16 | Amount                                            | VAS_Amount
 * 17 | Previous                                          | VAS_Previous
 * 18 | Next                                              | VAS_Next
 * 19 | Auto-allocated AP                                 | VAS_056_AutoAllocatedAPPayments
 * 20 | Payment Match to Invoice                          | VAS_056_PaymentMatchToInvoice
 * 21 | Last 30 days                                      | VAS_Last30Days
 * 22 | Partially allocated                               | VAS_UnAllocatedAmount
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_056_AutoAllocatedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var self = this;

        var classPrefix = "VAS-056-AutoAllocatedAPPayment-";

        var $root = $('<div class="' + classPrefix + 'root"></div>');
        var $body = null;
        var $footer = null;
        var $metricEl = null;
        var $busy = null;
        var $state = null;

        var $dialog = null;
        var $dialogBody = null;
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

        var rowsLoading = false;
        var pageSize = 8;
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

        /*
         * The dialog body is a flex scroll area, so the number of rows that
         * fit follows the popup's rendered height instead of a fixed page
         * size. The row height is measured from a real row when one exists
         * and falls back to this estimate while the table is still empty.
         */
        var dialogResizeObserver = null;
        var adaptiveResizeHandler = null;
        var adaptiveResizeFrame = null;
        var dialogRowHeightEstimate = 44;
        var dialogMinimumRows = 3;
        var adaptiveAdjustCount = 0;
        var adaptivePaginationReady = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text && text !== "[" + key + "]"
                ? text
                : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
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
            catch (e) {
                return null;
            }
        }

        function showBusy(show) {
            if (!$busy) {
                return;
            }

            $busy.toggleClass("is-visible", Boolean(show));
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) {
                return;
            }

            $dialogBusy[0].style.visibility = show
                ? "visible"
                : "hidden";
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);
            showState(false, "");

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPayments",

                type: "GET",
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = parseResponse(response);

                    if (!data || data.error) {
                        setNoData();
                        return;
                    }

                    renderMetric(data);
                },

                error: function () {
                    if (isDisposed) {
                        return;
                    }

                    setNoData();
                },

                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function measureDialogRowHeight() {
            var $row = $dialogTbody
                ? $dialogTbody
                    .find("tr")
                    .not(":has(." + classPrefix + "dialog-empty)")
                    .first()
                : null;

            var measured =
                $row && $row.length
                    ? $row.outerHeight()
                    : 0;

            if (
                measured <= 0 &&
                $dialogTbody &&
                $dialogTbody.length
            ) {
                var $measureRow = $(
                    '<tr aria-hidden="true" style="visibility:hidden;">' +
                    '<td>00 Jan 0000</td>' +
                    '<td>000000</td>' +
                    '<td>Vendor</td>' +
                    '<td>Bank account</td>' +
                    '<td>USD</td>' +
                    '<td>0.00</td>' +
                    '<td>0.00</td>' +
                    '</tr>'
                );

                $dialogTbody.append($measureRow);
                measured = $measureRow.outerHeight();
                $measureRow.remove();
            }

            return measured > 0
                ? measured
                : dialogRowHeightEstimate;
        }

        function updateAdaptivePageSize() {
            if (
                isDisposed ||
                !$dialogBody ||
                !$dialogBody[0] ||
                !$dialogTbody ||
                !$dialogTbody[0] ||
                !$dialog ||
                !$dialog.is(':visible')
            ) {
                return;
            }

            var container = $dialogBody[0];

            if (container.clientHeight <= 0) {
                return;
            }

            /*
             * Whatever sits above the first row inside the scroll area - the
             * table header, padding, any summary strip - is measured from the
             * tbody's own position rather than assumed, so the row space left
             * over is exact.
             */
            var headerOffset =
                (
                    $dialogTbody[0].getBoundingClientRect().top -
                    container.getBoundingClientRect().top
                ) + container.scrollTop;

            var availableHeight = Math.max(
                0,
                container.clientHeight - headerOffset
            );

            var rowHeight = measureDialogRowHeight();

            if (rowHeight <= 0) {
                return;
            }

            var nextPageSize = Math.max(
                dialogMinimumRows,
                Math.floor(availableHeight / rowHeight)
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

            if (rowsLoading) {
                return;
            }

            adaptiveAdjustCount++;

            // Keep the record the user is looking at on screen.
            var firstVisibleRecord =
                ((tabState[activeFilter].pageNo - 1) * pageSize) + 1;

            pageSize = nextPageSize;

            tabState[activeFilter].pageNo = Math.max(
                1,
                Math.ceil(firstVisibleRecord / pageSize)
            );

            for (var key in tabState) {
                if (
                    tabState.hasOwnProperty(key) &&
                    key !== activeFilter
                ) {
                    tabState[key].loaded = false;
                    tabState[key].pageNo = 1;
                }
            }

            if (adaptivePaginationReady) {
                loadRows();
            }
        }

        function setupAdaptivePagination() {
            if (!$dialogBody || !$dialogBody[0]) {
                return;
            }

            adaptivePaginationReady = false;
            adaptiveAdjustCount = 0;
            updateAdaptivePageSize();

            window.setTimeout(function () {
                if (
                    !$dialog ||
                    !$dialog.is(':visible') ||
                    isDisposed
                ) {
                    return;
                }

                updateAdaptivePageSize();

                adaptivePaginationReady = true;

                if (!tabState[activeFilter].loaded) {
                    loadRows();
                }
                else {
                    updatePagerControlsForCurrentState();
                }
            }, 0);

              if (
                  window.ResizeObserver &&
                  !dialogResizeObserver
              ) {
                dialogResizeObserver = new ResizeObserver(
                    function () {
                        updateAdaptivePageSize();
                    }
                );

                dialogResizeObserver.observe($dialogBody[0]);

                /*
                 * The scroll area keeps a fixed height, so only the row
                 * container changes size when rows arrive. Watching it is
                 * what corrects the estimate the first pass had to use.
                 */
                if ($dialogTbody && $dialogTbody[0]) {
                      dialogResizeObserver.observe($dialogTbody[0]);
                  }
              }

              if (!adaptiveResizeHandler) {
                  adaptiveResizeHandler = function () {
                      if (adaptiveResizeFrame !== null) {
                          return;
                      }

                      adaptiveResizeFrame = window.requestAnimationFrame(function () {
                          adaptiveResizeFrame = null;
                          adaptiveAdjustCount = 0;
                          updateAdaptivePageSize();
                      });
                  };

                  window.addEventListener('resize', adaptiveResizeHandler);

                  if (window.visualViewport) {
                      window.visualViewport.addEventListener('resize', adaptiveResizeHandler);
                  }
              }
          }

          function teardownAdaptivePagination() {
            adaptivePaginationReady = false;

            if (dialogResizeObserver) {
                  dialogResizeObserver.disconnect();
                  dialogResizeObserver = null;
              }

              if (adaptiveResizeHandler) {
                  window.removeEventListener('resize', adaptiveResizeHandler);

                  if (window.visualViewport) {
                      window.visualViewport.removeEventListener('resize', adaptiveResizeHandler);
                  }

                  adaptiveResizeHandler = null;
              }

              if (adaptiveResizeFrame !== null) {
                  window.cancelAnimationFrame(adaptiveResizeFrame);
                  adaptiveResizeFrame = null;
              }
          }

        function loadRows() {
            if (!$dialogTbody || rowsLoading) {
                return;
            }

            var filterAtRequest = activeFilter;
            var state = tabState[filterAtRequest];

            rowsLoading = true;
            showDialogBusy(true);

            if ($pagerPrev) {
                $pagerPrev.prop("disabled", true);
            }

            if ($pagerNext) {
                $pagerNext.prop("disabled", true);
            }

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    "VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPaymentRows",

                type: "GET",
                cache: false,

                data: {
                    pageNo: state.pageNo,
                    pageSize: pageSize,
                    filter: filterAtRequest
                },

                success: function (response) {
                    if (
                        isDisposed ||
                        filterAtRequest !== activeFilter
                    ) {
                        return;
                    }

                    var data = parseResponse(response);

                    renderPageResult(
                        filterAtRequest,
                        data && !data.error
                            ? data
                            : {}
                    );

                    tabState[filterAtRequest].loaded = true;
                },

                error: function () {
                    if (
                        isDisposed ||
                        filterAtRequest !== activeFilter
                    ) {
                        return;
                    }

                    renderPageResult(filterAtRequest, {});
                },

                complete: function () {
                    if (isDisposed) {
                        return;
                    }

                    rowsLoading = false;

                    if (filterAtRequest !== activeFilter) {
                        loadRows();
                        return;
                    }

                    updateAdaptivePageSize();

                    if (!rowsLoading) {
                        showDialogBusy(false);
                    }

                    updatePagerControlsForCurrentState();
                }
            });
        }

        function renderMetric(data) {
            var percent = Number(
                data.autoAllocatedPercent || 0
            );

            if ($metricEl) {
                $metricEl.text(formatPercent(percent));
            }

            var matched = Number(
                data.matchedPayments || 0
            );

            var unmatched = Number(
                data.unmatchedPayments || 0
            );

            if (isNaN(matched)) {
                matched = 0;
            }

            if (isNaN(unmatched)) {
                unmatched = 0;
            }

            if (matched + unmatched <= 0) {
                setNoData();
                return;
            }

            showState(false, "");

            tabState.allocated.totalRecords = matched;
            tabState.unallocated.totalRecords = unmatched;

            tabState.allocated.totalPages =
                pageSize > 0
                    ? Math.ceil(matched / pageSize)
                    : 0;

            tabState.unallocated.totalPages =
                pageSize > 0
                    ? Math.ceil(unmatched / pageSize)
                    : 0;

            updateTabCounts();

            if (
                $dialog &&
                $dialog.is(":visible")
            ) {
                tabState[activeFilter].loaded = false;
                loadRows();
            }
        }

        function renderPageResult(filter, data) {
            var state = tabState[filter];

            var rows =
                data && Array.isArray(data.rows)
                    ? data.rows
                    : [];

            state.totalRecords = Number(
                data && data.totalRecords || 0
            );

            state.totalPages = Number(
                data && data.totalPages || 0
            );

            if (
                data &&
                typeof data.pageNo !== "undefined"
            ) {
                state.pageNo = Number(data.pageNo);
            }

            if (
                state.totalPages > 0 &&
                state.pageNo > state.totalPages
            ) {
                state.pageNo = state.totalPages;
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
                    : (state.pageNo - 1) * pageSize;

            var to = Math.min(
                from + rows.length,
                state.totalRecords
            );

            updatePagerControls(from, to);
            updateTabCounts();
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    "<tr>" +
                    '<td class="' +
                    classPrefix +
                    'dialog-empty" colspan="7">' +
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

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i] || {};

                var dateText = formatDate(row.date);
                var documentNo = row.documentNo || "";
                var vendor = row.vendor || "";
                var bankText = formatBankAccount(row);
                var currencyCode = row.paymentCurrency || "";

                var currencySymbol =
                    row.paymentCurrencySymbol ||
                    currencyCode ||
                    "";

                var amountValue = Number(row.amount || 0);

                if (isNaN(amountValue)) {
                    amountValue = 0;
                }

                var amountSign =
                    amountValue < 0 ? "-" : "";

                var amountText = formatAmount(
                    Math.abs(amountValue)
                );

                var amountHtml = amountSign;

                if (currencySymbol) {
                    amountHtml +=
                        '<span class="' +
                        classPrefix +
                        'cur-inline">' +
                        escapeHtml(currencySymbol) +
                        "</span>";
                }

                amountHtml += escapeHtml(amountText);

                var partialHtml = "";
                var partialTitle = "";

                if (row.isPartiallyAllocated) {
                    var unallocatedValue = Number(
                        row.unallocatedAmt || 0
                    );

                    if (isNaN(unallocatedValue)) {
                        unallocatedValue = 0;
                    }

                    var unallocatedSign =
                        unallocatedValue < 0 ? "-" : "";

                    var unallocatedText = formatAmount(
                        Math.abs(unallocatedValue)
                    );

                    partialTitle =
                        unallocatedSign +
                        (
                            currencySymbol
                                ? currencySymbol
                                : ""
                        ) + unallocatedText;

                    partialHtml += unallocatedSign;

                    if (currencySymbol) {
                        partialHtml +=
                            '<span class="' +
                            classPrefix +
                            'cur-inline">' +
                            escapeHtml(currencySymbol) +
                            "</span>";
                    }

                    partialHtml += escapeHtml(unallocatedText);
                }

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
                    'td-partial" title="' +
                    escapeHtml(partialTitle) +
                    '">' +
                    partialHtml +
                    "</td>" +

                    '<td class="' +
                    classPrefix +
                    'td-amount" title="' +
                    escapeHtml(
                        amountSign +
                        (
                            currencySymbol
                                ? currencySymbol
                                : ""
                        ) + amountText
                    ) +
                    '">' +
                    amountHtml +
                    "</td>" +

                    "</tr>";

                $dialogTbody.append(rowHtml);
            }
        
            updateAdaptivePageSize();
        }

        function updatePagerControlsForCurrentState() {
            var state = tabState[activeFilter];

            var from =
                state.totalRecords === 0
                    ? 0
                    : (state.pageNo - 1) * pageSize;

            var to = Math.min(
                from + pageSize,
                state.totalRecords
            );

            updatePagerControls(from, to);
        }

        function updatePagerControls(from, to) {
            var state = tabState[activeFilter];

            updateTabCounts();

            if ($pagerHelper) {
                if (state.totalRecords > 0) {
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

                    $pagerHelper.text(
                        lbl(
                            "VAS_Showing",
                            "Showing"
                        ) +
                        " " +
                        (from + 1) +
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

            /* The indicator always reads, down to "1 of 1" on an empty or
               single-page list: blanking it left the two arrows framing a gap,
               which looks like a pager that failed to load rather than one
               with nowhere to go. Being disabled is what says that. */
            if ($pagerText) {
                $pagerText.text(
                    state.pageNo +
                    " " +
                    lbl(
                        "VAS_Of",
                        "of"
                    ) +
                    " " +
                    Math.max(1, state.totalPages)
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
                    state.pageNo >= state.totalPages
                );
            }
        }

        function updateTabCounts() {
            if ($tabAllocatedCount) {
                $tabAllocatedCount.text(
                    tabState.allocated.totalRecords
                );
            }

            if ($tabUnallocatedCount) {
                $tabUnallocatedCount.text(
                    tabState.unallocated.totalRecords
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

            activeFilter = filter;
            tabState[filter].pageNo = 1;
            tabState[filter].loaded = false;

            updateActiveTabStyles();
            loadRows();
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

            if ($dialog) {
                $dialog.toggleClass(
                    classPrefix + "filter-unallocated",
                    activeFilter === "unallocated"
                );
            }
        }

        function showState(show, message) {
            if ($state) {
                $state
                    .text(message || "")
                    .toggleClass(
                        "is-visible",
                        Boolean(show)
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
            if ($metricEl) {
                $metricEl.text("");
            }

            tabState.allocated.totalRecords = 0;
            tabState.allocated.totalPages = 0;

            tabState.unallocated.totalRecords = 0;
            tabState.unallocated.totalPages = 0;

            updateTabCounts();

            showState(
                true,
                lbl(
                    "VAS_056_MessageNoData",
                    "No Data"
                )
            );
        }

        function formatPercent(value) {
            return Number(
                value || 0
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }
            ) + "%";
        }

        function formatAmount(value) {
            return Number(
                value || 0
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: getStdPrecision(),
                    maximumFractionDigits: getStdPrecision()
                }
            );
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            try {
                if (
                    VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env.getCtx().getStdPrecision
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

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return value;
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
            var bankName =
                row && row.bankName
                    ? String(row.bankName).trim()
                    : "";

            var accountNo =
                row && row.accountNo
                    ? String(row.accountNo).trim()
                    : "";

            var last4 = "";

            if (accountNo) {
                last4 =
                    accountNo.length > 4
                        ? accountNo.slice(-4)
                        : accountNo;
            }

            if (bankName && last4) {
                return bankName + " ****" + last4;
            }

            if (bankName) {
                return bankName;
            }

            if (last4) {
                return "****" + last4;
            }

            return "";
        }

        function openDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.show();

            $("body").addClass(
                classPrefix + "body-lock"
            );

            updateActiveTabStyles();
            updateTabCounts();

            setupAdaptivePagination();
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            teardownAdaptivePagination();

            $dialog.hide();

            $("body").removeClass(
                classPrefix + "body-lock"
            );

            activeFilter = "allocated";

            tabState.allocated.pageNo = 1;
            tabState.allocated.loaded = false;

            tabState.unallocated.pageNo = 1;
            tabState.unallocated.loaded = false;

            updateActiveTabStyles();
        }

        function createDialog() {
            $dialog = $(
                '<div class="' +
                classPrefix +
                'dialog" style="display:none;" role="dialog" aria-modal="true">' +

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
                'th-partial">' +
                escapeHtml(
                    lbl(
                        "VAS_UnAllocatedAmount",
                        "Un Allocated"
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

            $dialogBody = $dialog.find(
                "." + classPrefix + "dialog-body"
            );

            $dialogTbody = $dialog.find(
                "." + classPrefix + "dialog-tbody"
            );

            $dialogBusy = $dialog.find(
                "." + classPrefix + "dialog-busy"
            );

            showDialogBusy(false);

            $pagerHelper = $dialog.find(
                "." + classPrefix + "pager-helper"
            );

            $pagerPrev = $dialog.find(
                "." + classPrefix + "pager-prev"
            );

            $pagerNext = $dialog.find(
                "." + classPrefix + "pager-next"
            );

            $pagerText = $dialog.find(
                "." + classPrefix + "pager-text"
            );

            $tabAllocated = $dialog.find(
                "." + classPrefix + "tab-allocated"
            );

            $tabUnallocated = $dialog.find(
                "." + classPrefix + "tab-unallocated"
            );

            $tabAllocatedCount = $dialog.find(
                "." + classPrefix + "tab-count-allocated"
            );

            $tabUnallocatedCount = $dialog.find(
                "." + classPrefix + "tab-count-unallocated"
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
                    switchTab("allocated");
                }
            );

            $tabUnallocated.on(
                "click",
                function () {
                    switchTab("unallocated");
                }
            );

            $pagerPrev.on(
                "click",
                function () {
                    var state = tabState[activeFilter];

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
                    var state = tabState[activeFilter];

                    if (
                        rowsLoading ||
                        state.pageNo >= state.totalPages
                    ) {
                        return;
                    }

                    state.pageNo++;
                    state.loaded = false;

                    loadRows();
                }
            );

            $(document).on(
                "keydown.VAS-056-AutoAllocatedAPPayment-" +
                self.AD_UserHomeWidgetID,

                function (event) {
                    if (
                        event.key === "Escape" &&
                        $dialog.is(":visible")
                    ) {
                        closeDialog();
                    }
                }
            );

            $("body").append($dialog);
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

                '<div class="' +
                classPrefix +
                'state-message"></div>' +

                "</div>"
            );

            $body = $card.find(
                "." + classPrefix + "body"
            );

            $footer = $card.find(
                "." + classPrefix + "footer"
            );

            $metricEl = $card.find(
                "." + classPrefix + "value"
            );

            $state = $card.find(
                "." + classPrefix + "state-message"
            );

            $card.on(
                "click",
                openDialog
            );

            $card.on(
                "keydown",
                function (event) {
                    if (
                        event.key === "Enter" ||
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
            tabState.allocated.loaded = false;
            tabState.allocated.pageNo = 1;

            tabState.unallocated.loaded = false;
            tabState.unallocated.pageNo = 1;

            loadKpi();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            teardownAdaptivePagination();

            $(document).off(
                "keydown.VAS-056-AutoAllocatedAPPayment-" +
                this.AD_UserHomeWidgetID
            );

            $("body").removeClass(
                classPrefix + "body-lock"
            );

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            if ($root) {
                $root.remove();
            }

            $metricEl = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            $dialogBody = null;
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
                    frame.widgetInfo.AD_UserHomeWidgetID;
            }

            this.Initalize();

            if (
                this.frame &&
                this.frame.getContentGrid
            ) {
                this.frame
                    .getContentGrid()
                    .append(this.getRoot());
            }
        };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.widgetSizeChange =
        function (height, width) {
            var $root = this.getRoot();

            if (!$root) {
                return;
            }

            $root.toggleClass(
                "VAS-056-AutoAllocatedAPPayment-compact",
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
