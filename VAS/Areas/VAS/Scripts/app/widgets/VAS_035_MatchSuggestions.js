/**
 * Match Suggestions Widget (Widget 19 — 6x3, drill-down key `match`)
 * Purpose - Pairs each unallocated customer receipt of the last 30 days with
 *           its best-fit open sales invoice (same partner, closest amount in
 *           the PAYMENT currency, converted at the payment date) and tags the
 *           pairing High / Review. Each row opens a match-review modal:
 *           confidence banner, payment ↔ invoice two-pane compare, balance
 *           strip and the "why this match" signal list. The modal Apply
 *           buttons POST to ApplyAllocation, which creates and completes a
 *           C_AllocationHdr on the payment accounting date (same outcome as
 *           the standard Allocation form). The footer "Open allocation form"
 *           link opens the standard Allocation form (AD_Form classname
 *           VAdvantage.Apps.AForms.VAllocation) via
 *           VIS.viewManager.startForm(AD_Form_ID).
 * Design  - Per design.md / PROMPT.md "Widget 19 — Match suggestions" +
 *           review image_1. All sizes in `em` per CLAUDE.md. Namespaced
 *           vas-msug-*.
 *
 * Backend - VAS_035_MatchSuggestions/GetMatchSuggestions  (paged list)
 *           VAS_035_MatchSuggestions/GetMatchDetail        (review modal)
 *           VAS_035_MatchSuggestions/ApplyAllocation       (create + complete allocation)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+----------------------------
 *  1  | Match suggestions                     | VAS_035_MatchSuggestions
 * 1a  | Unallocated receipts paired with      | VAS_035_Subtitle
 *     |   their best-fit invoice              |
 *  2  | Open Allocation Form                  | VAS_035_OpenAllocationForm
 *  3  | High                                  | VAS_035_High
 *  4  | Review                                | VAS_035_Review
 *  6  | due                                   | VAS_035_Due
 *  7  | suggestion / suggestions              | VAS_035_Suggestion / VAS_035_Suggestions
 *  8  | ready to allocate                     | VAS_035_ReadyToAllocate
 *  9  | No match suggestions                  | VAS_035_NoSuggestions
 * 10  | Match Review                          | VAS_035_MatchReview
 * 11  | High confidence match                 | VAS_035_HighConfidenceMatch
 * 12  | Needs review                          | VAS_035_NeedsReview
 * 13  | Payment · Receipt                     | VAS_035_PaymentReceipt
 * 14  | Suggested Invoice                     | VAS_035_SuggestedInvoice
 * 15  | Receipt Date                          | VAS_035_ReceiptDate
 * 16  | Customer                              | VAS_Customer
 * 17  | Payment Method                        | VAS_035_PaymentMethod
 * 18  | Reference                             | VAS_035_Reference
 * 19  | Bank Account                          | VAS_011_BankAccount
 * 20  | Currency                              | VAS_PaymentCurrency
 * 21  | Amount Received                       | VAS_035_AmountReceived
 * 22  | Invoice Date                          | VAS_035_InvoiceDate
 * 23  | Payment Terms                         | VAS_035_PaymentTerms
 * 24  | Due Date                              | VAS_035_DueDate
 * 25  | Grand Total                           | VAS_035_GrandTotal
 * 26  | Open Amount                           | VAS_035_OpenAmount
 * 27  | balance — fully settles the invoice   | VAS_035_FullySettles
 * 28  | still open after apply                | VAS_035_StillOpen
 * 29  | Why this match                        | VAS_035_WhyThisMatch
 * 30  | Partner matches                       | VAS_035_PartnerMatches
 * 31  | Receipt and invoice belong to the     | VAS_035_PartnerMatchesDetail
 *     |   same customer                       |
 * 32  | Amount matches                        | VAS_035_AmountMatches
 * 33  | Amount differs                        | VAS_035_AmountDiffers
 * 34  | Reference cited                       | VAS_035_ReferenceCited
 * 35  | Invoice number found in the receipt   | VAS_035_ReferenceCitedDetail
 *     |   reference                           |
 * 36  | No reference cited                    | VAS_035_NoReferenceCited
 * 37  | Invoice number not found in the       | VAS_035_NoReferenceCitedDetail
 *     |   receipt reference                   |
 * 38  | Within due window                     | VAS_035_WithinDueWindow
 * 39  | Outside due window                    | VAS_035_OutsideDueWindow
 * 40  | days from due date                    | VAS_035_DaysFromDueDate
 * 41  | Skip                                  | VAS_035_Skip
 * 42  | Apply Allocation                      | VAS_035_ApplyAllocation
 * 43  | Apply as part-payment                 | VAS_035_ApplyPartPayment
 * 44  | Close                                 | VAS_Close
 * 45  | of / Previous / Next                  | VAS_Of / VAS_Previous / VAS_Next
 * 46  | vs                                    | VAS_035_Vs
 * 47  | Allocation created                    | VAS_035_AllocationCreated
 * 48  | Allocation could not be created       | VAS_035_AllocationFailed
 * 49  | Low                                   | VAS_035_Low
 * 50  | Low confidence match                  | VAS_035_LowConfidence
 * 51  | remains unallocated on the receipt    | VAS_035_RemainsOnReceipt
 *     |   after apply                         |
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_035_MatchSuggestions = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-msug-root">');
        var $card;
        var $listBody;
        var $footerInfo;
        var $openFormLink;
        var $pager;
        var $prevBtn;
        var $nextBtn;
        var $pageText;
        var $busy;

        var $dialog;
        var $dialogBody;
        var $dialogBanner;
        var $dialogFooterApply;
        var $dialogBusy;

        /* Pairing currently shown in the modal — the Apply target. The pay
           schedule id pins the allocation to exactly the suggested schedule. */
        var currentPaymentId = 0;
        var currentInvoiceId = 0;
        var currentPayScheduleId = 0;
        var applying = false;

        /* AD_Form_ID of the standard Allocation form (resolved server-side from
           AD_Form.ClassName = 'VAdvantage.Apps.AForms.VAllocation'). */
        var allocationFormId = 0;

        var pageNo = 1;
        var pageSize = 5;
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

        function formatAmount(amount, symbol, precision) {
            var text = Number(amount || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: Number(precision || 0),
                maximumFractionDigits: Number(precision || 0)
            });

            var sym = symbol ? symbol.toString() : "";
            if (!sym) { return text; }

            /* 3-char ISO codes read better after the amount; glyph symbols before. */
            return sym.length === 3 ? (text + " " + sym) : (sym + text);
        }

        function formatDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, {
                day: "2-digit", month: "short", year: "numeric"
            });
        }

        /* Compact "18 May" used by the row meta line (image_1). */
        function formatShortDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, {
                day: "numeric", month: "short"
            });
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadData();
        };

        /* ── List ─────────────────────────────────────────────────────── */

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_035_MatchSuggestions/GetMatchSuggestions',
                type: 'GET',
                cache: false,
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (!data || data.error) { setNoData(); return; }

                    renderData(data);
                },
                error: function () { setNoData(); },
                complete: function () { showBusy(false); }
            });
        }

        function setNoData() {
            totalPages = 0;
            totalRecords = 0;

            if ($listBody) {
                $listBody.html(
                    '<div class="vas-msug-nodata">' +
                    escapeHtml(lbl("VAS_035_NoSuggestions", "No match suggestions")) +
                    '</div>'
                );
            }

            if ($footerInfo) { $footerInfo.text(""); }
            updatePager();
        }

        function renderData(data) {
            var rows = (data && data.Rows) ? data.Rows : [];

            allocationFormId = Number(data && data.AllocationFormId || 0);
            if ($openFormLink) {
                $openFormLink.css('display', allocationFormId > 0 ? 'inline-flex' : 'none');
            }

            if (rows.length === 0) { setNoData(); return; }

            totalRecords = Number(data.TotalRecords || 0);
            totalPages = Number(data.TotalPages || 0);
            if (data && data.PageNo) { pageNo = Number(data.PageNo); }

            $listBody.empty();

            for (var i = 0; i < rows.length; i++) {
                $listBody.append(buildRow(rows[i]));
            }

            if ($footerInfo) {
                var sugLabel = totalRecords === 1
                    ? lbl("VAS_035_Suggestion", "suggestion")
                    : lbl("VAS_035_Suggestions", "suggestions");

                /* Count and total in bold; the total stays in the receipt
                   currency (no base-currency conversion). */
                $footerInfo.html(
                    '<b>' + escapeHtml(totalRecords + ' ' + sugLabel) + '</b> · ' +
                    '<b>' + escapeHtml(formatAmount(data.ReadyToAllocate, data.CurrencySymbol, data.Precision)) + '</b> ' +
                    escapeHtml(lbl("VAS_035_ReadyToAllocate", "ready to allocate"))
                );
            }

            updatePager();
        }

        function buildRow(row) {
            /* Three-way confidence: High (green) / Review (amber) / Low (red). */
            var confClass = row.Confidence === 'HIGH' ? 'vas-msug-tag-high'
                : (row.Confidence === 'REVIEW' ? 'vas-msug-tag-review' : 'vas-msug-tag-low');
            var tagText = row.Confidence === 'HIGH' ? lbl("VAS_035_High", "High")
                : (row.Confidence === 'REVIEW' ? lbl("VAS_035_Review", "Review") : lbl("VAS_035_Low", "Low"));
            var receiptAmt = formatAmount(row.ReceiptAmount, row.ReceiptCurrencySymbol, row.ReceiptPrecision);
            /* Invoice open amount arrives already converted to the receipt
               currency at the payment date — same symbol/precision as the receipt. */
            var openAmt = formatAmount(row.OpenAmount, row.ReceiptCurrencySymbol, row.ReceiptPrecision);

            var $row = $(
                '<div class="vas-msug-row" role="button" tabindex="0">' +
                '<span class="vas-msug-rail"></span>' +
                '<div class="vas-msug-info">' +
                /* Customer · receipt amount, side by side (image_1). */
                '<div class="vas-msug-row-title">' +
                '<span class="vas-msug-customer">' + escapeHtml(row.Customer) + '</span>' +
                '<span class="vas-msug-row-sep">·</span>' +
                '<span class="vas-msug-row-amount">' + escapeHtml(receiptAmt) + '</span>' +
                '</div>' +
                '<div class="vas-msug-meta">' +
                '<span class="vas-msug-doc" title="' + escapeHtml(row.ReceiptNo) + '">' + escapeHtml(row.ReceiptNo) + '</span>' +
                ' → ' +
                '<span class="vas-msug-doc" title="' + escapeHtml(row.InvoiceNo) + '">' + escapeHtml(row.InvoiceNo) + '</span>' +
                ' · ' + escapeHtml(openAmt) +
                ' · ' + escapeHtml(lbl("VAS_035_Due", "due")) + ' ' + escapeHtml(formatShortDate(row.DueDate)) +
                '</div>' +
                '</div>' +
                '<span class="vas-msug-tag ' + confClass + '">' +
                '<span class="vas-msug-tag-dot"></span>' + escapeHtml(tagText) +
                '</span>' +
                '<span class="vas-msug-chevron">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</span>' +
                '</div>'
            );

            $row.on('click', function () { openDialog(row.PaymentId, row.InvoiceId, row.InvoicePayScheduleId); });
            $row.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog(row.PaymentId, row.InvoiceId, row.InvoicePayScheduleId);
                }
            });

            return $row;
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + " " + lbl("VAS_Of", "of") + " " + totalPages) : "");
            }
            if ($prevBtn) { $prevBtn.prop("disabled", pageNo <= 1 || totalPages <= 1); }
            if ($nextBtn) { $nextBtn.prop("disabled", totalPages <= 1 || pageNo >= totalPages); }

            /* No pager chrome at all when there is nothing to page through —
               the whole pager row collapses so it leaves no gap. */
            if ($pager) {
                $pager.css('display', totalPages > 1 ? 'inline-flex' : 'none');
                $pager.closest('.vas-msug-footer-row').css('display', totalPages > 1 ? 'flex' : 'none');
            }
        }

        /* ── Allocation form (VAdvantage.Apps.AForms.VAllocation) ─────── */

        function openAllocationForm() {
            if (allocationFormId > 0) {
                closeDialog();
                VIS.viewManager.startForm(allocationFormId);
            }
        }

        /* ── Match-review modal ───────────────────────────────────────── */

        function openDialog(paymentId, invoiceId, payScheduleId) {
            if (!$dialog) { return; }

            currentPaymentId = Number(paymentId || 0);
            currentInvoiceId = Number(invoiceId || 0);
            currentPayScheduleId = Number(payScheduleId || 0);
            applying = false;

            $dialog.show();
            $('body').addClass('vas-msug-body-lock');
            $dialogBody.empty();
            $dialogBanner.attr('class', 'vas-msug-banner').empty();
            $dialogFooterApply.prop('disabled', true).text(lbl("VAS_035_ApplyAllocation", "Apply allocation"));

            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_035_MatchSuggestions/GetMatchDetail',
                type: 'GET',
                cache: false,
                data: { paymentId: currentPaymentId, invoiceId: currentInvoiceId, payScheduleId: currentPayScheduleId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (!data || data.error) { renderDetailEmpty(); return; }

                    renderDetail(data);
                },
                error: function () { renderDetailEmpty(); },
                complete: function () { showDialogBusy(false); }
            });
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-msug-body-lock');
        }

        function renderDetailEmpty() {
            $dialogBody.html(
                '<div class="vas-msug-nodata">' +
                escapeHtml(lbl("VAS_035_NoSuggestions", "No match suggestions")) +
                '</div>'
            );
        }

        function paneRow(label, value, valueClass) {
            return '<div class="vas-msug-pane-row">' +
                '<span class="vas-msug-pane-label">' + escapeHtml(label) + '</span>' +
                '<span class="vas-msug-pane-value' + (valueClass ? ' ' + valueClass : '') + '">' + escapeHtml(value || '—') + '</span>' +
                '</div>';
        }

        /* "<Bank Name> · ****<last-4>"; degrade gracefully when a part is missing. */
        function formatBankAccount(data) {
            var bankName = data.BankName ? String(data.BankName).trim() : "";
            var accountNo = data.AccountNo ? String(data.AccountNo).trim() : "";
            var last4 = accountNo ? (accountNo.length > 4 ? accountNo.slice(-4) : accountNo) : "";

            if (bankName && last4) { return bankName + ' · ****' + last4; }
            if (bankName) { return bankName; }
            if (last4) { return '****' + last4; }
            return "";
        }

        function checkRow(ok, title, detail) {
            var okIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
            var crossIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';

            return '<div class="vas-msug-check ' + (ok ? 'vas-msug-check-ok' : 'vas-msug-check-warn') + '">' +
                '<span class="vas-msug-check-icon">' + (ok ? okIcon : crossIcon) + '</span>' +
                '<div class="vas-msug-check-text">' +
                '<div class="vas-msug-check-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-msug-check-detail">' + escapeHtml(detail) + '</div>' +
                '</div>' +
                '</div>';
        }

        function renderDetail(data) {
            var balance = Number(data.BalanceAfterApply || 0);
            var isShort = balance > 0;       /* receipt < open: schedule stays partly open */
            var isOver = balance < 0;        /* receipt > open: remainder stays on the receipt */

            /* 1 — confidence banner: score + verdict (High / Review / Low). */
            var bannerClass = data.Confidence === 'HIGH' ? 'vas-msug-banner-high'
                : (data.Confidence === 'REVIEW' ? 'vas-msug-banner-review' : 'vas-msug-banner-low');
            var verdict = data.Confidence === 'HIGH' ? lbl("VAS_035_HighConfidenceMatch", "High confidence match")
                : (data.Confidence === 'REVIEW' ? lbl("VAS_035_NeedsReview", "Needs review") : lbl("VAS_035_LowConfidence", "Low confidence match"));

            $dialogBanner
                .attr('class', 'vas-msug-banner ' + bannerClass)
                .html(
                    '<span class="vas-msug-banner-score">' + Number(data.Score || 0) + '%</span>' +
                    '<span class="vas-msug-banner-verdict">' + escapeHtml(verdict) + '</span>'
                );

            /* 2 — two-pane compare with a link arrow between them. */
            var receiptAmt = formatAmount(data.ReceiptAmount, data.ReceiptCurrencySymbol, data.ReceiptPrecision);
            var grandTotal = formatAmount(data.GrandTotal, data.InvoiceCurrencySymbol, data.InvoicePrecision);
            var openAmt = formatAmount(data.OpenAmount, data.InvoiceCurrencySymbol, data.InvoicePrecision);
            /* Open amount converted to the receipt currency at the payment date —
               basis of the compare, balance line and amount signal. */
            var openAmtPay = formatAmount(data.OpenAmountPay, data.ReceiptCurrencySymbol, data.ReceiptPrecision);

            var paymentPane =
                '<div class="vas-msug-pane">' +
                '<div class="vas-msug-pane-head">' + escapeHtml(lbl("VAS_035_PaymentReceipt", "Payment · Receipt")) +
                '<span class="vas-msug-pane-doc">' + escapeHtml(data.ReceiptNo || '') + '</span></div>' +
                paneRow(lbl("VAS_035_ReceiptDate", "Receipt Date"), formatDate(data.ReceiptDate)) +
                paneRow(lbl("VAS_Customer", "Customer"), data.ReceiptCustomer) +
                paneRow(lbl("VAS_035_PaymentMethod", "Payment Method"), data.PaymentMethod) +
                paneRow(lbl("VAS_035_Reference", "Reference"), data.Reference) +
                paneRow(lbl("VAS_011_BankAccount", "Bank Account"), formatBankAccount(data)) +
                paneRow(lbl("VAS_PaymentCurrency", "Currency"), data.ReceiptCurrency) +
                paneRow(lbl("VAS_035_AmountReceived", "Amount Received"), receiptAmt, 'vas-msug-amount-received') +
                '</div>';

            var invoicePane =
                '<div class="vas-msug-pane">' +
                '<div class="vas-msug-pane-head">' + escapeHtml(lbl("VAS_035_SuggestedInvoice", "Suggested Invoice")) +
                '<span class="vas-msug-pane-doc">' + escapeHtml(data.InvoiceNo || '') + '</span></div>' +
                paneRow(lbl("VAS_035_InvoiceDate", "Invoice Date"), formatDate(data.InvoiceDate)) +
                paneRow(lbl("VAS_Customer", "Customer"), data.InvoiceCustomer) +
                paneRow(lbl("VAS_035_PaymentTerms", "Payment Terms"), data.PaymentTerms) +
                paneRow(lbl("VAS_035_DueDate", "Due Date"), formatDate(data.DueDate)) +
                paneRow(lbl("VAS_035_GrandTotal", "Grand Total"), grandTotal) +
                paneRow(lbl("VAS_PaymentCurrency", "Currency"), data.InvoiceCurrency) +
                paneRow(lbl("VAS_035_OpenAmount", "Open Amount"), openAmt, 'vas-msug-amount-open') +
                '</div>';

            var compare =
                '<div class="vas-msug-compare">' +
                paymentPane +
                '<span class="vas-msug-link-arrow">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>' +
                '</span>' +
                invoicePane +
                '</div>';

            /* 3 — balance strip (receipt currency): exact / short / over. */
            var balanceAmt = formatAmount(Math.abs(balance), data.ReceiptCurrencySymbol, data.ReceiptPrecision);
            var balanceStrip;
            if (isShort) {
                /* Receipt is short — the difference stays open on the schedule. */
                balanceStrip = '<div class="vas-msug-balance vas-msug-balance-short">' +
                    escapeHtml(balanceAmt + ' ' + lbl("VAS_035_StillOpen", "still open after apply")) +
                    '</div>';
            }
            else if (isOver) {
                /* Receipt exceeds the open amount — the difference stays
                   unallocated on the receipt after the schedule settles. */
                balanceStrip = '<div class="vas-msug-balance vas-msug-balance-short">' +
                    escapeHtml(balanceAmt + ' ' + lbl("VAS_035_RemainsOnReceipt", "remains unallocated on the receipt after apply")) +
                    '</div>';
            }
            else {
                balanceStrip = '<div class="vas-msug-balance vas-msug-balance-exact">' +
                    escapeHtml(formatAmount(0, data.ReceiptCurrencySymbol, data.ReceiptPrecision) + ' ' + lbl("VAS_035_FullySettles", "balance — fully settles the invoice")) +
                    '</div>';
            }

            /* 4 — "why this match": the four signals. */
            var gapText = Number(data.DateGapDays) >= 0
                ? (data.DateGapDays + ' ' + lbl("VAS_035_DaysFromDueDate", "days from due date"))
                : '—';

            var checks =
                '<div class="vas-msug-why">' +
                '<div class="vas-msug-why-title">' + escapeHtml(lbl("VAS_035_WhyThisMatch", "Why this match")) + '</div>' +
                checkRow(!!data.PartnerOk,
                    lbl("VAS_035_PartnerMatches", "Partner matches"),
                    lbl("VAS_035_PartnerMatchesDetail", "Receipt and invoice belong to the same customer")) +
                checkRow(!!data.AmountOk,
                    data.AmountOk ? lbl("VAS_035_AmountMatches", "Amount matches") : lbl("VAS_035_AmountDiffers", "Amount differs"),
                    receiptAmt + ' ' + lbl("VAS_035_Vs", "vs") + ' ' + openAmtPay) +
                checkRow(!!data.RefOk,
                    data.RefOk ? lbl("VAS_035_ReferenceCited", "Reference cited") : lbl("VAS_035_NoReferenceCited", "No reference cited"),
                    data.RefOk
                        ? lbl("VAS_035_ReferenceCitedDetail", "Invoice number found in the receipt reference")
                        : lbl("VAS_035_NoReferenceCitedDetail", "Invoice number not found in the receipt reference")) +
                checkRow(!!data.DateOk,
                    data.DateOk ? lbl("VAS_035_WithinDueWindow", "Within due window") : lbl("VAS_035_OutsideDueWindow", "Outside due window"),
                    gapText) +
                '</div>';

            $dialogBody.html(compare + balanceStrip + checks);

            /* 5 — footer: the apply button becomes part-payment when the receipt is short. */
            $dialogFooterApply
                .text(isShort
                    ? lbl("VAS_035_ApplyPartPayment", "Apply as part-payment")
                    : lbl("VAS_035_ApplyAllocation", "Apply allocation"))
                .prop('disabled', false);
        }

        /* POSTs the pairing to ApplyAllocation: the server creates and
           completes a C_AllocationHdr dated on the payment accounting date
           (part-payment when the receipt is short), then the list reloads. */
        function applyCurrentSuggestion() {
            if (applying || currentPaymentId <= 0 || currentInvoiceId <= 0 || currentPayScheduleId <= 0) { return; }

            applying = true;
            $dialogFooterApply.prop('disabled', true);
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_035_MatchSuggestions/ApplyAllocation',
                type: 'POST',
                data: { paymentId: currentPaymentId, invoiceId: currentInvoiceId, payScheduleId: currentPayScheduleId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    /* ADialog.info translates its FIRST arg as a message key;
                       dynamic server text goes verbatim in the THIRD arg. */
                    if (data && data.Success) {
                        closeDialog();
                        VIS.ADialog.info(null, null, data.Message || lbl("VAS_035_AllocationCreated", "Allocation created"));
                        pageNo = 1;
                        loadData();
                    }
                    else {
                        VIS.ADialog.info(null, null, (data && data.Message) || lbl("VAS_035_AllocationFailed", "Allocation could not be created"));
                        $dialogFooterApply.prop('disabled', false);
                    }
                },
                error: function () {
                    VIS.ADialog.info(null, null, lbl("VAS_035_AllocationFailed", "Allocation could not be created"));
                    $dialogFooterApply.prop('disabled', false);
                },
                complete: function () {
                    applying = false;
                    showDialogBusy(false);
                }
            });
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-msug-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-msug-dialog-scrim"></div>' +
                '<div class="vas-msug-dialog-card">' +

                '<div class="vas-msug-dialog-header">' +
                '<div class="vas-msug-dialog-title">' + escapeHtml(lbl("VAS_035_MatchReview", "Match review")) + '</div>' +
                '<button type="button" class="vas-msug-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-msug-banner"></div>' +

                '<div class="vas-msug-dialog-body">' +
                '<div class="vas-msug-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<div class="vas-msug-dialog-content"></div>' +
                '</div>' +

                '<div class="vas-msug-dialog-footer">' +
                '<button type="button" class="vas-msug-btn-skip">' + escapeHtml(lbl("VAS_035_Skip", "Skip")) + '</button>' +
                '<button type="button" class="vas-msug-btn-apply">' + escapeHtml(lbl("VAS_035_ApplyAllocation", "Apply allocation")) + '</button>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-msug-dialog-content');
            $dialogBanner = $dialog.find('.vas-msug-banner');
            $dialogBusy = $dialog.find('.vas-msug-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $dialogFooterApply = $dialog.find('.vas-msug-btn-apply');

            $dialog.find('.vas-msug-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });
            $dialog.find('.vas-msug-dialog-scrim').on('click', function () { closeDialog(); });
            $dialog.find('.vas-msug-btn-skip').on('click', function () { closeDialog(); });
            $dialogFooterApply.on('click', function () { applyCurrentSuggestion(); });

            $(document).on('keydown.vas-msug', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        /* ── Card shell ───────────────────────────────────────────────── */

        function createWidget() {
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            var arrowR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>';

            /* Link / pairing glyph for the title icon well. */
            var linkIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"></path>' +
                '<path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"></path>' +
                '</svg>';

            $card = $(
                '<div class="vas-msug-card">' +

                '<div class="vas-msug-head">' +
                '<div class="vas-msug-head-left">' +
                '<span class="vas-msug-icon">' + linkIcon + '</span>' +
                '<div class="vas-msug-title-group">' +
                '<span class="vas-msug-title">' + escapeHtml(lbl("VAS_035_MatchSuggestions", "Match Suggestions")) + '</span>' +
                '<span class="vas-msug-subtitle">' + escapeHtml(lbl("VAS_035_Subtitle", "Unallocated receipts paired with their best-fit invoice")) + '</span>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-msug-body"></div>' +

                /* Footer: info (left) + open-form link (right) on one line,
                   pager on its own line below. */
                '<div class="vas-msug-footer">' +
                '<div class="vas-msug-footer-top">' +
                '<span class="vas-msug-footer-info"></span>' +
                '<a href="javascript:void(0)" class="vas-msug-openform" style="display:none;">' +
                escapeHtml(lbl("VAS_035_OpenAllocationForm", "Open Allocation Form")) + ' ' + arrowR +
                '</a>' +
                '</div>' +
                '<div class="vas-msug-footer-row" style="display:none;">' +
                '<div class="vas-msug-pager" style="display:none;">' +
                '<button type="button" class="vas-msug-page-btn vas-msug-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-msug-page-text"></span>' +
                '<button type="button" class="vas-msug-page-btn vas-msug-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '</div>'
            );

            $listBody = $card.find('.vas-msug-body');
            $footerInfo = $card.find('.vas-msug-footer-info');
            $openFormLink = $card.find('.vas-msug-openform');
            $pager = $card.find('.vas-msug-pager');
            $prevBtn = $card.find('.vas-msug-prev');
            $nextBtn = $card.find('.vas-msug-next');
            $pageText = $card.find('.vas-msug-page-text');

            /* The footer link lands the user on the standard Allocation form
               where every suggestion is one click away. */
            $openFormLink.on('click', function () { openAllocationForm(); });

            $prevBtn.on('click', function () {
                if (pageNo <= 1) { return; }
                pageNo--;
                loadData();
            });

            $nextBtn.on('click', function () {
                if (totalPages <= 1 || pageNo >= totalPages) { return; }
                pageNo++;
                loadData();
            });

            $root.append($card);

            $busy = $('<div class="vas-msug-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-msug');
            $('body').removeClass('vas-msug-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_035_MatchSuggestions.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_035_MatchSuggestions.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_035_MatchSuggestions.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_035_MatchSuggestions.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
