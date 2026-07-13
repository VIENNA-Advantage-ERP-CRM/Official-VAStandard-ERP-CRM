/**
 * VAS_RecentReceipts Widget
 * Purpose - Glass dashboard card listing the most recent AR receipts of the
 *           last 30 days, server-side paged. Each row shows Date, Receipt No.,
 *           Customer, Method (tender-type chip), Bank account (short form
 *           "HDFC ****4821") and signed Amount (green for incoming, red for
 *           refund). Clicking a row opens the Receipt detail modal: a
 *           two-column Receipt-summary grid + an Allocation table that lists
 *           every posted allocation against the receipt. When the receipt has
 *           no posted allocations the modal shows the on-account hint pill.
 * Design  - Per the supplied reference HTML / design.md (Onfinity glass card
 *           + drill-down modal). All sizes in `em` per CLAUDE.md.
 * Backend - VAS_RecentReceiptsController.GetRecentReceipts  (paged list)
 *           VAS_RecentReceiptsController.GetReceiptAllocations (modal)
 *
 * ── Labels / Message Keys (VAS_ prefix) ───────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Recent receipts                           | VAS_RecentReceipts
 *  2  | Click a row for full detail               | VAS_ClickRowForDetail
 *  3  | Date                                      | VAS_Date
 *  4  | Receipt No.                               | VAS_ReceiptNo
 *  5  | Customer                                  | VAS_Customer
 *  6  | Method                                    | VAS_Method
 *  7  | Bank account                              | VAS_BankAccount
 *  8  | Amount                                    | VAS_Amount
 *  9  | Receipt                                   | VAS_ReceiptDialog
 * 10  | Receipt summary                           | VAS_ReceiptSummary
 * 11  | Receipt date                              | VAS_ReceiptDate
 * 12  | Receipt no.                               | VAS_ReceiptNoLower
 * 13  | Payment method                            | VAS_PaymentMethod
 * 14  | Reference                                 | VAS_Reference
 * 15  | Currency                                  | VAS_Currency
 * 16  | Allocation                                | VAS_Allocation
 * 17  | Invoice No.                               | VAS_InvoiceNo
 * 18  | Invoice date                              | VAS_InvoiceDate
 * 19  | Invoice amount                            | VAS_InvoiceAmount
 * 20  | Allocated amount                          | VAS_AllocatedAmount
 * 21  | invoice / invoices                        | VAS_Invoice / VAS_Invoices
 * 22  | allocated                                 | VAS_AllocatedSuffix
 * 23  | Receipt total                             | VAS_ReceiptTotal
 * 24  | Not allocated to any invoice yet —        | VAS_OnAccountHint
 *     | receipt is on account.                    |
 * 25  | On account                                | VAS_OnAccount
 * 26  | No receipts in this period                | VAS_NoReceiptsThisPeriod
 * 27  | Showing                                   | VAS_Showing
 * 28  | of                                        | VAS_Of
 * 29  | Previous                                  | VAS_Previous
 * 30  | Next                                      | VAS_Next
 * 31  | Close                                     | VAS_Close
 * 32  | receipt / receipts                        | VAS_Receipt / VAS_Receipts
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size
       on :root equal to the dashboard container's current pixel width so the
       title/subtitle clamps resolve against the DASHBOARD's visible content
       area, not the viewport. A single document-level ResizeObserver serves
       every widget (the var is global); without a marked container — or
       without ResizeObserver — the CSS falls back to 100vw. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            /* Spec: write a PIXEL length — calc() needs a length, not a number. */
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    /* Standard Compiere tender-type codes mapped to short display strings.
       Custom values (e.g. UPI / NEFT / RTGS) are passed through unchanged. */
    var TENDER_TYPES = {
        'K': 'Card',
        'A': 'Direct Deposit',
        'D': 'Direct Debit',
        'X': 'Cash',
        'T': 'Transfer',
        'C': 'Cheque',
        'Z': 'Mixed'
    };

    VAS.VAS_RecentReceiptsWidget = function () {

        /* Captured at construction so closures (AJAX callbacks for the
           print button, etc.) can reach the windowNo that init() sets on
           the instance after this constructor returns. */
        var widgetSelf = this;

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-rr-root">');
        var $card;
        var $tbody;
        var $busy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;

        var $dialog;
        var $dialogBusy;
        var $dialogTitle;
        var $dialogSub;
        var $dialogGrid;
        var $dialogAlloc;
        var $dialogPrint;

        /* The C_Payment_ID of the receipt currently open in the modal —
           reused by the Print button to fetch print info and drive APrint. */
        var currentPaymentId = 0;
        var printBusy = false;

        /* Server-paged list state. pageSize is 9 rows per page; pageNo /
           totalPages / totalRecords come from the server response. */
        var pageSize = 9;
        var pageNo = 1;
        var totalPages = 0;
        var totalRecords = 0;
        var rowsLoading = false;
        var dialogLoading = false;

        function lbl(key, fallback) {
            var t = (window.VIS && VIS.Msg) ? VIS.Msg.getMsg(key) : null;
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

        function pick(obj, pascal, camel) {
            if (!obj) { return undefined; }
            if (typeof obj[pascal] !== "undefined") { return obj[pascal]; }
            return obj[camel];
        }

        function formatExactAmount(value, stdPrecision) {
            var num = Number(value || 0);
            var prec = Number(stdPrecision);
            if (isNaN(prec) || prec < 0) { prec = 2; }
            return num.toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
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

        /* Tender-type → display string. Falls back to the raw code so custom
           values (UPI / NEFT / RTGS) render as-is. */
        function methodDisplay(code) {
            if (!code) { return ""; }
            var c = String(code).trim();
            return TENDER_TYPES[c] || c;
        }

        /* Bank account display helpers. The widget table uses the short form
           ("HDFC ****4821") to keep the column narrow; the modal uses the
           full form ("HDFC Bank · ****4821"). */
        function last4(acct) {
            if (!acct) { return ""; }
            var a = String(acct).trim();
            return a.length > 4 ? a.slice(-4) : a;
        }

        function bankShort(bankName, accountNo) {
            var first = String(bankName || "").trim().split(/\s+/)[0] || "";
            var tail = last4(accountNo);
            if (first && tail) { return first + ' ****' + tail; }
            if (first) { return first; }
            if (tail) { return '****' + tail; }
            return "";
        }

        function bankFull(bankName, accountNo) {
            var name = String(bankName || "").trim();
            var tail = last4(accountNo);
            if (name && tail) { return name + ' · ****' + tail; }
            if (name) { return name; }
            if (tail) { return '****' + tail; }
            return "";
        }

        /* Signed amount renderer for the widget table.
              positive / zero             → "+<sym><abs>"  green
              negative (refund / void)   → "-<sym><abs>"  red
           Sign is ASCII '+' or '-', driven from the raw signed value.
           The CSS class is returned alongside so the caller can apply it. */
        function signedAmountHtml(value, stdPrecision, sym) {
            var num = Number(value || 0);
            var isNeg = num < 0;
            var absText = formatExactAmount(Math.abs(num), stdPrecision);
            var symHtml = sym ? '<span class="vas-rr-cur-inline">' + escapeHtml(sym) + '</span>' : '';
            var sign = isNeg ? '-' : '';
            return {
                html: '<span class="vas-rr-amt-sign">' + sign + '</span>' + symHtml + escapeHtml(absText),
                cls: isNeg ? 'vas-rr-td-amount-neg' : 'vas-rr-td-amount-pos',
                title: sign + (sym ? sym + ' ' : '') + absText
            };
        }

        this.Initalize = function () {
            createWidget();
            loadRows();
        };

        /* ── Main card data ─────────────────────────────────────────── */

        function loadRows() {
            rowsLoading = true;
            showBusy(true);
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            var requestedPage = pageNo;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_RecentReceipts/GetRecentReceipts',
                type: 'GET',
                cache: false,
                data: {
                    pageNo: requestedPage,
                    pageSize: pageSize
                },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    rowsLoading = false;
                    showBusy(false);

                    if (data && data.error) {
                        renderPageResult({ Rows: [], TotalRecords: 0, TotalPages: 0, PageNo: requestedPage });
                        return;
                    }

                    renderPageResult(data || {});
                },
                error: function () {
                    rowsLoading = false;
                    showBusy(false);
                    renderPageResult({ Rows: [], TotalRecords: 0, TotalPages: 0, PageNo: requestedPage });
                }
            });
        }

        function renderPageResult(data) {
            var rows = pick(data, "Rows", "rows") || [];
            totalRecords = Number(pick(data, "TotalRecords", "totalRecords") || 0);
            totalPages = Number(pick(data, "TotalPages", "totalPages") || 0);

            var serverPage = pick(data, "PageNo", "pageNo");
            if (typeof serverPage !== "undefined" && serverPage !== null) {
                pageNo = Number(serverPage);
            }

            if (pageNo > totalPages && totalPages > 0) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            renderRows(rows);

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);
            updatePagerControls(from, to);
        }

        function renderRows(rows) {
            if (!$tbody) { return; }
            $tbody.empty();

            if (!rows || rows.length === 0) {
                $tbody.html(
                    '<tr><td class="vas-rr-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_NoReceiptsThisPeriod", "No receipts in this period")) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var paymentId = Number(pick(row, "PaymentId", "paymentId") || 0);
                var dateText = formatDate(pick(row, "DateAcct", "dateAcct"));
                var docNo = pick(row, "DocumentNo", "documentNo") || "";
                var customer = pick(row, "CustomerName", "customerName") || "";
                var method = methodDisplay(pick(row, "TenderType", "tenderType"));
                var bankName = pick(row, "BankName", "bankName") || "";
                var accountNo = pick(row, "AccountNo", "accountNo") || "";
                var bankText = bankShort(bankName, accountNo);
                var stdPrecision = Number(pick(row, "StdPrecision", "stdPrecision") || 2);
                var amount = Number(pick(row, "PayAmount", "payAmount") || 0);
                var sym = pick(row, "CurSymbol", "curSymbol") || pick(row, "CurrencyIso", "currencyIso") || "";
                var amt = signedAmountHtml(amount, stdPrecision, sym);

                var $tr = $(
                    '<tr class="vas-rr-tr" data-payment-id="' + paymentId + '" tabindex="0">' +
                    '<td class="vas-rr-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-rr-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-rr-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-rr-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-rr-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-rr-td-method">' +
                    (method ? '<span class="vas-rr-method-chip" title="' + escapeHtml(method) + '">' + escapeHtml(method) + '</span>' : '') +
                    '</td>' +
                    '<td class="vas-rr-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-rr-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-rr-td-amount ' + amt.cls + '" title="' + escapeHtml(amt.title) + '">' + amt.html + '</td>' +
                    '</tr>'
                );

                $tbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var rcptLabel = totalRecords === 1
                        ? lbl("VAS_Receipt", "receipt")
                        : lbl("VAS_Receipts", "receipts");

                    $pagerHelper.text(
                        lbl("VAS_Showing", "Showing") + ' ' +
                        (from + 1) + '–' + to + ' ' +
                        lbl("VAS_Of", "of") + ' ' + totalRecords + ' ' + rcptLabel
                    );
                }
                else {
                    $pagerHelper.text("");
                }
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? (pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages) : "");
            }

            if ($pagerPrev) {
                $pagerPrev.prop("disabled", rowsLoading || pageNo <= 1);
            }
            if ($pagerNext) {
                $pagerNext.prop("disabled", rowsLoading || totalPages <= 1 || pageNo >= totalPages);
            }
        }

        /* ── Modal: receipt detail + allocation table ───────────────── */

        function openDialog(paymentId) {
            if (!$dialog || !paymentId) { return; }
            currentPaymentId = paymentId;
            $dialog.show();
            $('body').addClass('vas-rr-body-lock');
            if ($dialogPrint) { $dialogPrint.prop("disabled", false); }
            loadAllocations(paymentId);
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-rr-body-lock');
            if ($dialogGrid) { $dialogGrid.empty(); }
            if ($dialogAlloc) { $dialogAlloc.empty(); }
            if ($dialogTitle) { $dialogTitle.text(lbl("VAS_ReceiptDialog", "Receipt")); }
            if ($dialogSub) { $dialogSub.text(""); }
            dialogLoading = false;
            currentPaymentId = 0;
            printBusy = false;
        }

        /* Print: fetch AD_Process_ID + AD_Table_ID for the current receipt
           and hand off to VIS.APrint. The modal busy overlay stays on for
           the whole life of the action — through the AJAX round-trip and the
           startPdf() call — so the user gets feedback until the PDF is
           ready. The busy state is cleared in one of three ways, whichever
           fires first: the APrint callback, a safety timer (so a non-
           callback APrint build still releases the UI), or the next
           print/modal-close cycle. */
        function clearPrintBusy() {
            printBusy = false;
            showDialogBusy(false);
            if ($dialogPrint) { $dialogPrint.prop("disabled", false); }
        }

        function printReceipt() {
            if (printBusy || !currentPaymentId) { return; }
            if (!window.VIS || typeof VIS.APrint !== "function") { return; }

            printBusy = true;
            showDialogBusy(true);
            if ($dialogPrint) { $dialogPrint.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_RecentReceipts/GetPaymentPrintInfo',
                type: 'GET',
                cache: false,
                data: { C_Payment_ID: currentPaymentId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    var processId = Number(pick(data, "AD_Process_ID", "aD_Process_ID") || pick(data, "ad_process_id", "adProcessId") || 0);
                    var tableId = Number(pick(data, "AD_Table_ID", "aD_Table_ID") || pick(data, "ad_table_id", "adTableId") || 0);
                    var recordId = Number(pick(data, "Record_ID", "record_ID") || pick(data, "record_id", "recordId") || currentPaymentId);

                    if (processId <= 0 || tableId <= 0 || recordId <= 0) {
                        clearPrintBusy();
                        if (window.VIS && VIS.ADialog) {
                            VIS.ADialog.info(lbl("VAS_PrintProcessNotFound", "Print process not configured for Payment."));
                        }
                        return;
                    }

                    var widgetWindowNo = widgetSelf && widgetSelf.windowNo ? widgetSelf.windowNo : 0;

                    /* Safety timer — if APrint's startPdf doesn't honour the
                       completion callback (older builds), the busy overlay
                       still releases after a worst-case wait. */
                    var safetyTimer = setTimeout(clearPrintBusy, 30000);

                    /* Signature: new VIS.APrint(AD_Process_ID, AD_Table_ID,
                       Record_ID, windowNo, null). startPdf(cb) opens the
                       generated PDF in a new tab; cb runs once the pipeline
                       returns control. */
                    var prin = new VIS.APrint(processId, tableId, recordId, widgetWindowNo, null);
                    try {
                        prin.startPdf(function () {
                            clearTimeout(safetyTimer);
                            clearPrintBusy();
                        });
                    }
                    catch (e) {
                        /* Older APrint may not accept a callback — fall
                           back to no-arg invocation and rely on the safety
                           timer to release the overlay. */
                        try { prin.startPdf(); } catch (e2) { /* swallow */ }
                    }
                },
                error: function () {
                    clearPrintBusy();
                }
            });
        }

        function loadAllocations(paymentId) {
            if (dialogLoading) { return; }
            dialogLoading = true;
            showDialogBusy(true);

            if ($dialogGrid) { $dialogGrid.empty(); }
            if ($dialogAlloc) { $dialogAlloc.empty(); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_RecentReceipts/GetReceiptAllocations',
                type: 'GET',
                cache: false,
                data: { C_Payment_ID: paymentId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    dialogLoading = false;
                    showDialogBusy(false);

                    if (data && data.error) {
                        renderAllocations({});
                        return;
                    }
                    renderAllocations(data || {});
                },
                error: function () {
                    dialogLoading = false;
                    showDialogBusy(false);
                    renderAllocations({});
                }
            });
        }

        function fieldHtml(label, valueHtml, extraClass) {
            var cls = "vas-rr-d-field" + (extraClass ? (' ' + extraClass) : '');
            return '<div class="' + cls + '">' +
                '<div class="vas-rr-d-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-rr-d-value">' + valueHtml + '</div>' +
                '</div>';
        }

        function renderAllocations(detail) {
            if (!$dialogGrid || !$dialogAlloc) { return; }

            var stdPrecision = Number(pick(detail, "StdPrecision", "stdPrecision") || 2);
            var sym = pick(detail, "CurSymbol", "curSymbol") || pick(detail, "CurrencyIso", "currencyIso") || "";
            var iso = pick(detail, "CurrencyIso", "currencyIso") || "";
            var docNo = pick(detail, "DocumentNo", "documentNo") || "";
            var customer = pick(detail, "CustomerName", "customerName") || "";
            var dateText = formatDate(pick(detail, "DateAcct", "dateAcct"));
            var bankName = pick(detail, "BankName", "bankName") || "";
            var accountNo = pick(detail, "AccountNo", "accountNo") || "";
            var bankLine = bankFull(bankName, accountNo);
            var reference = pick(detail, "Reference", "reference") || "";
            var method = methodDisplay(pick(detail, "TenderType", "tenderType"));
            var payAmountNum = Number(pick(detail, "PayAmount", "payAmount") || 0);
            var payAmountText = formatExactAmount(Math.abs(payAmountNum), stdPrecision);
            var payAmountSign = payAmountNum < 0 ? '-' : '';
            var amountValueClass = payAmountNum < 0 ? 'vas-rr-d-value-amt-neg' : 'vas-rr-d-value-amt';

            /* Title: "Receipt <DocNo>" + customer subtitle, per Image_3. */
            if ($dialogTitle) {
                $dialogTitle.text(lbl("VAS_ReceiptDialog", "Receipt") + (docNo ? ' ' + docNo : ''));
            }
            if ($dialogSub) {
                $dialogSub.text(customer);
            }

            /* RECEIPT SUMMARY grid — 2 columns × 4 rows.
               Field order matches Image_3: date / no, customer / method,
               reference / bank, currency / amount. */
            var symHtml = sym ? '<span class="vas-rr-cur-inline">' + escapeHtml(sym) + '</span>' : '';
            var amountHtml = '<span class="' + amountValueClass + '">' +
                '<span class="vas-rr-amt-sign">' + payAmountSign + '</span>' +
                symHtml + escapeHtml(payAmountText) + '</span>';

            var gridHtml =
                fieldHtml(lbl("VAS_ReceiptDate", "Receipt date"), escapeHtml(dateText)) +
                fieldHtml(lbl("VAS_ReceiptNoLower", "Receipt no."), '<span class="vas-rr-d-value-mono">' + escapeHtml(docNo) + '</span>') +
                fieldHtml(lbl("VAS_Customer", "Customer"), '<strong>' + escapeHtml(customer) + '</strong>') +
                fieldHtml(lbl("VAS_PaymentMethod", "Payment method"), escapeHtml(method)) +
                fieldHtml(lbl("VAS_Reference", "Reference"), '<span class="vas-rr-d-value-mono">' + escapeHtml(reference) + '</span>') +
                fieldHtml(lbl("VAS_BankAccount", "Bank account"), escapeHtml(bankLine)) +
                fieldHtml(lbl("VAS_Currency", "Currency"), escapeHtml(iso)) +
                fieldHtml(lbl("VAS_Amount", "Amount"), amountHtml);

            $dialogGrid.html(gridHtml);

            /* ALLOCATION section. When there are no posted allocations the
               receipt is "on account" — show the dashed hint box + warning
               pill, per Image_3's empty state. */
            $dialogAlloc.empty();
            var lines = pick(detail, "Lines", "lines") || [];

            if (!lines || lines.length === 0) {
                $dialogAlloc.html(
                    '<div class="vas-rr-alloc-empty">' +
                    escapeHtml(lbl("VAS_OnAccountHint", "Not allocated to any invoice yet — receipt is on account.")) +
                    '</div>' +
                    '<span class="vas-rr-onacct-pill">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                    '<circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>' +
                    '</svg>' +
                    escapeHtml(lbl("VAS_OnAccount", "On account")) +
                    '</span>'
                );
                return;
            }

            /* Each allocation line is one of three cases — Invoice / Payment /
               GL Journal. The server has COALESCEd the three sources into a
               generic Ref* triple (RefDocumentNo / RefDocumentDate /
               RefDocumentAmount) so the rendering loop only branches on
               LineType to pick the chip variant and accessibility title. */
            var rowsHtml = "";
            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                var type = String(pick(line, "LineType", "lineType") || "Other");

                /* Fallback to the type-specific fields if the generic Ref*
                   triple is missing (older client / serializer); covers all
                   three cases without forcing one path. */
                var refDoc = pick(line, "RefDocumentNo", "refDocumentNo");
                var refDate = pick(line, "RefDocumentDate", "refDocumentDate");
                var refAmtNum = pick(line, "RefDocumentAmount", "refDocumentAmount");

                if (!refDoc) {
                    if (type === "Payment") {
                        refDoc = pick(line, "RefPaymentDocumentNo", "refPaymentDocumentNo") || "";
                        refDate = refDate || pick(line, "RefPaymentDate", "refPaymentDate");
                        if (refAmtNum == null) { refAmtNum = pick(line, "RefPaymentAmount", "refPaymentAmount"); }
                    }
                    else if (type === "Journal") {
                        refDoc = pick(line, "GLJournalDocumentNo", "glJournalDocumentNo") || "";
                        var lineNo = Number(pick(line, "GLJournalLineNo", "glJournalLineNo") || 0);
                        if (refDoc && lineNo > 0) { refDoc = refDoc + ' / L' + lineNo; }
                        refDate = refDate || pick(line, "GLJournalDate", "glJournalDate");
                        if (refAmtNum == null) { refAmtNum = pick(line, "GLJournalAmount", "glJournalAmount"); }
                    }
                    else {
                        refDoc = pick(line, "InvoiceDocumentNo", "invoiceDocumentNo") || "";
                        refDate = refDate || pick(line, "InvoiceDate", "invoiceDate");
                        if (refAmtNum == null) { refAmtNum = pick(line, "InvoiceGrandTotal", "invoiceGrandTotal"); }
                    }
                }

                /* The Journal line number is informative when the generic
                   Ref* triple was used and the doc number alone doesn't
                   identify the specific line. */
                if (type === "Journal") {
                    var lineNoG = Number(pick(line, "GLJournalLineNo", "glJournalLineNo") || 0);
                    if (refDoc && lineNoG > 0 && refDoc.indexOf('/ L') < 0) {
                        refDoc = refDoc + ' / L' + lineNoG;
                    }
                }

                var refDocText = String(refDoc || "");
                var refDateText = formatDate(refDate);
                var refAmtNumVal = Number(refAmtNum || 0);
                var refAmtSign = refAmtNumVal < 0 ? '-' : '';
                var refAmount = refAmtSign + formatExactAmount(Math.abs(refAmtNumVal), stdPrecision);
                var appliedNum = Number(pick(line, "AppliedAmount", "appliedAmount") || 0);
                var appliedSign = appliedNum < 0 ? '-' : '';
                var applied = appliedSign + formatExactAmount(Math.abs(appliedNum), stdPrecision);

                /* Type chip — INV (blue) / PMT (success-deep) / GL (warning). */
                var chipText = type === "Payment" ? "PMT"
                              : type === "Journal" ? "GL"
                              : type === "Invoice" ? "INV"
                              : "REF";
                var chipCls = type === "Payment" ? "vas-rr-type-pmt"
                              : type === "Journal" ? "vas-rr-type-gl"
                              : type === "Invoice" ? "vas-rr-type-inv"
                              : "vas-rr-type-other";

                rowsHtml +=
                    '<tr>' +
                    '<td>' +
                    '<span class="vas-rr-type-chip ' + chipCls + '" title="' + escapeHtml(type) + '">' + chipText + '</span>' +
                    '<span class="vas-rr-d-inv-no">' + escapeHtml(refDocText) + '</span>' +
                    '</td>' +
                    '<td class="vas-rr-d-inv-date">' + escapeHtml(refDateText) + '</td>' +
                    '<td class="vas-rr-d-num">' + symHtml + escapeHtml(refAmount) + '</td>' +
                    '<td class="vas-rr-d-num vas-rr-d-alloc-amt">' + symHtml + escapeHtml(applied) + '</td>' +
                    '</tr>';
            }

            var totalText = payAmountSign + (sym ? sym : '') + payAmountText;
            var invCountLabel = lines.length === 1
                ? lbl("VAS_Invoice", "allocation")
                : lbl("VAS_Invoices", "allocations");

            /* The allocation table sits inside a bordered frame that owns
               the remaining vertical space in the modal. Inside the frame:
                 • .vas-rr-d-alloc-table-wrap — flex-grows, overflow-y:auto,
                   so only the line rows scroll when the list is long.
                 • .vas-rr-alloc-foot — flex-shrink:0, anchored at the
                   bottom with the running totals.
               The frame's outer border keeps the whole allocation block
               looking like a single panel even though only the inner
               table area scrolls. */
            $dialogAlloc.html(
                '<div class="vas-rr-d-alloc-frame">' +
                '<div class="vas-rr-d-alloc-table-wrap">' +
                '<table class="vas-rr-alloc-table">' +
                '<thead>' +
                '<tr>' +
                '<th>' + escapeHtml(lbl("VAS_Reference", "Reference")) + '</th>' +
                '<th>' + escapeHtml(lbl("VAS_Date", "Date")) + '</th>' +
                '<th class="vas-rr-d-num">' + escapeHtml(lbl("VAS_InvoiceAmount", "Document amount")) + '</th>' +
                '<th class="vas-rr-d-num">' + escapeHtml(lbl("VAS_AllocatedAmount", "Allocated amount")) + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody>' + rowsHtml + '</tbody>' +
                '</table>' +
                '</div>' +
                '<div class="vas-rr-alloc-foot">' +
                '<span>' + lines.length + ' ' + escapeHtml(invCountLabel) + ' ' + escapeHtml(lbl("VAS_AllocatedSuffix", "allocated")) + '</span>' +
                '<span>' + escapeHtml(lbl("VAS_ReceiptTotal", "Receipt total")) + ' <b>' + escapeHtml(totalText) + '</b></span>' +
                '</div>' +
                '</div>'
            );
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-rr-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-rr-dialog-scrim"></div>' +
                '<div class="vas-rr-dialog-card">' +

                '<div class="vas-rr-dialog-header">' +
                '<div class="vas-rr-dialog-htext">' +
                '<span class="vas-rr-dialog-hicon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/>' +
                '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>' +
                '</svg>' +
                '</span>' +
                '<div class="vas-rr-dialog-title-group">' +
                '<div class="vas-rr-dialog-title">' + escapeHtml(lbl("VAS_ReceiptDialog", "Receipt")) + '</div>' +
                '<div class="vas-rr-dialog-sub"></div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-rr-dialog-actions">' +
                '<button type="button" class="vas-rr-dialog-print" aria-label="' + escapeHtml(lbl("VAS_Print", "Print")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="6 9 6 2 18 2 18 9"/>' +
                '<path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/>' +
                '<rect x="6" y="14" width="12" height="8"/>' +
                '</svg>' +
                '<span class="vas-rr-print-label">' + escapeHtml(lbl("VAS_Print", "Print")) + '</span>' +
                '</button>' +
                '<button type="button" class="vas-rr-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-rr-dialog-body">' +
                '<div class="vas-rr-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +

                '<div class="vas-rr-section-label">' + escapeHtml(lbl("VAS_ReceiptSummary", "Receipt summary")) + '</div>' +
                '<div class="vas-rr-d-grid"></div>' +

                '<div class="vas-rr-section-label">' + escapeHtml(lbl("VAS_Allocation", "Allocation")) + '</div>' +
                '<div class="vas-rr-d-alloc"></div>' +

                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogBusy = $dialog.find('.vas-rr-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $dialogTitle = $dialog.find('.vas-rr-dialog-title');
            $dialogSub = $dialog.find('.vas-rr-dialog-sub');
            $dialogGrid = $dialog.find('.vas-rr-d-grid');
            $dialogAlloc = $dialog.find('.vas-rr-d-alloc');
            $dialogPrint = $dialog.find('.vas-rr-dialog-print');

            $dialogPrint.on('click', function (e) {
                e.stopPropagation();
                printReceipt();
            });

            $dialog.find('.vas-rr-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-rr-dialog-scrim').on('click', function () {
                closeDialog();
            });

            $(document).on('keydown.vas-rr', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            /* Attached to <body> so fixed positioning escapes any
               transformed dashboard ancestor. */
            $('body').append($dialog);
        }

        function createWidget() {
            $card = $(
                '<div class="vas-rr-card">' +

                '<div class="vas-rr-head">' +
                '<div class="vas-rr-head-left">' +
                '<span class="vas-rr-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/>' +
                '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>' +
                '</svg>' +
                '</span>' +
                '<span class="vas-rr-title">' + escapeHtml(lbl("VAS_RecentReceipts", "Recent receipts")) + '</span>' +
                '</div>' +
                '<span class="vas-rr-head-hint">' + escapeHtml(lbl("VAS_ClickRowForDetail", "Click a row for full detail")) + '</span>' +
                '</div>' +

                '<div class="vas-rr-table-wrap">' +
                '<table class="vas-rr-table">' +
                '<thead>' +
                '<tr>' +
                '<th class="vas-rr-th-date">' + escapeHtml(lbl("VAS_Date", "Date")) + '</th>' +
                '<th class="vas-rr-th-doc">' + escapeHtml(lbl("VAS_ReceiptNo", "Receipt No.")) + '</th>' +
                '<th class="vas-rr-th-customer">' + escapeHtml(lbl("VAS_Customer", "Customer")) + '</th>' +
                '<th class="vas-rr-th-method">' + escapeHtml(lbl("VAS_Method", "Method")) + '</th>' +
                '<th class="vas-rr-th-bank">' + escapeHtml(lbl("VAS_BankAccount", "Bank account")) + '</th>' +
                '<th class="vas-rr-th-amount">' + escapeHtml(lbl("VAS_Amount", "Amount")) + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody class="vas-rr-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-rr-footer">' +
                '<span class="vas-rr-pager-helper"></span>' +
                '<div class="vas-rr-pager">' +
                '<button type="button" class="vas-rr-pager-btn vas-rr-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-rr-pager-text"></span>' +
                '<button type="button" class="vas-rr-pager-btn vas-rr-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>'
            );

            $tbody = $card.find('.vas-rr-tbody');
            $pagerHelper = $card.find('.vas-rr-pager-helper');
            $pagerPrev = $card.find('.vas-rr-pager-prev');
            $pagerNext = $card.find('.vas-rr-pager-next');
            $pagerText = $card.find('.vas-rr-pager-text');

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

            /* Row click → open detail modal. Delegated so re-rendered rows
               keep their handler across pages. */
            $tbody.on('click', 'tr.vas-rr-tr', function () {
                var pid = Number($(this).attr('data-payment-id') || 0);
                if (pid > 0) { openDialog(pid); }
            });

            $tbody.on('keydown', 'tr.vas-rr-tr', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    var pid = Number($(this).attr('data-payment-id') || 0);
                    if (pid > 0) { openDialog(pid); }
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-rr-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            createDialog();
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadRows();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-rr');
            $('body').removeClass('vas-rr-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();
        };
    };

    VAS.VAS_RecentReceiptsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Now that the root is in the DOM, self-wire the dashboard-width
           CSS variable the title clamp reads (spec §Measurement Setup). */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_RecentReceiptsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_RecentReceiptsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_RecentReceiptsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
