/**
 * On-account Receipts Widget (Widget 06)
 * Purpose - Glass KPI card (tint-success) showing the total of customer
 *           advances / on-account receipts not yet matched to invoices, in the
 *           base (accounting-schema) currency, with a plain-text "N unapplied
 *           advances" line. Clicking opens a modal listing each receipt
 *           (Date, Receipt No., Customer, Bank account, Amount).
 * Design  - Per design.md / PROMPT.md "Widget 06" + image_1 (modal) and
 *           image_2 (card). All sizes in `em` per CLAUDE.md. Namespaced
 *           vas-oar-*.
 *
 * Backend - OnAccountReceipts/GetOnAccountReceipts   (KPI tile)
 *           OnAccountReceipts/GetOnAccountRows        (paged modal)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | On-Account receipts                       | VAS_003_OnAccountReceipts
 *  2  | On-Account                                | VAS_003_OnAccount
 *  3  | Advances and on-account payments not yet  | VAS_003_AdvancesSubtitle
 *     |   matched to invoices                     |
 *  4  | View                                      | VAS_View
 *  5  | unapplied advances                        | VAS_003_UnappliedAdvances
 *  6  | unapplied advance                         | VAS_003_UnappliedAdvance
 *  7  | Date                                      | VAS_003_Date
 *  8  | Receipt No.                               | VAS_003_ReceiptNo
 *  9  | Customer                                  | VAS_Customer
 * 10  | Bank account                              | VAS_003_BankAccount
 * 10a | Currency                                  | VAS_PaymentCurrency
 * 11  | Amount                                    | VAS_003_Amount
 * 12  | No on-account receipts                    | VAS_003_NoOnAccountReceipts
 * 13  | receipt / receipts                        | VAS_003_Receipt / VAS_003_Receipts
 * 14  | Close                                     | VAS_Close
 * 15  | Showing                                   | VAS_Showing
 * 16  | of                                        | VAS_Of
 * 17  | Previous                                  | VAS_Previous
 * 18  | Next                                      | VAS_Next
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

    VAS.VAS_003_OnAccountReceipts = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-oar-root">');
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

        /* Latest KPI snapshot (base-currency symbol/iso/precision re-used by the metric). */
        var lastSymbol = "";
        var lastIso = "";
        var lastPrecision;               /* undefined → formatCompactAmount falls back to std precision */
        var lastTotal = 0;
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
                url: VIS.Application.contextUrl + 'VAS_003_OnAccountReceipts/GetOnAccountReceipts',
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
                url: VIS.Application.contextUrl + 'VAS_003_OnAccountReceipts/GetOnAccountRows',
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
            lastTotal = 0;
            lastCount = 0;
            if ($metricEl) { $metricEl.html(formatMetric(0, lastSymbol, lastIso, lastPrecision)); }
            if ($detailEl) { $detailEl.text(detailText(0)); }
        }

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* fall through */ }
            return 2;
        }

        /* Compose the metric exactly like OverdueWidget: sign FIRST, then the
           base-currency symbol, then the compact magnitude from the shared
           VIS.Util.formatCompactAmount (Indian vs international numbering by iso,
           kept to the currency precision). The symbol renders whenever it is
           present — never gated behind a non-zero value, so 0 still shows it. */
        function formatMetric(value, symbol, isoCode, precision) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var compact = VIS.Util.formatCompactAmount(value, isoCode, precision);
            var sym = symbol ? '<span class="vas-oar-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + compact;
        }

        function detailText(count) {
            var n = Number(count || 0);
            var noun = n === 1
                ? lbl("VAS_003_UnappliedAdvance", "unapplied advance")
                : lbl("VAS_003_UnappliedAdvances", "unapplied advances");
            return n + ' ' + noun;
        }

        function renderMetric(data) {
            lastTotal = Number(data.onAccountAmount || 0);
            lastSymbol = data.symbol || "";
            lastCount = Number(data.advanceCount || 0);
            /* Use iso + precision from the backend when present; otherwise fall
               back to the client's standard precision (iso stays "" → intl scale). */
            lastIso = data.isoCode || "";
            lastPrecision = (data.stdPrecision === undefined || data.stdPrecision === null)
                ? getStdPrecision() : Number(data.stdPrecision);

            if ($metricEl) { $metricEl.html(formatMetric(lastTotal, lastSymbol, lastIso, lastPrecision)); }
            if ($detailEl) { $detailEl.text(detailText(lastCount)); }

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
                    '<tr><td class="vas-oar-dialog-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_003_NoOnAccountReceipts", "No on-account receipts")) +
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
                var iso = row.currencyIso || "";
                var sym = row.curSymbol || iso || "";
                var stdPrecision = (row.stdPrecision === undefined || row.stdPrecision === null)
                    ? getStdPrecision() : Number(row.stdPrecision);
                var rawAmt = Number(row.amount || 0);
                var amtSign = rawAmt < 0 ? '-' : '';
                /* Modal shows the full, exact per-row amount (no compact magnitude):
                   sign + symbol + locale-formatted number at the row's precision. */
                var amtText = Math.abs(rawAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
                var amtHtml = escapeHtml(amtSign) + (sym ? '<span class="vas-oar-cur-inline">' + escapeHtml(sym) + '</span>' : '') + escapeHtml(amtText);

                var $tr = $(
                    '<tr>' +
                    '<td class="vas-oar-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-oar-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-oar-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-oar-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-oar-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-oar-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-oar-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-oar-td-currency" title="' + escapeHtml(iso) + '">' + escapeHtml(iso) + '</td>' +
                    '<td class="vas-oar-td-amount" title="' + escapeHtml(amtSign + (sym ? sym + ' ' : '') + amtText) + '">' + amtHtml + '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var rcptLabel = totalRecords === 1
                        ? lbl("VAS_003_Receipt", "receipt")
                        : lbl("VAS_003_Receipts", "receipts");

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
            $('body').addClass('vas-oar-body-lock');
            if (!rowsLoaded) { loadRows(); }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-oar-body-lock');
            pageNo = 1;
            rowsLoaded = false;
        }

        function cardIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<rect x="2" y="5" width="20" height="14" rx="2"></rect>' +
                '<line x1="2" y1="10" x2="22" y2="10"></line>' +
                '</svg>';
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-oar-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-oar-dialog-scrim"></div>' +
                '<div class="vas-oar-dialog-card">' +

                '<div class="vas-oar-dialog-header">' +
                '<div class="vas-oar-dialog-icon">' + cardIconSvg() + '</div>' +
                '<div class="vas-oar-dialog-title-group">' +
                '<div class="vas-oar-dialog-title">' + escapeHtml(lbl("VAS_003_OnAccountReceipts", "On-account receipts")) + '</div>' +
                '<div class="vas-oar-dialog-subtitle">' + escapeHtml(lbl("VAS_003_AdvancesSubtitle", "Advances and on-account payments not yet matched to invoices")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-oar-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-oar-dialog-body">' +
                '<div class="vas-oar-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-oar-dialog-table">' +
                '<thead><tr>' +
                '<th class="vas-oar-th-date" title="' + escapeHtml(lbl("VAS_003_Date", "Date")) + '">' + escapeHtml(lbl("VAS_003_Date", "Date")) + '</th>' +
                '<th class="vas-oar-th-doc" title="' + escapeHtml(lbl("VAS_003_ReceiptNo", "Receipt No.")) + '">' + escapeHtml(lbl("VAS_003_ReceiptNo", "Receipt No.")) + '</th>' +
                '<th class="vas-oar-th-customer" title="' + escapeHtml(lbl("VAS_Customer", "Customer")) + '">' + escapeHtml(lbl("VAS_Customer", "Customer")) + '</th>' +
                '<th class="vas-oar-th-bank" title="' + escapeHtml(lbl("VAS_003_BankAccount", "Bank account")) + '">' + escapeHtml(lbl("VAS_003_BankAccount", "Bank account")) + '</th>' +
                '<th class="vas-oar-th-currency" title="' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '">' + escapeHtml(lbl("VAS_PaymentCurrency", "Currency")) + '</th>' +
                '<th class="vas-oar-th-amount" title="' + escapeHtml(lbl("VAS_003_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_003_Amount", "Amount")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-oar-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-oar-dialog-footer">' +
                '<span class="vas-oar-pager-helper"></span>' +
                '<div class="vas-oar-pager">' +
                '<button type="button" class="vas-oar-pager-btn vas-oar-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-oar-pager-text"></span>' +
                '<button type="button" class="vas-oar-pager-btn vas-oar-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-oar-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-oar-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-oar-pager-helper');
            $pagerPrev = $dialog.find('.vas-oar-pager-prev');
            $pagerNext = $dialog.find('.vas-oar-pager-next');
            $pagerText = $dialog.find('.vas-oar-pager-text');

            $dialog.find('.vas-oar-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });
            $dialog.find('.vas-oar-dialog-scrim').on('click', function () { closeDialog(); });

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

            $(document).on('keydown.vas-oar', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-oar-card" role="button" tabindex="0">' +

                '<div class="vas-oar-head">' +
                '<div class="vas-oar-head-left">' +
                '<div class="vas-oar-icon">' + cardIconSvg() + '</div>' +
                '<div class="vas-oar-label-group">' +
                '<span class="vas-oar-label">' + escapeHtml(lbl("VAS_003_OnAccount", "On-account")) + '</span>' +
                '<span class="vas-oar-subtitle">' + escapeHtml(lbl("VAS_003_TotalUnallocatedReceipts", "Total Unallocated Receipts")) + '</span>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-oar-metric">—</div>' +

                '<span class="vas-oar-detail-text"></span>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-oar-metric');
            $detailEl = $card.find('.vas-oar-detail-text');

            $card.on('click', function () { openDialog(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });
            $root.append($card);

            $busy = $('<div class="vas-oar-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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
            $(document).off('keydown.vas-oar');
            $('body').removeClass('vas-oar-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_003_OnAccountReceipts.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_003_OnAccountReceipts.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_003_OnAccountReceipts.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_003_OnAccountReceipts.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
