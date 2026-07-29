; VAS = window.VAS || {};

/**
 * Cleared AP Payment Widget
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+------------------------------
 *  1  | Unreconciled                                      | VAS_027_messageCleared
 *  2  | Of last month                                     | VAS_027_messageAPPaymentClearedWhy
 *  3  | No Data                                           | VAS_027_messageNoData
 *  4  | payments                                          | VAS_027_messagePayments
 *  5  | awaiting bank match                               | VAS_027_messageAwaitingBankMatch
 *  6  | days                                              | VAS_027_messageDays
 *  7  | No unreconciled payments                          | VAS_027_NoUnreconciledPayments
 *  8  | Showing                                           | VAS_Showing
 *  9  | of                                                | VAS_Of
 * 10  | Unreconciled payments                             | VAS_027_UnreconciledPayments
 * 11  | Auto-match process is not configured              | VAS_027_AutoMatchProcessNotConfigured
 * 12  | Payment reconciliation window is not configured    | VAS_027_ReconciliationWindowNotConfigured
 * 13  | Could not open action                             | VAS_ErrorLoading
 * 14  | Unreconciled (summary label)                      | VAS_027_messageUnreconciled
 * 15  | Amount (summary label)                            | VAS_027_messageAmount
 * 16  | Oldest (summary label)                            | VAS_027_messageOldest
 * 17  | Payment No. (table header)                        | VAS_027_messagePaymentNo
 * 18  | Date (table header)                               | VAS_027_messageDate
 * 19  | Vendor (table header)                             | VAS_027_messageVendor
 * 20  | Bank Account (table header)                       | VAS_027_messageBankAccount
 * 21  | Currency (table header)                           | VAS_027_messageCurrency
 * 22  | Method (table header)                             | VAS_027_messageMethod
 * 23  | Auto-match remaining (button)                     | VAS_027_messageAutoMatchRemaining
 * 24  | Open reconciliation (button)                      | VAS_027_messageOpenReconciliation
 * ──────────────────────────────────────────────────────────────────────────────
 */

; (function (VAS, $) {
    "use strict";

    var VAS_027_ClearedAPPaymentWidget = function () {
        var self = this;

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        this.autoMatchProcessId = 0;
        this.reconciliationWindowId = 0;

        var $root = $(
            '<div class="vas-finance-kpi-root">'
        );

        var $card = null;
        var $value = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;

        var $dialog = null;
        var $dialogBody = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $dialogSubtitle = null;

        var $summaryCount = null;
        var $summaryAmount = null;
        var $summaryOldest = null;
        var $summaryRate = null;

        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var isDisposed = false;
        var rowsLoaded = false;
        var rowsLoading = false;

        var pageNo = 1;
        var pageSize = 8;
        var totalPages = 0;
        var totalRecords = 0;

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

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== '[' + key + ']'
                ? text
                : fallback;
        }

        function normalizeResponse(response) {
            if (typeof response !== 'string') {
                return response;
            }

            try {
                return JSON.parse(response);
            }
            catch (error) {
                return null;
            }
        }

        function getResponseMessage(
            data,
            fallback
        ) {
            var key;

            if (!data) {
                return fallback;
            }

            key =
                data.errorKey ||
                data.messageKey;

            if (key) {
                return lbl(
                    key,
                    data.errorText ||
                    data.error ||
                    data.message ||
                    fallback
                );
            }

            return (
                data.errorText ||
                data.error ||
                data.message ||
                fallback
            );
        }

        function normalizePrecision(value) {
            var precision = Number(value);

            if (
                isNaN(precision) ||
                precision < 0
            ) {
                return 2;
            }

            return precision;
        }

        function escapeHtml(value) {
            return String(
                value == null ? '' : value
            )
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function formatCount(value) {
            return Number(
                value || 0
            ).toLocaleString(
                window.navigator.language
            );
        }

        function formatPercent(
            value
        ) {
            return Number(
                value || 0
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits:
                        2,

                    maximumFractionDigits:
                        2
                }
            ) + '%';
        }

        /*
         * Amounts shown in the dialog header are rounded to whole
         * numbers and the currency symbol is printed without the
         * trailing separator that comes from the currency setup,
         * for example ID12,000. Table cells keep the exact amount.
         */
        function formatHeaderAmount(
            value,
            symbol
        ) {
            var numericValue = Number(value || 0);

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            var sign =
                numericValue < 0 ? '-' : '';

            var numberText = Math.abs(
                numericValue
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 0
                }
            );

            var cleanSymbol = String(
                symbol || ''
            ).replace(/[\s-]+$/, '');

            return sign + cleanSymbol + numberText;
        }

        function formatExactAmount(
            value,
            precision,
            symbol
        ) {
            var stdPrecision =
                normalizePrecision(precision);

            var numericValue = Number(value || 0);

            if (isNaN(numericValue)) {
                numericValue = 0;
            }

            var sign =
                numericValue < 0 ? '-' : '';

            var numberText = Math.abs(
                numericValue
            ).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits:
                        stdPrecision,

                    maximumFractionDigits:
                        stdPrecision
                }
            );

            return sign + (
                symbol ? symbol : ''
            ) + numberText;
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(
                window.navigator.language,
                {
                    day: '2-digit',
                    month: 'short',
                    year: 'numeric'
                }
            );
        }

        function formatBankAccount(row) {
            var bankName =
                row && row.bankName
                    ? String(
                        row.bankName
                    ).trim()
                    : '';

            var accountNo =
                row && row.accountNo
                    ? String(
                        row.accountNo
                    ).trim()
                    : '';

            var last4 = accountNo
                ? accountNo.length > 4
                    ? accountNo.slice(-4)
                    : accountNo
                : '';

            if (bankName && last4) {
                return bankName +
                    ' ****' +
                    last4;
            }

            if (bankName) {
                return bankName;
            }

            return last4
                ? '****' + last4
                : '';
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass(
                    'is-visible',
                    Boolean(show)
                );
            }
        }

        function showDialogBusy(show) {
            if ($dialogBusy) {
                $dialogBusy.toggleClass(
                    'is-visible',
                    Boolean(show)
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

        function showActionError(message) {
            if (
                VIS.ADialog &&
                typeof VIS.ADialog.error ===
                'function'
            ) {
                VIS.ADialog.error(message);
                return;
            }

            if (
                VIS.ADialog &&
                typeof VIS.ADialog.info ===
                'function'
            ) {
                VIS.ADialog.info(message);
            }
        }

        function startMenuAction(
            actionType,
            actionId
        ) {
            var id = Number(actionId);

            if (
                isNaN(id) ||
                id <= 0
            ) {
                return false;
            }

            try {
                if (
                    VIS.viewManager &&
                    typeof VIS.viewManager
                        .startAction ===
                    'function'
                ) {
                    VIS.viewManager.startAction(
                        actionType,
                        id
                    );

                    return true;
                }

                if (
                    VIS.ViewManager &&
                    typeof VIS.ViewManager
                        .startAction ===
                    'function'
                ) {
                    VIS.ViewManager.startAction(
                        actionType,
                        id
                    );

                    return true;
                }
            }
            catch (error) {
                showActionError(
                    error.message ||
                    lbl(
                        'VAS_ErrorLoading',
                        'Could not open action'
                    )
                );

                return true;
            }

            return false;
        }

        function createWidget() {
            $card = $(
                '<div class="vas-finance-kpi-card" ' +
                'role="button" tabindex="0">'
            );

            var $header = $(
                '<div class="vas-finance-kpi-header">'
            );

            var $iconBox = $(
                '<div class="vas-finance-kpi-icon-box">'
            );

            var $icon = $(
                '<svg class="vas-finance-kpi-icon" ' +
                'viewBox="0 0 24 24" ' +
                'aria-hidden="true">' +
                '<path fill="currentColor" ' +
                'd="M9.2 16.6 4.95 12.35 ' +
                '6.36 10.94 9.2 13.77 ' +
                '17.64 5.34 19.05 6.75z">' +
                '</path>' +
                '</svg>'
            );

            var $title = $(
                '<div class="vas-finance-kpi-title">'
            ).text(
                lbl(
                    'VAS_027_messageCleared',
                    'Unreconciled'
                )
            );

            $iconBox.append($icon);

            $header
                .append($iconBox)
                .append($title);

            $body = $(
                '<div class="vas-finance-kpi-body">'
            );

            $value = $(
                '<div class="vas-finance-kpi-value">'
            );

            $body.append($value);

            $footer = $(
                '<div class="vas-finance-kpi-footer">'
            );

            var $description = $(
                '<div class="vas-finance-kpi-desc">'
            ).text(
                lbl(
                    'VAS_027_messageAPPaymentClearedWhy',
                    "Of last month's AP payments reconciled"
                )
            );

            $footer.append($description);

            $busy = $(
                '<div class="vas-finance-kpi-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $state = $(
                '<div class="vas-finance-kpi-state-message">'
            );

            $card
                .append($header)
                .append($body)
                .append($footer)
                .append($busy)
                .append($state);

            $card.on('click', function () {
                openDialog();
            });

            $card.on(
                'keydown',
                function (event) {
                    if (
                        event.key === 'Enter' ||
                        event.key === ' '
                    ) {
                        event.preventDefault();
                        openDialog();
                    }
                }
            );

            $root
                .empty()
                .append($card);

            createDialog();
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
                    'VAS_027_ClearedAPPaymentWidget/' +
                    'GetClearedAPPayment',

                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(response);

                    if (
                        !data ||
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

                error: function (
                    xhr,
                    status,
                    error
                ) {
                    if (isDisposed) {
                        return;
                    }

                    showState(
                        true,
                        error ||
                        lbl(
                            'VAS_ErrorLoading',
                            'Could not load data'
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
            var percentage =
                Number(data.value);
            var totalPayments =
                Number(data.totalPayments);

            if (isNaN(percentage)) {
                percentage = Number(
                    data.clearedPercentage
                );
            }

            if (
                isNaN(percentage) ||
                isNaN(totalPayments) ||
                totalPayments <= 0
            ) {
                showState(
                    true,
                    lbl(
                        'VAS_027_messageNoData',
                        'No Data'
                    )
                );

                return;
            }

            percentage = Math.max(
                0,
                Math.min(
                    percentage,
                    100
                )
            );

            showState(false, '');

            $value.text(
                formatPercent(
                    percentage,
                    data.precision
                )
            );
        }

        function measureDialogRowHeight() {
            var $row = $dialogTbody
                ? $dialogTbody
                    .find('tr')
                    .not(':has(.vas-cpa-dialog-empty)')
                    .first()
                : null;

            var measured =
                $row && $row.length
                    ? $row.outerHeight()
                    : 0;

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
                ((pageNo - 1) * pageSize) + 1;

            pageSize = nextPageSize;

            pageNo = Math.max(
                1,
                Math.ceil(firstVisibleRecord / pageSize)
            );

            loadRows();
        }

        function setupAdaptivePagination() {
            if (!$dialogBody || !$dialogBody[0]) {
                return;
            }

            updateAdaptivePageSize();

            window.setTimeout(function () {
                updateAdaptivePageSize();
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
            if (
                isDisposed ||
                !$dialogTbody ||
                rowsLoading
            ) {
                return;
            }

            rowsLoading = true;

            showDialogBusy(true);
            updatePagerButtons();

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_027_ClearedAPPaymentWidget/' +
                    'GetUnreconciledAPPayments',

                type: 'GET',
                dataType: 'json',
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
                        normalizeResponse(response);

                    if (
                        !data ||
                        data.error
                    ) {
                        renderErrorRow(
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

                    renderPageResult(data);
                    rowsLoaded = true;
                },

                error: function (
                    xhr,
                    status,
                    error
                ) {
                    if (!isDisposed) {
                        renderErrorRow(
                            error ||
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );
                    }
                },

                complete: function () {
                    if (!isDisposed) {
                        rowsLoading = false;

                        showDialogBusy(false);
                        updateAdaptivePageSize();
                        updatePagerButtons();
                    }
                }
            });
        }

        function renderErrorRow(message) {
            totalRecords = 0;
            totalPages = 0;

            $dialogTbody.html(
                '<tr>' +
                '<td class="vas-cpa-dialog-empty" ' +
                'colspan="8">' +
                escapeHtml(message) +
                '</td>' +
                '</tr>'
            );

            updatePager();
        }

        function renderPageResult(data) {
            var rows =
                data && data.rows
                    ? data.rows
                    : [];

            totalRecords = Number(
                data.totalRecords || 0
            );

            totalPages = Number(
                data.totalPages || 0
            );

            if (
                typeof data.pageNo !==
                'undefined'
            ) {
                pageNo = Number(
                    data.pageNo
                );
            }

            if (
                totalPages > 0 &&
                pageNo > totalPages
            ) {
                pageNo = totalPages;
            }

            if (pageNo < 1) {
                pageNo = 1;
            }

            renderSummary(data);
            renderRows(rows);
            updatePager();
        }

        function renderSummary(data) {
            var symbol =
                data.curSymbol ||
                data.currencyIso ||
                '';

            var totalAmountText =
                formatHeaderAmount(
                    data.totalAmount,
                    symbol
                );

            var oldestDays = Number(
                data.oldestDays || 0
            );

            var autoMatchRate = Number(
                data.autoMatchRate || 0
            );

            $dialogSubtitle.text(
                formatCount(totalRecords) +
                ' ' +
                lbl(
                    'VAS_027_messagePayments',
                    'payments'
                ) +
                ' - ' +
                totalAmountText +
                ' ' +
                lbl(
                    'VAS_027_messageAwaitingBankMatch',
                    'awaiting bank match'
                )
            );

            $summaryCount.text(
                formatCount(totalRecords)
            );

            $summaryAmount.text(
                totalAmountText
            );

            $summaryOldest.text(
                oldestDays > 0
                    ? oldestDays +
                    ' ' +
                    lbl(
                        'VAS_027_messageDays',
                        'days'
                    )
                    : '0'
            );

            $summaryRate.text(
                formatPercent(
                    autoMatchRate,
                    0
                )
            );
        }

        function renderRows(rows) {
            $dialogTbody.empty();

            if (
                !rows ||
                rows.length === 0
            ) {
                $dialogTbody.html(
                    '<tr>' +
                    '<td class="vas-cpa-dialog-empty" ' +
                    'colspan="8">' +
                    escapeHtml(
                        lbl(
                            'VAS_027_NoUnreconciledPayments',
                            'No unreconciled payments'
                        )
                    ) +
                    '</td>' +
                    '</tr>'
                );

                return;
            }

            $.each(
                rows,
                function (index, row) {
                    var dateText =
                        formatDate(row.date);

                    var paymentNo =
                        row.paymentNo || '';

                    var vendor =
                        row.vendor || '';

                    var bankText =
                        formatBankAccount(row);

                    var currencyIso =
                        row.currencyIso || '';

                    var stdPrecision =
                        normalizePrecision(
                            row.stdPrecision
                        );

                    var amountText =
                        formatExactAmount(
                            row.amount,
                            stdPrecision,
                            row.curSymbol ||
                            currencyIso
                        );

                    var method =
                        row.method || '';

                    var reason =
                        row.whyUnreconciled || '';

                    $dialogTbody.append(
                        '<tr>' +

                        '<td class="vas-cpa-td-doc" ' +
                        'title="' +
                        escapeHtml(paymentNo) +
                        '">' +
                        '<span class="vas-cpa-truncate">' +
                        escapeHtml(paymentNo) +
                        '</span>' +
                        '</td>' +

                        '<td class="vas-cpa-td-date" ' +
                        'title="' +
                        escapeHtml(dateText) +
                        '">' +
                        escapeHtml(dateText) +
                        '</td>' +

                        '<td class="vas-cpa-td-vendor" ' +
                        'title="' +
                        escapeHtml(vendor) +
                        '">' +
                        '<span class="vas-cpa-truncate">' +
                        escapeHtml(vendor) +
                        '</span>' +
                        '</td>' +

                        '<td class="vas-cpa-td-bank" ' +
                        'title="' +
                        escapeHtml(bankText) +
                        '">' +
                        '<span class="vas-cpa-truncate">' +
                        escapeHtml(bankText) +
                        '</span>' +
                        '</td>' +

                        '<td class="vas-cpa-td-currency">' +
                        escapeHtml(currencyIso) +
                        '</td>' +


                        '<td class="vas-cpa-td-method">' +
                        escapeHtml(method) +
                        '</td>' +


                        '<td class="vas-cpa-td-amount">' +
                        escapeHtml(amountText) +
                        '</td>' +

                        '</tr>'
                    );
                }
            );
        
            updateAdaptivePageSize();
        }

        function updatePager() {
            var loadedRows =
                $dialogTbody
                    ? $dialogTbody
                        .find('tr')
                        .not(
                            ':has(.vas-cpa-dialog-empty)'
                        )
                        .length
                    : 0;

            var from =
                totalRecords === 0
                    ? 0
                    : (
                        pageNo - 1
                    ) * pageSize + 1;

            var to = Math.min(
                (
                    pageNo - 1
                ) * pageSize +
                loadedRows,
                totalRecords
            );

            $pagerHelper.text(
                totalRecords > 0
                    ? lbl(
                        'VAS_Showing',
                        'Showing'
                    ) +
                    ' ' +
                    from +
                    '-' +
                    to +
                    ' ' +
                    lbl(
                        'VAS_Of',
                        'of'
                    ) +
                    ' ' +
                    totalRecords
                    : ''
            );

            $pagerText.text(
                totalPages > 0
                    ? pageNo +
                    ' ' +
                    lbl(
                        'VAS_Of',
                        'of'
                    ) +
                    ' ' +
                    totalPages
                    : ''
            );

            updatePagerButtons();
        }

        function updatePagerButtons() {
            $pagerPrev.prop(
                'disabled',
                rowsLoading ||
                pageNo <= 1
            );

            $pagerNext.prop(
                'disabled',
                rowsLoading ||
                totalPages <= 1 ||
                pageNo >= totalPages
            );
        }

        function openDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.show();

            $('body').addClass(
                'vas-cpa-body-lock'
            );

            setupAdaptivePagination();

            if (!rowsLoaded) {
                pageNo = 1;
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            teardownAdaptivePagination();

            $dialog.hide();

            $('body').removeClass(
                'vas-cpa-body-lock'
            );
        }

        function reconcileIconSvg() {
            return (
                '<svg viewBox="0 0 24 24" ' +
                'fill="none" stroke="currentColor" ' +
                'stroke-width="1.8" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<polyline points="23 4 23 10 17 10">' +
                '</polyline>' +

                '<polyline points="1 20 1 14 7 14">' +
                '</polyline>' +

                '<path d="M3.51 9a9 9 0 0 1 ' +
                '14.85-3.36L23 10M1 14l4.64 ' +
                '4.36A9 9 0 0 0 20.49 15">' +
                '</path>' +

                '</svg>'
            );
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-cpa-dialog" ' +
                'style="display:none;" ' +
                'role="dialog" aria-modal="true">' +

                '<div class="vas-cpa-dialog-scrim">' +
                '</div>' +

                '<div class="vas-cpa-dialog-card">' +

                '<div class="vas-cpa-dialog-header">' +

                '<div class="vas-cpa-dialog-icon">' +
                reconcileIconSvg() +
                '</div>' +

                '<div class="vas-cpa-dialog-title-group">' +

                '<div class="vas-cpa-dialog-title">' +
                escapeHtml(
                    lbl(
                        'VAS_027_UnreconciledPayments',
                        'Unreconciled payments'
                    )
                ) +
                '</div>' +

                '<div class="vas-cpa-dialog-subtitle">' +
                '</div>' +

                '</div>' +

                '<button type="button" ' +
                'class="vas-cpa-dialog-close">' +
                '<svg viewBox="0 0 24 24" ' +
                'fill="none" stroke="currentColor" ' +
                'stroke-width="1.8">' +
                '<line x1="18" y1="6" ' +
                'x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" ' +
                'x2="18" y2="18"></line>' +
                '</svg>' +
                '</button>' +

                '</div>' +

                '<div class="vas-cpa-summary">' +

                '<div>' +
                '<span>' + lbl('VAS_027_messageUnreconciled', 'Unreconciled') + '</span>' +
                '<strong class="vas-cpa-summary-count">' +
                '0' +
                '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + lbl('VAS_027_messageAmount', 'Amount') + '</span>' +
                '<strong class="vas-cpa-summary-amount">' +
                '0' +
                '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + lbl('VAS_027_messageOldest', 'Oldest') + '</span>' +
                '<strong class="vas-cpa-summary-oldest">' +
                '0' +
                '</strong>' +
                '</div>' +


                '</div>' +

                '<div class="vas-cpa-dialog-body">' +

                '<div class="vas-cpa-dialog-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>' +

                '<table class="vas-cpa-dialog-table">' +

                '<thead>' +
                '<tr>' +
                '<th>' + lbl('VAS_027_messagePaymentNo', 'Payment No.') + '</th>' +
                '<th>' + lbl('VAS_027_messageDate', 'Date') + '</th>' +
                '<th>' + lbl('VAS_027_messageVendor', 'Vendor') + '</th>' +
                '<th>' + lbl('VAS_027_messageBankAccount', 'Bank Account') + '</th>' +
                '<th>' + lbl('VAS_027_messageCurrency', 'Currency') + '</th>' +
                '<th>' + lbl('VAS_027_messageMethod', 'Method') + '</th>' +
                '<th>' + lbl('VAS_027_messageAmount', 'Amount') + '</th>' +
                '</tr>' +
                '</thead>' +

                '<tbody class="vas-cpa-dialog-tbody">' +
                '</tbody>' +

                '</table>' +

                '</div>' +

                '<div class="vas-cpa-dialog-footer">' +

                '<span class="vas-cpa-pager-helper">' +
                '</span>' +

                '<div class="vas-cpa-dialog-actions">' +

                '<button type="button" ' +
                'class="vas-cpa-action vas-cpa-auto-match">' +
                lbl('VAS_027_messageAutoMatchRemaining', 'Auto-match remaining') +
                '</button>' +

                '<button type="button" ' +
                'class="vas-cpa-action ' +
                'vas-cpa-action-primary ' +
                'vas-cpa-open-reconciliation">' +
                lbl('VAS_027_messageOpenReconciliation', 'Open reconciliation') +
                '</button>' +

                '</div>' +

                '<div class="vas-cpa-pager">' +

                '<button type="button" ' +
                'class="vas-cpa-pager-btn ' +
                'vas-cpa-pager-prev">' +
                '&#10094;' +
                '</button>' +

                '<span class="vas-cpa-pager-text">' +
                '</span>' +

                '<button type="button" ' +
                'class="vas-cpa-pager-btn ' +
                'vas-cpa-pager-next">' +
                '&#10095;' +
                '</button>' +

                '</div>' +

                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find(
                '.vas-cpa-dialog-body'
            );

            $dialogTbody = $dialog.find(
                '.vas-cpa-dialog-tbody'
            );

            $dialogBusy = $dialog.find(
                '.vas-cpa-dialog-busy'
            );

            $dialogSubtitle = $dialog.find(
                '.vas-cpa-dialog-subtitle'
            );

            $summaryCount = $dialog.find(
                '.vas-cpa-summary-count'
            );

            $summaryAmount = $dialog.find(
                '.vas-cpa-summary-amount'
            );

            $summaryOldest = $dialog.find(
                '.vas-cpa-summary-oldest'
            );

            $summaryRate = $dialog.find(
                '.vas-cpa-summary-rate'
            );

            $pagerHelper = $dialog.find(
                '.vas-cpa-pager-helper'
            );

            $pagerPrev = $dialog.find(
                '.vas-cpa-pager-prev'
            );

            $pagerNext = $dialog.find(
                '.vas-cpa-pager-next'
            );

            $pagerText = $dialog.find(
                '.vas-cpa-pager-text'
            );

            $dialog.find(
                '.vas-cpa-dialog-close, ' +
                '.vas-cpa-dialog-scrim'
            ).on(
                'click',
                closeDialog
            );

            $pagerPrev.on(
                'click',
                function () {
                    if (
                        rowsLoading ||
                        pageNo <= 1
                    ) {
                        return;
                    }

                    pageNo--;
                    loadRows();
                }
            );

            $pagerNext.on(
                'click',
                function () {
                    if (
                        rowsLoading ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;
                    loadRows();
                }
            );

            $dialog.find(
                '.vas-cpa-auto-match'
            ).on(
                'click',
                function () {
                    var started =
                        startMenuAction(
                            'P',
                            self.autoMatchProcessId
                        );

                    if (!started) {
                        showActionError(
                            lbl(
                                'VAS_027_AutoMatchProcessNotConfigured',
                                'Auto-match process is not configured'
                            )
                        );
                    }
                }
            );

            $dialog.find(
                '.vas-cpa-open-reconciliation'
            ).on(
                'click',
                function () {
                    var started =
                        startMenuAction(
                            'W',
                            self.reconciliationWindowId
                        );

                    if (!started) {
                        showActionError(
                            lbl(
                                'VAS_027_ReconciliationWindowNotConfigured',
                                'Payment reconciliation window is not configured'
                            )
                        );
                    }
                }
            );

            $(document).on(
                'keydown.VAS_027_ClearedAPPaymentWidget',
                function (event) {
                    if (
                        event.key === 'Escape' &&
                        $dialog &&
                        $dialog.is(':visible')
                    ) {
                        closeDialog();
                    }
                }
            );

            $('body').append($dialog);
        }

        this.setActionIds = function (
            processId,
            windowId
        ) {
            self.autoMatchProcessId =
                Number(processId || 0);

            self.reconciliationWindowId =
                Number(windowId || 0);
        };

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshData = function () {
            rowsLoaded = false;
            pageNo = 1;

            loadData();

            if (
                $dialog &&
                $dialog.is(':visible')
            ) {
                loadRows();
            }
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            $(document).off(
                'keydown.VAS_027_ClearedAPPaymentWidget'
            );

            $('body').removeClass(
                'vas-cpa-body-lock'
            );

            teardownAdaptivePagination();

            if ($dialog) {
                $dialog.remove();
            }

            $root.remove();

            $card = null;
            $value = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            $dialog = null;
            $dialogBody = null;
            $dialogTbody = null;
            $dialogBusy = null;
        };
    };

    VAS.VAS_027_ClearedAPPaymentWidget =
        VAS_027_ClearedAPPaymentWidget;

    VAS.VAS_027_ClearedAPPaymentWidget
        .prototype.init = function (
            windowNo,
            frame
        ) {
            this.frame = frame;
            this.windowNo = windowNo;

            var processId = 0;
            var reconciliationWindowId = 0;

            if (
                frame &&
                frame.widgetInfo
            ) {
                this.AD_UserHomeWidgetID =
                    frame.widgetInfo
                        .AD_UserHomeWidgetID ||
                    0;

                processId = Number(
                    frame.widgetInfo
                        .VAS_AutoMatchProcess_ID ||
                    frame.widgetInfo
                        .AD_Process_ID ||
                    0
                );

                reconciliationWindowId =
                    Number(
                        frame.widgetInfo
                            .VAS_ReconciliationWindow_ID ||
                        frame.widgetInfo
                            .AD_Window_ID ||
                        0
                    );
            }

            /*
             * أهم تصحيح:
             * نمرر IDs إلى داخل instance بدل استخدام
             * متغيرات غير موجودة داخل prototype.
             */
            this.setActionIds(
                processId,
                reconciliationWindowId
            );

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

    VAS.VAS_027_ClearedAPPaymentWidget
        .prototype.widgetSizeChange =
        function (height, width) {
            var $root =
                this.getRoot();

            if (!$root) {
                return;
            }

            $root.toggleClass(
                'vas-finance-kpi-compact',
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

    VAS.VAS_027_ClearedAPPaymentWidget
        .prototype.refreshWidget =
        function () {
            this.refreshData();
        };

    VAS.VAS_027_ClearedAPPaymentWidget
        .prototype.dispose =
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
