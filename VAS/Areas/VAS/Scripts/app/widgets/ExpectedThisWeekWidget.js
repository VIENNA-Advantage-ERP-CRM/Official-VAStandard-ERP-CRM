/**
 * Expected This Week Widget
 * Purpose - KPI card showing AR invoice amount due in the next 7 days in
 *           base (accounting schema) currency. Clicking the card opens a
 *           modal dialog listing the invoices with Date, Invoice No., Customer,
 *           Invoice Currency, Amount (in invoice currency — no conversion).
 * Design   - Per design.md / dashboard-widgets.md: Onfinity Glass Widget with
 *            `tint-success` KPI shell — icon well, muted label, ↗ View hint,
 *            big bold success-green metric prefixed with the base currency
 *            symbol, and a plain "Invoice due in 7 days" detail line.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Expected this week                    | VIS_ExpectedThisWeek
 *  2  | View                                  | VIS_View
 *  3  | Invoice due in 7 days                 | VIS_InvoiceDueIn7Days
 *  4  | Invoices due within the next 7 days   | VIS_InvoicesDueWithinNext7Days
 *  5  | Invoice No.                           | VIS_InvoiceNo
 *  6  | Invoice date                          | VIS_InvoiceDate
 *  7  | Customer                              | VIS_Customer
 *  8  | Due date                              | VIS_DueDate
 *  9  | Invoice Currency                      | VIS_InvoiceCurrency
 * 10  | Due amount                            | VIS_DueAmount
 * 11  | Due in                                | VIS_DueIn
 * 11  | Today                                 | VIS_Today
 * 12  | day / days                            | VIS_Day / VIS_Days
 * 13  | overdue                               | VIS_Overdue
 * 14  | Close                                 | VIS_Close
 * 15  | Loading…                              | VIS_Loading
 * 16  | No invoices due in this period        | VIS_NoInvoicesDuePeriod
 * 17  | invoice / invoices                    | VIS_Invoice / VIS_Invoices
 * 18  | Showing                               | VIS_Showing
 * 19  | of                                    | VIS_Of
 * 20  | Previous                              | VIS_Previous
 * 21  | Next                                  | VIS_Next
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ExpectedThisWeekWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-etw-root">');
        var $metricEl;
        var $busy;
        var $dialog;
        var $dialogTbody;
        var $dialogSubtitle;
        var $dialogBusy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;

        /* Latest KPI snapshot (kept so the dialog header can re-use the base
           currency symbol and the period string without a refetch). */
        var lastSymbol = "";
        var lastTotal = 0;
        var rowsLoaded = false;
        var rowsLoading = false;

        /* Server-side paging: each page requests fresh rows from the controller
           so the browser never has to hold thousands of rows. */
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
                url: VIS.Application.contextUrl + 'ExpectedThisWeek/GetExpectedThisWeek',
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
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'ExpectedThisWeek/GetExpectedThisWeekRows',
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

                    /* Clear in-flight flag BEFORE renderPageResult so updatePagerControls
                       sets disabled state for the post-fetch reality. */
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

            if (pageNo > totalPages && totalPages > 0) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            renderRows(rows);

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);

            updatePagerControls(from, to);
        }

        function setNoData() {
            lastTotal = 0;

            if ($metricEl) {
                $metricEl.html(formatMetric(0, lastSymbol));
            }
        }

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

        /* Currency symbol leads the amount (sign before symbol when negative). */
        function formatMetric(value, symbol) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var compact = formatCompactAmount(Math.abs(value));
            var sym = symbol ? '<span class="vas-etw-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + compact;
        }

        function renderMetric(data) {
            var total = (data && data.expectedAmountThisWeek) || 0;
            var sym = (data && data.symbol) || "";

            lastTotal = Number(total);
            lastSymbol = sym;

            if ($metricEl) {
                $metricEl.html(formatMetric(lastTotal, lastSymbol));
            }

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

        /* Days from "today" (local) to a `yyyy-MM-dd` date string. Result is
           positive when the date is in the future, negative when it's in the
           past, zero when it is today. Returns `null` when the input cannot be
           parsed so the caller can render an empty cell instead of NaN days. */
        function getDaysUntilDue(dateStr) {
            if (!dateStr) { return null; }
            var due = new Date(dateStr);
            if (isNaN(due.getTime())) { return null; }

            var today = new Date();
            today.setHours(0, 0, 0, 0);
            due.setHours(0, 0, 0, 0);

            return Math.round((due.getTime() - today.getTime()) / 86400000);
        }

        function formatDueIn(days) {
            if (days == null) { return ""; }

            if (days === 0) {
                return lbl("VIS_Today", "Today");
            }

            var unit = Math.abs(days) === 1
                ? lbl("VIS_Day", "day")
                : lbl("VIS_Days", "days");

            if (days < 0) {
                return Math.abs(days) + ' ' + unit + ' ' + lbl("VIS_Overdue", "overdue");
            }

            return days + ' ' + unit;
        }

        function renderRows(rows) {
            if (!$dialogTbody) { return; }
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-etw-dialog-empty" colspan="7">' +
                    lbl("VIS_NoInvoicesDuePeriod", "No invoices due in this period") +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var docNo = row.documentNo || "";
                var invoiceDateText = formatDate(row.invoiceDate);
                var customer = row.customer || "";
                var dueDateText = formatDate(row.dueDate);
                var invoiceCurrency = row.invoiceCurrency || "";
                var sym = row.invoiceCurrencySymbol || invoiceCurrency || "";
                var amountText = formatExactAmount(row.amount);
                /* Amount keeps its invoice-currency symbol inline — no
                   conversion to base currency, per spec. */
                var amountHtml = (sym ? '<span class="vas-etw-cur-inline">' + escapeHtml(sym) + '</span>' : '') + escapeHtml(amountText);

                var dueDays = getDaysUntilDue(row.dueDate);
                var dueInText = formatDueIn(dueDays);
                /* Tone the "Due in" pill amber by default, red if the schedule
                   is already overdue. Today (0 days) gets a slightly stronger
                   amber to signal urgency. */
                var dueInClass = "vas-etw-duein-pill";
                if (dueDays != null && dueDays < 0) {
                    dueInClass += " vas-etw-duein-overdue";
                }
                else if (dueDays === 0) {
                    dueInClass += " vas-etw-duein-today";
                }

                var $tr = $(
                    '<tr>' +
                    '<td class="vas-etw-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-etw-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-etw-td-invdate" title="' + escapeHtml(invoiceDateText) + '">' + escapeHtml(invoiceDateText) + '</td>' +
                    '<td class="vas-etw-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-etw-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-etw-td-duedate" title="' + escapeHtml(dueDateText) + '">' + escapeHtml(dueDateText) + '</td>' +
                    '<td class="vas-etw-td-currency" title="' + escapeHtml(invoiceCurrency) + '">' + escapeHtml(invoiceCurrency) + '</td>' +
                    '<td class="vas-etw-td-amount" title="' + escapeHtml((sym ? sym + ' ' : '') + amountText) + '">' + amountHtml + '</td>' +
                    '<td class="vas-etw-td-duein" title="' + escapeHtml(dueInText) + '">' +
                    (dueInText ? '<span class="' + dueInClass + '">' + escapeHtml(dueInText) + '</span>' : '') +
                    '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var invLabel = totalRecords === 1
                        ? lbl("VIS_Invoice", "invoice")
                        : lbl("VIS_Invoices", "invoices");

                    $pagerHelper.text(
                        lbl("VIS_Showing", "Showing") + ' ' +
                        (from + 1) + '–' + to + ' ' +
                        lbl("VIS_Of", "of") + ' ' + totalRecords + ' ' + invLabel
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
            $('body').addClass('vas-etw-body-lock');

            if (!rowsLoaded) {
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-etw-body-lock');

            /* Reset paging state so the next openDialog() starts at page 1 with
               a fresh fetch instead of remembering where the user left off. */
            pageNo = 1;
            rowsLoaded = false;
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-etw-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-etw-dialog-scrim"></div>' +
                '<div class="vas-etw-dialog-card">' +

                '<div class="vas-etw-dialog-header">' +
                '<div class="vas-etw-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>' +
                '<line x1="16" y1="2" x2="16" y2="6"/>' +
                '<line x1="8" y1="2" x2="8" y2="6"/>' +
                '<line x1="3" y1="10" x2="21" y2="10"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-etw-dialog-title-group">' +
                '<div class="vas-etw-dialog-title">' + lbl("VIS_ExpectedThisWeek", "Expected this week") + '</div>' +
                '<div class="vas-etw-dialog-subtitle">' + lbl("VIS_InvoicesDueWithinNext7Days", "Invoices due within the next 7 days") + '</div>' +
                '</div>' +
                '<button type="button" class="vas-etw-dialog-close" aria-label="' + escapeHtml(lbl("VIS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-etw-dialog-body">' +
                '<div class="vas-etw-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-etw-dialog-table">' +
                '<thead>' +
                '<tr>' +
                '<th class="vas-etw-th-doc" title="' + escapeHtml(lbl("VIS_InvoiceNo", "Invoice No.")) + '">' + lbl("VIS_InvoiceNo", "Invoice No.") + '</th>' +
                '<th class="vas-etw-th-invdate" title="' + escapeHtml(lbl("VIS_InvoiceDate", "Invoice date")) + '">' + lbl("VIS_InvoiceDate", "Invoice date") + '</th>' +
                '<th class="vas-etw-th-customer" title="' + escapeHtml(lbl("VIS_Customer", "Customer")) + '">' + lbl("VIS_Customer", "Customer") + '</th>' +
                '<th class="vas-etw-th-duedate" title="' + escapeHtml(lbl("VIS_DueDate", "Due date")) + '">' + lbl("VIS_DueDate", "Due date") + '</th>' +
                '<th class="vas-etw-th-currency" title="' + escapeHtml(lbl("VIS_InvoiceCurrency", "Invoice Currency")) + '">' + lbl("VIS_InvoiceCurrency", "Invoice Currency") + '</th>' +
                '<th class="vas-etw-th-amount" title="' + escapeHtml(lbl("VIS_DueAmount", "Due amount")) + '">' + lbl("VIS_DueAmount", "Due amount") + '</th>' +
                '<th class="vas-etw-th-duein" title="' + escapeHtml(lbl("VIS_DueIn", "Due in")) + '">' + lbl("VIS_DueIn", "Due in") + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody class="vas-etw-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-etw-dialog-footer">' +
                '<span class="vas-etw-pager-helper"></span>' +
                '<div class="vas-etw-pager">' +
                '<button type="button" class="vas-etw-pager-btn vas-etw-pager-prev" aria-label="' + escapeHtml(lbl("VIS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-etw-pager-text"></span>' +
                '<button type="button" class="vas-etw-pager-btn vas-etw-pager-next" aria-label="' + escapeHtml(lbl("VIS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-etw-dialog-tbody');
            $dialogSubtitle = $dialog.find('.vas-etw-dialog-subtitle');
            $dialogBusy = $dialog.find('.vas-etw-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-etw-pager-helper');
            $pagerPrev = $dialog.find('.vas-etw-pager-prev');
            $pagerNext = $dialog.find('.vas-etw-pager-next');
            $pagerText = $dialog.find('.vas-etw-pager-text');

            $dialog.find('.vas-etw-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-etw-dialog-scrim').on('click', function () {
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

            $(document).on('keydown.vas-etw', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-etw-card" role="button" tabindex="0">' +

                '<div class="vas-etw-head">' +
                '<div class="vas-etw-head-left">' +
                '<div class="vas-etw-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>' +
                '<line x1="16" y1="2" x2="16" y2="6"/>' +
                '<line x1="8" y1="2" x2="8" y2="6"/>' +
                '<line x1="3" y1="10" x2="21" y2="10"/>' +
                '</svg>' +
                '</div>' +
                '<span class="vas-etw-label">' + lbl("VIS_ExpectedThisWeek", "Expected this week") + '</span>' +
                '</div>' +
                '<button type="button" class="vas-etw-view">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M7 17 17 7"/><path d="M7 7h10v10"/>' +
                '</svg>' +
                '<span>' + lbl("VIS_View", "View") + '</span>' +
                '</button>' +
                '</div>' +

                '<div class="vas-etw-metric">—</div>' +

                '<span class="vas-etw-detail-text">' +
                lbl("VIS_InvoiceDueIn7Days", "Invoice due in 7 days") +
                '</span>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-etw-metric');

            $card.on('click', function () {
                openDialog();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            $card.find('.vas-etw-view').on('click', function (e) {
                e.stopPropagation();
                openDialog();
            });

            $root.append($card);

            $busy = $('<div class="vas-etw-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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
            $(document).off('keydown.vas-etw');
            $('body').removeClass('vas-etw-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();
        };
    };

    VIS.ExpectedThisWeekWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.ExpectedThisWeekWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.ExpectedThisWeekWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.ExpectedThisWeekWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);
