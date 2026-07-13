/**
 * Today's Receipts Widget (AR Receipt widget, branch 45)
 * Purpose - Glass KPI card (tint-info) showing the TOTAL amount of AR receipts
 *           recorded today (base/accounting currency, compact e.g. ₹8.65L), with
 *           a "TODAY · N receipts · across M customers" detail pill (image_1).
 *           Clicking opens a modal listing each receipt — Date, Receipt No.,
 *           Customer, Bank account (Bank · ****last-4), Payment Currency, Amount
 *           (full value, no ellipsis) — in the receipt's own currency (image_2).
 * Design  - design.md (Onfinity) glass-card family; sizes in `em` per CLAUDE.md.
 *           Namespaced vas-tdr-*.
 *
 * Backend - VAS_012_TodayReceipts/GetTodayReceipts      (KPI tile)
 *           VAS_012_TodayReceipts/GetTodayReceiptRows    (paged modal)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Today's receipts                          | VAS_012_TodayReceipts
 *  2  | TODAY                                     | VAS_012_TodayBadge
 *  3  | across                                    | VAS_012_Across
 *  4  | customer / customers                      | VAS_012_Customer / VAS_012_Customers
 *  5  | Receipts recorded today                   | VAS_012_Subtitle
 *  6  | No receipts today                         | VAS_012_NoReceiptsToday
 *  7  | View                                      | VAS_View
 *  8  | Date                                      | VAS_Date
 *  9  | Receipt No.                               | VAS_ReceiptNo
 * 10  | Customer                                  | VAS_Customer
 * 11  | Bank account                              | VAS_BankAccount
 * 12  | Currency                                  | VAS_PaymentCurrency
 * 13  | Amount                                    | VAS_Amount
 * 14  | receipt / receipts                        | VAS_Receipt / VAS_Receipts
 * 15  | Close                                     | VAS_Close
 * 16  | Showing                                   | VAS_Showing
 * 17  | of                                        | VAS_Of
 * 18  | Previous                                  | VAS_Previous
 * 19  | Next                                      | VAS_Next
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

    VAS.VAS_012_TodayReceipts = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-tdr-root">');
        var $metricEl;
        var $detailEl;
        var $busy;
        var $dialog;
        var $dialogTbody;
        var $dialogBusy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;

        /* Latest KPI snapshot. */
        var lastSymbol = "";
        var lastIso = "";
        var lastPrecision;               /* undefined → VIS.Util falls back to std precision */
        var lastAmount = 0;
        var lastReceiptCount = 0;
        var lastCustomerCount = 0;

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
                url: VIS.Application.contextUrl + 'VAS_012_TodayReceipts/GetTodayReceipts',
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
                url: VIS.Application.contextUrl + 'VAS_012_TodayReceipts/GetTodayReceiptRows',
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
            lastAmount = 0;
            lastReceiptCount = 0;
            lastCustomerCount = 0;
            if ($metricEl) { $metricEl.html(formatMetric(0, lastSymbol, lastIso, lastPrecision)); }
            renderDetail();
        }

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* fall through */ }
            return 2;
        }

        function formatExactAmount(value, stdPrecision) {
            var num = Number(value || 0);
            var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
            return num.toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
        }

        /* Currency symbol *before* the amount, sign before symbol only for
           negatives ('-' for value < 0; no sign for value >= 0; e.g. ₹1.2M, -₹0.5K).
           The compact magnitude (Indian vs international numbering by base currency,
           kept to precision) comes from the shared VIS.Util.formatCompactAmount, which
           always returns a non-negative string. The symbol is shown whenever present
           — including at zero (e.g. ₹0.00) — never gated behind a non-zero amount. */
        function formatMetric(value, symbol, isoCode, precision) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var compact = VIS.Util.formatCompactAmount(value, isoCode, precision);
            var sym = symbol ? '<span class="vas-tdr-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + compact;
        }

        /* Detail line: "<N> receipts · across <M> customers", pluralised. */
        function renderDetail() {
            if (!$detailEl) { return; }

            var rcptLabel = lastReceiptCount === 1
                ? lbl("VAS_Receipt", "receipt")
                : lbl("VAS_Receipts", "receipts");
            var custLabel = lastCustomerCount === 1
                ? lbl("VAS_012_Customer", "customer")
                : lbl("VAS_012_Customers", "customers");

            var text = lastReceiptCount + ' ' + rcptLabel + ' · ' +
                lbl("VAS_012_Across", "across") + ' ' +
                lastCustomerCount + ' ' + custLabel;

            $detailEl.text(text);
        }

        function renderMetric(data) {
            lastAmount = Number(data.amount || 0);
            lastSymbol = (data && data.symbol) || lastSymbol || "";
            lastIso = (data && data.isoCode) || lastIso || "";
            if (data && data.stdPrecision !== undefined && data.stdPrecision !== null) {
                lastPrecision = Number(data.stdPrecision);
            }
            lastReceiptCount = Number(data.receiptCount || 0);
            lastCustomerCount = Number(data.customerCount || 0);

            if ($metricEl) {
                $metricEl.html(formatMetric(lastAmount, lastSymbol, lastIso, lastPrecision));
                /* Full (non-compact) value on hover so the abbreviation never
                   hides the exact figure. */
                var _metricSign = lastAmount < 0 ? '-' : '';
                $metricEl.attr('title', _metricSign + (lastSymbol ? lastSymbol + ' ' : '') + formatExactAmount(Math.abs(lastAmount), lastPrecision));
            }

            renderDetail();

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
                    '<tr><td class="vas-tdr-dialog-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_012_NoReceiptsToday", "No receipts today")) +
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
                var rawAmt = Number(row.amount || 0);
                var amtSign = rawAmt < 0 ? '-' : '';
                var amtText = formatExactAmount(Math.abs(rawAmt), stdPrecision);
                var iso = row.currencyIso || "";
                var sym = row.curSymbol || iso || "";
                var amtHtml = escapeHtml(amtSign) + (sym ? '<span class="vas-tdr-cur-inline">' + escapeHtml(sym) + '</span>' : '') + escapeHtml(amtText);

                var $tr = $(
                    '<tr>' +
                    '<td class="vas-tdr-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-tdr-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-tdr-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-tdr-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-tdr-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-tdr-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-tdr-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-tdr-td-currency" title="' + escapeHtml(iso) + '">' + escapeHtml(iso) + '</td>' +
                    '<td class="vas-tdr-td-amount" title="' + escapeHtml(amtSign + (sym ? sym + ' ' : '') + amtText) + '">' + amtHtml + '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
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
            if ($pagerPrev) { $pagerPrev.prop("disabled", rowsLoading || pageNo <= 1); }
            if ($pagerNext) { $pagerNext.prop("disabled", rowsLoading || totalPages <= 1 || pageNo >= totalPages); }
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('vas-tdr-body-lock');
            if (!rowsLoaded) { loadRows(); }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-tdr-body-lock');
            pageNo = 1;
            rowsLoaded = false;
        }

        /* Clock glyph (matches image_1 header icon). */
        function clockIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<circle cx="12" cy="12" r="9"></circle>' +
                '<polyline points="12 7 12 12 15.5 14"></polyline>' +
                '</svg>';
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-tdr-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-tdr-dialog-scrim"></div>' +
                '<div class="vas-tdr-dialog-card">' +

                '<div class="vas-tdr-dialog-header">' +
                '<div class="vas-tdr-dialog-icon">' + clockIconSvg() + '</div>' +
                '<div class="vas-tdr-dialog-title-group">' +
                '<div class="vas-tdr-dialog-title">' + escapeHtml(lbl("VAS_012_TodayReceipts", "Today's receipts")) + '</div>' +
                '<div class="vas-tdr-dialog-subtitle">' + escapeHtml(lbl("VAS_012_Subtitle", "Receipts recorded today")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-tdr-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-tdr-dialog-body">' +
                '<div class="vas-tdr-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-tdr-dialog-table">' +
                '<thead><tr>' +
                '<th class="vas-tdr-th-date" title="' + escapeHtml(lbl("VAS_Date", "Date")) + '">' + escapeHtml(lbl("VAS_Date", "Date")) + '</th>' +
                '<th class="vas-tdr-th-doc" title="' + escapeHtml(lbl("VAS_ReceiptNo", "Receipt No.")) + '">' + escapeHtml(lbl("VAS_ReceiptNo", "Receipt No.")) + '</th>' +
                '<th class="vas-tdr-th-customer" title="' + escapeHtml(lbl("VAS_Customer", "Customer")) + '">' + escapeHtml(lbl("VAS_Customer", "Customer")) + '</th>' +
                '<th class="vas-tdr-th-bank" title="' + escapeHtml(lbl("VAS_BankAccount", "Bank account")) + '">' + escapeHtml(lbl("VAS_BankAccount", "Bank account")) + '</th>' +
                '<th class="vas-tdr-th-currency" title="' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '">' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '</th>' +
                '<th class="vas-tdr-th-amount" title="' + escapeHtml(lbl("VAS_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_Amount", "Amount")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-tdr-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-tdr-dialog-footer">' +
                '<span class="vas-tdr-pager-helper"></span>' +
                '<div class="vas-tdr-pager">' +
                '<button type="button" class="vas-tdr-pager-btn vas-tdr-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-tdr-pager-text"></span>' +
                '<button type="button" class="vas-tdr-pager-btn vas-tdr-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-tdr-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-tdr-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-tdr-pager-helper');
            $pagerPrev = $dialog.find('.vas-tdr-pager-prev');
            $pagerNext = $dialog.find('.vas-tdr-pager-next');
            $pagerText = $dialog.find('.vas-tdr-pager-text');

            $dialog.find('.vas-tdr-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });
            $dialog.find('.vas-tdr-dialog-scrim').on('click', function () { closeDialog(); });

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

            $(document).on('keydown.vas-tdr', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-tdr-card" role="button" tabindex="0">' +

                '<div class="vas-tdr-head">' +
                '<div class="vas-tdr-head-left">' +
                '<div class="vas-tdr-icon">' + clockIconSvg() + '</div>' +
                '<span class="vas-tdr-label">' + escapeHtml(lbl("VAS_012_TodayReceipts", "Today's receipts")) + '</span>' +
                '</div>' +
                '</div>' +

                '<div class="vas-tdr-metric">—</div>' +

                '<div class="vas-tdr-detail">' +
                '<span class="vas-tdr-detail-text"></span>' +
                '</div>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-tdr-metric');
            $detailEl = $card.find('.vas-tdr-detail-text');

            $card.on('click', function () { openDialog(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });
            $root.append($card);

            $busy = $('<div class="vas-tdr-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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
            $(document).off('keydown.vas-tdr');
            $('body').removeClass('vas-tdr-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_012_TodayReceipts.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_012_TodayReceipts.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_012_TodayReceipts.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_012_TodayReceipts.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
