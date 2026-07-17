/**
 * Unreconciled Receipts Widget (Widget 05)
 * Purpose - Glass KPI card (tint-info) showing the COUNT of customer receipts
 *           (CO/CL) not yet reconciled against the bank statement, with a
 *           plain-text "Not yet matched to statement" line. Clicking opens a
 *           modal listing each receipt (Date, Receipt No., Customer, Bank
 *           account, Payment Currency, Amount).
 * Design  - Per design.md / PROMPT.md "Widget 05" + image_1 (modal) and
 *           image_2 (card). All sizes in `em` per CLAUDE.md. Namespaced
 *           vas-unr-*.
 *
 * Backend - VAS_011_UnreconciledReceipts/GetUnreconciledReceipts   (KPI tile)
 *           VAS_011_UnreconciledReceipts/GetUnreconciledRows        (paged modal)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Unreconciled receipts                     | VAS_011_UnreconciledReceipts
 *  2  | Created this month, not yet reconciled    | VAS_011_UnreconciledSubtitle
 *     |   with the bank statement                 |
 *  3  | Not yet matched to statement              | VAS_011_NotYetMatched
 *  4  | View                                      | VAS_View
 *  5  | Date                                      | VAS_011_Date
 *  6  | Receipt No.                               | VAS_011_ReceiptNo
 *  7  | Customer                                  | VAS_Customer
 *  8  | Bank account                              | VAS_011_BankAccount
 *  9  | Currency                                  | VAS_PaymentCurrency
 * 10  | Amount                                    | VAS_011_Amount
 * 11  | No unreconciled receipts                  | VAS_011_NoUnreconciledReceipts
 * 12  | receipt / receipts                        | VAS_011_Receipt / VAS_011_Receipts
 * 13  | Close                                     | VAS_Close
 * 14  | Showing                                   | VAS_Showing
 * 15  | of                                        | VAS_Of
 * 16  | Previous                                  | VAS_Previous
 * 17  | Next                                      | VAS_Next
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_011_UnreconciledReceipts = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-unr-root">');
        var $metricEl;
        var $busy;
        var $dialog;
        var $dialogTbody;
        var $dialogBusy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;

        var lastCount = 0;
        var rowsLoaded = false;
        var rowsLoading = false;

        /* Server-side paging. */
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
                url: VIS.Application.contextUrl + 'VAS_011_UnreconciledReceipts/GetUnreconciledReceipts',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setNoData(); return; }

                    renderMetric(data || {});
                },
                error: function () { setNoData(); },
                complete: function () { showBusy(false); }
            });
        }

        function loadRows() {
            if (!$dialogTbody) { return; }

            rowsLoading = true;
            showDialogBusy(true);
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_011_UnreconciledReceipts/GetUnreconciledRows',
                type: 'GET',
                cache: false,
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

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

            if (data && typeof data.pageNo !== "undefined") { pageNo = Number(data.pageNo); }
            if (pageNo > totalPages && totalPages > 0) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            renderRows(rows);

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);
            updatePagerControls(from, to);
        }

        function setNoData() {
            lastCount = 0;
            if ($metricEl) { $metricEl.text(formatCount(0)); }
        }

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* fall through */ }
            return 2;
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        /* Returns a signed amount string: sign ('+'/'-') comes FIRST, followed by
           the absolute magnitude formatted to stdPrecision decimal places.
           Examples: "+63,000.00", "-13,000.00".
           Callers must pass the RAW signed value (never Math.abs'd). */
        function formatExactAmount(value, stdPrecision) {
            var num = Number(value || 0);
            var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
            var sign = num < 0 ? '-' : '';
            var magnitude = Math.abs(num).toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
            return sign + magnitude;
        }

        function renderMetric(data) {
            lastCount = Number(data.count || 0);
            if ($metricEl) { $metricEl.text(formatCount(lastCount)); }
            if ($dialog && $dialog.is(':visible')) { loadRows(); }
        }

        function formatDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, {
                day: "2-digit", month: "short", year: "numeric"
            });
        }

        /* "<Bank Name> · ****<last-4>"; degrade gracefully when a part is missing. */
        function formatBankAccount(row) {
            var bankName = (row && row.bankName) ? String(row.bankName).trim() : "";
            var accountNo = (row && row.accountNo) ? String(row.accountNo).trim() : "";
            var last4 = "";
            if (accountNo) { last4 = accountNo.length > 4 ? accountNo.slice(-4) : accountNo; }

            if (bankName && last4) { return bankName + ' · ****' + last4; }
            if (bankName) { return bankName; }
            if (last4) { return '****' + last4; }
            return "";
        }

        function renderRows(rows) {
            if (!$dialogTbody) { return; }
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-unr-dialog-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_011_NoUnreconciledReceipts", "No unreconciled receipts")) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.date);
                var docNo = row.documentNo || "";
                var customer = row.customer || "";
                var bankText = formatBankAccount(row);
                var stdPrecision = Number(row.stdPrecision || getStdPrecision());
                /* Sign comes from the raw value; magnitude is formatted from the
                   ABSOLUTE value so the formatter never injects its own sign
                   (passing the raw value would leave the first digit to be mis-read
                   as the sign). Rendered order: sign · symbol-span · magnitude. */
                var rawAmt = Number(row.amount || 0);
                var amtSign = rawAmt < 0 ? '-' : '';
                var amtMagnitude = formatExactAmount(Math.abs(rawAmt), stdPrecision);
                var iso = row.currencyIso || "";
                var sym = row.curSymbol || iso || "";
                var amtHtml = escapeHtml(amtSign) +
                    (sym ? '<span class="vas-unr-cur-inline">' + escapeHtml(sym) + '</span>' : '') +
                    escapeHtml(amtMagnitude);

                var $tr = $(
                    '<tr>' +
                    '<td class="vas-unr-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-unr-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-unr-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-unr-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-unr-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-unr-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-unr-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-unr-td-currency" title="' + escapeHtml(iso) + '">' + escapeHtml(iso) + '</td>' +
                    '<td class="vas-unr-td-amount" title="' + escapeHtml(amtSign + (sym ? sym : '') + amtMagnitude) + '">' + amtHtml + '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var rcptLabel = totalRecords === 1
                        ? lbl("VAS_011_Receipt", "receipt")
                        : lbl("VAS_011_Receipts", "receipts");

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
            if ($pagerPrev) { $pagerPrev.prop("disabled", rowsLoading || pageNo <= 1); }
            if ($pagerNext) { $pagerNext.prop("disabled", rowsLoading || totalPages <= 1 || pageNo >= totalPages); }
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('vas-unr-body-lock');
            if (!rowsLoaded) { loadRows(); }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-unr-body-lock');
            pageNo = 1;
            rowsLoaded = false;
        }

        /* Refresh / reconcile circular-arrows glyph (matches image_1). */
        function reconcileIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="23 4 23 10 17 10"></polyline>' +
                '<polyline points="1 20 1 14 7 14"></polyline>' +
                '<path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>' +
                '</svg>';
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-unr-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-unr-dialog-scrim"></div>' +
                '<div class="vas-unr-dialog-card">' +

                '<div class="vas-unr-dialog-header">' +
                '<div class="vas-unr-dialog-icon">' + reconcileIconSvg() + '</div>' +
                '<div class="vas-unr-dialog-title-group">' +
                '<div class="vas-unr-dialog-title">' + escapeHtml(lbl("VAS_011_UnreconciledReceipts", "Unreconciled receipts")) + '</div>' +
                '<div class="vas-unr-dialog-subtitle">' + escapeHtml(lbl("VAS_011_UnreconciledSubtitle", "Created this month, not yet reconciled with the bank statement")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-unr-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-unr-dialog-body">' +
                '<div class="vas-unr-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-unr-dialog-table">' +
                '<thead><tr>' +
                '<th class="vas-unr-th-date" title="' + escapeHtml(lbl("VAS_011_Date", "Date")) + '">' + escapeHtml(lbl("VAS_011_Date", "Date")) + '</th>' +
                '<th class="vas-unr-th-doc" title="' + escapeHtml(lbl("VAS_011_ReceiptNo", "Receipt No.")) + '">' + escapeHtml(lbl("VAS_011_ReceiptNo", "Receipt No.")) + '</th>' +
                '<th class="vas-unr-th-customer" title="' + escapeHtml(lbl("VAS_Customer", "Customer")) + '">' + escapeHtml(lbl("VAS_Customer", "Customer")) + '</th>' +
                '<th class="vas-unr-th-bank" title="' + escapeHtml(lbl("VAS_011_BankAccount", "Bank account")) + '">' + escapeHtml(lbl("VAS_011_BankAccount", "Bank account")) + '</th>' +
                '<th class="vas-unr-th-currency" title="' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '">' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '</th>' +
                '<th class="vas-unr-th-amount" title="' + escapeHtml(lbl("VAS_011_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_011_Amount", "Amount")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-unr-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-unr-dialog-footer">' +
                '<span class="vas-unr-pager-helper"></span>' +
                '<div class="vas-unr-pager">' +
                '<button type="button" class="vas-unr-pager-btn vas-unr-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-unr-pager-text"></span>' +
                '<button type="button" class="vas-unr-pager-btn vas-unr-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-unr-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-unr-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-unr-pager-helper');
            $pagerPrev = $dialog.find('.vas-unr-pager-prev');
            $pagerNext = $dialog.find('.vas-unr-pager-next');
            $pagerText = $dialog.find('.vas-unr-pager-text');

            $dialog.find('.vas-unr-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });
            $dialog.find('.vas-unr-dialog-scrim').on('click', function () { closeDialog(); });

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

            $(document).on('keydown.vas-unr', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-unr-card" role="button" tabindex="0">' +

                '<div class="vas-unr-head">' +
                '<div class="vas-unr-head-left">' +
                '<div class="vas-unr-icon">' + reconcileIconSvg() + '</div>' +
                '<div class="vas-unr-label-group">' +
                '<span class="vas-unr-label">' + escapeHtml(lbl("VAS_011_UnreconciledReceipts", "Unreconciled receipts")) + '</span>' +
                '<span class="vas-unr-subtitle">' + escapeHtml(lbl("VAS_011_AllTimeNotReconciled", "All-time receipts not yet reconciled")) + '</span>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-unr-metric">—</div>' +

                '<span class="vas-unr-detail-text">' + escapeHtml(lbl("VAS_011_NotYetMatched", "Not yet matched to statement")) + '</span>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-unr-metric');

            $card.on('click', function () { openDialog(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });
            $root.append($card);

            $busy = $('<div class="vas-unr-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            createDialog();
        }

        this.refreshWidget = function () {
            rowsLoaded = false;
            pageNo = 1;
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-unr');
            $('body').removeClass('vas-unr-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_011_UnreconciledReceipts.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_011_UnreconciledReceipts.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_011_UnreconciledReceipts.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_011_UnreconciledReceipts.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
