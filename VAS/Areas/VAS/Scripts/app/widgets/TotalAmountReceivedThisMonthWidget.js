/**
 * Total Amount Received This Month Widget
 * Purpose - KPI card showing total AR receipts posted in the current month in
 *           base (accounting schema) currency. Clicking the card opens a modal
 *           dialog listing every receipt (Date, Receipt No., Customer, Bank
 *           account, Amount, Payment Currency, Allocated).
 * Design   - Per design.md / widget.html: Onfinity Glass Widget with `tint-info`
 *            KPI shell — 2rem icon well, 13px muted label, ↗ View hint, big bold
 *            primary-blue metric prefixed with the base currency symbol, and a
 *            translucent "POSTED · Customer collections so far" detail pill.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+--------------------------------
 *  1  | Received this month                       | VIS_ReceivedThisMonth
 *  2  | View                                      | VIS_View
 *  3  | Customer collections so far               | VIS_CustomerCollectionsSoFar
 *  4  | All receipts posted in                    | VIS_AllReceiptsPostedIn
 *  5  | Date                                      | VIS_Date
 *  6  | Receipt No.                               | VIS_ReceiptNo
 *  7  | Customer                                  | VIS_Customer
 *  8  | Bank account                              | VIS_BankAccount
 *  9  | Amount                                    | VIS_Amount
 * 10  | Payment Currency                          | VIS_PaymentCurrency
 * 11  | Allocated                                 | VIS_Allocated
 * 12  | No receipts in this period                | VIS_NoReceiptsThisPeriod
 * 13  | Total                                     | VIS_Total
 * 14  | receipt / receipts                        | VIS_Receipt / VIS_Receipts
 * 15  | Close                                     | VIS_Close
 * 16  | Showing                                   | VIS_Showing
 * 17  | of                                        | VIS_Of
 * 18  | Previous                                  | VIS_Previous
 * 19  | Next                                      | VIS_Next
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.TotalAmountReceivedThisMonthWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-tarm-root">');
        var $metricEl;
        var $busy;
        var $dialog;
        var $dialogTbody;
        var $dialogSubtitle;
        var $dialogFooter;
        var $dialogBusy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        /* Latest KPI snapshot (kept so the dialog header/footer can re-use the
           same base-currency symbol and receipt count without a refetch). */
        var lastSymbol = "";
        var lastPeriod = "";
        var lastCount = 0;
        var lastTotal = 0;
        var rowsLoaded = false;
        var rowsLoading = false;

        /* Server-side paging: each page requests fresh rows from the controller
           so the browser never has to hold thousands of months-worth of data. */
        var pageSize = 10;
        var pageNo = 1;
        var totalPages = 0;
        var totalRecords = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'TotalAmountReceivedThisMonth/GetAmountReceivedThisMonth',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;

                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderMetric(data);
                },
                error: function () {
                    setNoData();
                },
                complete: function () { showBusy(false); }
            });
        }

        function loadRows() {
            if (!$dialogTbody) { return; }

            rowsLoading = true;
            showDialogBusy(true);
            /* Disable pager buttons while in flight to prevent a quick double
               click from racing two requests. */
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'TotalAmountReceivedThisMonth/GetReceivedThisMonthRows',
                type: 'GET',
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;

                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    /* Clear the in-flight flag BEFORE renderPageResult so the
                       Prev/Next disabled-state set by updatePagerControls reflects
                       the post-fetch reality, not the still-loading-from-this-fetch
                       state. Otherwise the Next button stays disabled after the
                       initial load and pager clicks appear dead. */
                    rowsLoading = false;
                    showDialogBusy(false);

                    if (data && data.error) {
                        renderPageResult({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo });
                        return;
                    }

                    renderPageResult(data || {});
                    rowsLoaded = true;
                },
                error: function () {
                    rowsLoading = false;
                    showDialogBusy(false);
                    renderPageResult({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo });
                }
            });
        }

        function renderPageResult(data) {
            var rows = (data && data.rows) ? data.rows : [];

            totalRecords = Number(data && data.totalRecords || 0);
            totalPages = Number(data && data.totalPages || 0);

            if (data && typeof data.pageNo !== "undefined") {
                pageNo = Number(data.pageNo);
            }

            /* Snap pageNo back into range if the server returned a different
               page (e.g. last-page deletion shrinking the total). */
            if (pageNo > totalPages && totalPages > 0) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            renderRows(rows);

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);

            updatePagerControls(from, to);
        }

        function setNoData() {
            lastTotal = 0;
            lastCount = 0;

            if ($metricEl) {
                $metricEl.text(formatMetric(0, lastSymbol));
            }
        }

        /* Compact-amount formatter: 10M→Cr (Indian crore), 100K→L (lakh),
           1K→K. Falls back to locale formatting with the std precision for
           amounts under 1000 so cash registers still read naturally. */
        function formatCompactAmount(value) {
            value = Number(value || 0);

            if (value >= 10000000) {
                return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }

            if (value >= 100000) {
                return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }

            if (value >= 1000) {
                return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }

            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function formatExactAmount(value) {
            var num = Number(value || 0);
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return num.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        /* Place the currency symbol *before* the amount, sign before symbol
           when negative (e.g. -₹1.2M). Matches design.md and image_2.png. */
        function formatMetric(value, symbol) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var compact = formatCompactAmount(Math.abs(value));
            var sym = symbol ? '<span class="vas-tarm-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + compact;
        }

        function renderMetric(data) {
            var total = (data && (data.totalAmountReceivedThisMonth || data.totalAmountReceivedJanuary)) || 0;
            var sym = (data && data.symbol) || "";
            var period = (data && data.period) || "";
            var count = (data && data.receiptCount) || 0;

            lastTotal = Number(total);
            lastSymbol = sym;
            lastPeriod = period;
            lastCount = Number(count);

            if ($metricEl) {
                $metricEl.html(formatMetric(lastTotal, lastSymbol));
            }

            if ($dialogSubtitle) {
                $dialogSubtitle.text(lbl("VIS_AllReceiptsPostedIn", "All receipts posted in") + ' ' + lastPeriod);
            }

            /* If the dialog is already open (e.g. data refresh while drilled
               down), refresh the rows so the period footer stays in sync. */
            if ($dialog && $dialog.is(':visible')) {
                loadRows();
            }
        }

        function formatDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, {
                day: "2-digit",
                month: "short",
                year: "numeric"
            });
        }

        function renderRows(rows) {
            if (!$dialogTbody) { return; }
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-tarm-dialog-empty" colspan="7">' +
                    lbl("VIS_NoReceiptsThisPeriod", "No receipts in this period") +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.date);
                var sym = row.paymentCurrencySymbol || row.paymentCurrency || "";
                var docNo = row.documentNo || "";
                var customer = row.customer || "";
                var bankText = formatBankAccount(row);
                var currencyCode = row.paymentCurrency || "";
                var amountText = formatExactAmount(row.amount);
                var allocatedLabel = row.allocated
                    ? lbl("VIS_Allocated", "Allocated")
                    : lbl("VIS_NotAllocated", "Not allocated");

                var allocatedCell = row.allocated
                    ? '<span class="vas-tarm-check vas-tarm-check-on" aria-label="' + escapeHtml(allocatedLabel) + '">' +
                      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>' +
                      '</span>'
                    : '<span class="vas-tarm-check vas-tarm-check-off" aria-label="' + escapeHtml(allocatedLabel) + '"></span>';

                /* Every cell gets a `title` attribute carrying its full value so
                   hovering reveals anything the column's fixed width has clipped
                   with `text-overflow: ellipsis`. */
                var $tr = $(
                    '<tr>' +
                    '<td class="vas-tarm-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-tarm-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-tarm-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-tarm-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-tarm-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-tarm-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-tarm-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-tarm-td-currency" title="' + escapeHtml(currencyCode) + '">' + escapeHtml(currencyCode) + '</td>' +
                    '<td class="vas-tarm-td-amount" title="' + escapeHtml(amountText) + '">' + escapeHtml(amountText) + '</td>' +
                    '<td class="vas-tarm-td-allocated" title="' + escapeHtml(allocatedLabel) + '">' + allocatedCell + '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
            }
        }

        /* Render bank info as "<Bank Name> · ****<last-4 of account>" when both
           parts are known; degrade gracefully when one is missing. */
        function formatBankAccount(row) {
            var bankName = (row && row.bankName) ? String(row.bankName).trim() : "";
            var accountNo = (row && row.accountNo) ? String(row.accountNo).trim() : "";

            var last4 = "";
            if (accountNo) {
                last4 = accountNo.length > 4 ? accountNo.slice(-4) : accountNo;
            }

            if (bankName && last4) {
                return bankName + ' · ****' + last4;
            }

            if (bankName) { return bankName; }
            if (last4) { return '****' + last4; }
            return "";
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var rcptLabel = totalRecords === 1
                        ? lbl("VIS_Receipt", "receipt")
                        : lbl("VIS_Receipts", "receipts");

                    $pagerHelper.text(
                        lbl("VIS_Showing", "Showing") + ' ' +
                        (from + 1) + '–' + to + ' ' +
                        lbl("VIS_Of", "of") + ' ' + totalRecords + ' ' + rcptLabel
                    );
                }
                else {
                    $pagerHelper.text("");
                }
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? (pageNo + ' ' + lbl("VIS_Of", "of") + ' ' + totalPages) : "");
            }

            if ($pagerPrev) {
                $pagerPrev.prop("disabled", rowsLoading || pageNo <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop("disabled", rowsLoading || totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('vas-tarm-body-lock');

            if (!rowsLoaded) {
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-tarm-body-lock');

            /* Reset paging state so the next openDialog() starts at page 1 with
               a fresh fetch instead of remembering where the user left off. */
            pageNo = 1;
            rowsLoaded = false;
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-tarm-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-tarm-dialog-scrim"></div>' +
                '<div class="vas-tarm-dialog-card">' +

                '<div class="vas-tarm-dialog-header">' +
                '<div class="vas-tarm-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/>' +
                '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-tarm-dialog-title-group">' +
                '<div class="vas-tarm-dialog-title">' + lbl("VIS_ReceivedThisMonth", "Received this month") + '</div>' +
                '<div class="vas-tarm-dialog-subtitle">' + lbl("VIS_AllReceiptsPostedIn", "All receipts posted in") + '</div>' +
                '</div>' +
                '<button type="button" class="vas-tarm-dialog-close" aria-label="' + escapeHtml(lbl("VIS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-tarm-dialog-body">' +
                '<div class="vas-tarm-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-tarm-dialog-table">' +
                /* Headers carry `title` too, so e.g. a clipped "Paymen…" surfaces
                   "Payment Currency" on hover (matches the cell title pattern). */
                '<thead>' +
                '<tr>' +
                '<th class="vas-tarm-th-date" title="' + escapeHtml(lbl("VIS_Date", "Date")) + '">' + lbl("VIS_Date", "Date") + '</th>' +
                '<th class="vas-tarm-th-doc" title="' + escapeHtml(lbl("VIS_ReceiptNo", "Receipt No.")) + '">' + lbl("VIS_ReceiptNo", "Receipt No.") + '</th>' +
                '<th class="vas-tarm-th-customer" title="' + escapeHtml(lbl("VIS_Customer", "Customer")) + '">' + lbl("VIS_Customer", "Customer") + '</th>' +
                '<th class="vas-tarm-th-bank" title="' + escapeHtml(lbl("VIS_BankAccount", "Bank account")) + '">' + lbl("VIS_BankAccount", "Bank account") + '</th>' +
                '<th class="vas-tarm-th-currency" title="' + escapeHtml(lbl("VIS_PaymentCurrency", "Payment Currency")) + '">' + lbl("VIS_PaymentCurrency", "Payment Currency") + '</th>' +
                '<th class="vas-tarm-th-amount" title="' + escapeHtml(lbl("VIS_Amount", "Amount")) + '">' + lbl("VIS_Amount", "Amount") + '</th>' +
                '<th class="vas-tarm-th-allocated" title="' + escapeHtml(lbl("VIS_Allocated", "Allocated")) + '">' + lbl("VIS_Allocated", "Allocated") + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody class="vas-tarm-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                /* Footer pager (design.md §Widget Footer Pager): left helper + right compact pager. */
                '<div class="vas-tarm-dialog-footer">' +
                '<span class="vas-tarm-pager-helper"></span>' +
                '<div class="vas-tarm-pager">' +
                '<button type="button" class="vas-tarm-pager-btn vas-tarm-pager-prev" aria-label="' + escapeHtml(lbl("VIS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-tarm-pager-text"></span>' +
                '<button type="button" class="vas-tarm-pager-btn vas-tarm-pager-next" aria-label="' + escapeHtml(lbl("VIS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-tarm-dialog-tbody');
            $dialogSubtitle = $dialog.find('.vas-tarm-dialog-subtitle');
            $dialogFooter = $dialog.find('.vas-tarm-dialog-footer');
            $dialogBusy = $dialog.find('.vas-tarm-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-tarm-pager-helper');
            $pagerPrev = $dialog.find('.vas-tarm-pager-prev');
            $pagerNext = $dialog.find('.vas-tarm-pager-next');
            $pagerText = $dialog.find('.vas-tarm-pager-text');

            $dialog.find('.vas-tarm-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-tarm-dialog-scrim').on('click', function () {
                closeDialog();
            });

            $pagerPrev.on('click', function () {
                if (rowsLoading || pageNo <= 1) { return; }
                pageNo--;
                loadRows();
            });

            $pagerNext.on('click', function () {
                if (rowsLoading || pageNo >= totalPages) { return; }
                pageNo++;
                loadRows();
            });

            $(document).on('keydown.vas-tarm', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            /* Attach the dialog to <body> so its fixed positioning escapes any
               transformed dashboard ancestor (which would otherwise become the
               containing block for `position: fixed`). */
            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-tarm-card" role="button" tabindex="0">' +

                '<div class="vas-tarm-head">' +
                '<div class="vas-tarm-head-left">' +
                '<div class="vas-tarm-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/>' +
                '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>' +
                '</svg>' +
                '</div>' +
                '<span class="vas-tarm-label">' + lbl("VIS_ReceivedThisMonth", "Received this month") + '</span>' +
                '</div>' +
                '<button type="button" class="vas-tarm-view">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M7 17 17 7"/><path d="M7 7h10v10"/>' +
                '</svg>' +
                '<span>' + lbl("VIS_View", "View") + '</span>' +
                '</button>' +
                '</div>' +

                '<div class="vas-tarm-metric">—</div>' +

                '<span class="vas-tarm-detail-text">' +
                lbl("VIS_CustomerCollectionsSoFar", "Customer collections so far") +
                '</span>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-tarm-metric');

            $card.on('click', function () {
                openDialog();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            /* The view link is a child of the clickable card — let it propagate. */
            $card.find('.vas-tarm-view').on('click', function (e) {
                e.stopPropagation();
                openDialog();
            });

            $root.append($card);

            $busy = $('<div class="vas-tarm-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            createDialog();
        }

        this.refreshWidget = function () {
            rowsLoaded = false;
            pageNo = 1;
            loadKpi();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-tarm');
            $('body').removeClass('vas-tarm-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();
        };
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);
