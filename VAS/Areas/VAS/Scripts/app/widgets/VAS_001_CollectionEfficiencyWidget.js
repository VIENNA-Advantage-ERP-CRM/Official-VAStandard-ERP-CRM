/**
 * Collection Efficiency Widget
 * Purpose - Glass dashboard widget showing trailing-30-day Collection
 *           Efficiency (on-time %) + DSO + overdue summary. Clicking the
 *           widget opens the Overdue Invoice Aging modal — a server-paged
 *           list of open sales invoice schedules past their due date.
 * Design  - Per design.md / dashboard-widgets.md + the supplied mock
 *           (image_1 widget, image_2 modal). All sizes in `em` per
 *           CLAUDE.md project rule.
 *
 * Backend - CollectionEfficiency/GetCollectionEfficiency      (KPI tile)
 *           CollectionEfficiency/GetOverdueAgingRows           (paged modal)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Collection efficiency                     | VAS_001_CollectionEfficiency
 *  2  | DSO · last 30 days                        | VAS_001_CollectionSubtitle
 *  3  | Aging                                     | VAS_001_Aging
 *  4  | ON-TIME                                   | VAS_001_OnTimeCap
 *  5  | WATCH                                     | VAS_001_Watch
 *  6  | DELAYED                                   | VAS_001_Delayed
 *  7  | DSO                                       | VAS_001_DSO
 *  8  | days · target                             | VAS_001_DaysTarget
 *  9  | overdue ·                                 | VAS_001_OverdueDot
 * 10  | invoices                                  | VAS_001_Invoices
 * 11  | invoice                                   | VAS_001_Invoice
 * 12  | Overdue invoice aging                     | VAS_001_OverdueInvoiceAging
 * 13  | Open invoices past their due date         | VAS_001_OpenPastDue
 * 14  | Invoice No.                               | VAS_InvoiceNo
 * 15  | Customer                                  | VAS_Customer
 * 16  | Due date                                  | VAS_DueDate
 * 17  | Age                                       | VAS_001_Age
 * 18  | Overdue amount                            | VAS_001_OverdueAmount
 * 18a | Currency                                  | VAS_Currency
 * 19  | days                                      | VAS_001_Days
 * 20  | overdue invoices                          | VAS_001_OverdueInvoicesLabel
 * 21  | overdue invoice                           | VAS_001_OverdueInvoiceLabel
 * 23  | Previous                                  | VAS_Previous
 * 24  | Next                                      | VAS_Next
 * 25  | Close                                     | VAS_Close
 * 26  | No overdue invoices                       | VAS_001_NoOverdueInvoices
 * 27  | Showing                                   | VAS_Showing
 * 28  | of                                        | VAS_Of
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

    VAS.VAS_001_CollectionEfficiencyWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-ce-root">');
        var $card;
        var $busy;

        var $percentText;
        var $dsoText;
        var $overdueText;
        var $ringFg;       /* the foreground SVG circle path */

        /* Modal references. */
        var $dialog;
        var $dialogBusy;
        var $dialogTbody;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;

        /* KPI cache so the donut can re-render on resize without re-fetching. */
        var lastKpi = null;

        /* Server-paged modal state — 10 rows per page fills the modal body
           without leaving large empty space at the bottom. */
        var pageSize = 10;
        var pageNo = 1;
        var totalPages = 0;
        var totalRecords = 0;
        var rowsLoading = false;

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

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* ignore */ }
            return 2;
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

        /* Compact INR/USD-style formatter for the small overdue summary on the
           widget — 6.20L, 1.20Cr, 25.5K. Falls back to the locale's full
           number for amounts < 1000. */
        function formatCompactAmount(value, sym, stdPrecision) {
            var num = Number(value || 0);
            var absVal = Math.abs(num);
            var sign = num < 0 ? '-' : '';
            var s = sym || '';

            if (absVal >= 10000000) {
                return sign + s + (absVal / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }
            if (absVal >= 100000) {
                return sign + s + (absVal / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }
            if (absVal >= 1000) {
                return sign + s + (absVal / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }
            return sign + s + formatExactAmount(absVal, stdPrecision);
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

        function statusFromPercent(percent) {
            var p = Number(percent || 0);
            if (p >= 90) { return "ON-TIME"; }
            if (p >= 75) { return "WATCH"; }
            return "DELAYED";
        }

        function donutColorForStatus(status) {
            if (status === "ON-TIME") { return "#019D89"; }
            if (status === "WATCH") { return "#D78B10"; }
            return "#D14545";
        }

        function donutValueColor(status) {
            if (status === "ON-TIME") { return "#0B6B45"; }
            if (status === "WATCH") { return "#9A6500"; }
            return "#A33F3F";
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        /* ── KPI tile ───────────────────────────────────────────────── */

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'CollectionEfficiency/GetCollectionEfficiency',
                type: 'GET',
                cache: false,
                data: { filterType: "Last30Days" },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        renderKpi({});
                        return;
                    }
                    renderKpi(data || {});
                },
                error: function () { renderKpi({}); },
                complete: function () { showBusy(false); }
            });
        }

        function renderKpi(data) {
            lastKpi = data;

            var percent = Number(pick(data, "CollectionEfficiencyPercent", "collectionEfficiencyPercent") || 0);
            var dsoDays = Number(pick(data, "DsoDays", "dsoDays") || 0);
            var targetDays = Number(pick(data, "DsoTargetDays", "dsoTargetDays") || 22);
            var overdueAmount = Number(pick(data, "OverdueAmount", "overdueAmount") || 0);
            var overdueInvoiceCount = Number(pick(data, "OverdueInvoiceCount", "overdueInvoiceCount") || 0);
            var status = pick(data, "CollectionStatus", "collectionStatus") || statusFromPercent(percent);
            /* Base (accounting-schema) currency symbol returned by the server. */
            var curSymbol = pick(data, "CurSymbol", "curSymbol") || "";

            updateDonut(percent, status);

            if ($percentText) {
                var prec = getStdPrecision();
                $percentText.text(formatExactAmount(percent, prec) + "%");
                $percentText.css("color", donutValueColor(status));
            }

            $card.find('.vas-ce-on-time').text(statusLabel(status));

            if ($dsoText) {
                $dsoText.html(
                    '<strong>' + escapeHtml(lbl("VAS_001_DSO", "DSO")) + '</strong> ' +
                    escapeHtml(String(dsoDays)) + ' ' +
                    escapeHtml(lbl("VAS_001_DaysTarget", "days · target")) + ' ' +
                    escapeHtml(String(targetDays))
                );
            }

            if ($overdueText) {
                var invLabel = overdueInvoiceCount === 1
                    ? lbl("VAS_001_Invoice", "invoice")
                    : lbl("VAS_001_Invoices", "invoices");
                $overdueText.text(
                    formatCompactAmount(overdueAmount, curSymbol, 2) + ' ' +
                    lbl("VAS_001_OverdueDot", "overdue ·") + ' ' +
                    overdueInvoiceCount + ' ' + invLabel
                );
            }
        }

        function statusLabel(status) {
            if (status === "ON-TIME") { return lbl("VAS_001_OnTimeCap", "ON-TIME"); }
            if (status === "WATCH") { return lbl("VAS_001_Watch", "WATCH"); }
            return lbl("VAS_001_Delayed", "DELAYED");
        }

        /* SVG donut — pathLength 100 lets stroke-dasharray work in percent. */
        function updateDonut(percent, status) {
            if (!$ringFg || !$ringFg[0]) { return; }
            var safe = Math.max(0, Math.min(Number(percent || 0), 100));
            var color = donutColorForStatus(status);
            $ringFg.attr("stroke-dasharray", safe + " 100");
            $ringFg.attr("stroke", color);
        }

        /* ── Aging modal ────────────────────────────────────────────── */

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('vas-ce-body-lock');
            pageNo = 1;
            loadAging();
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-ce-body-lock');
            if ($dialogTbody) { $dialogTbody.empty(); }
            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            rowsLoading = false;
        }

        function loadAging() {
            if (rowsLoading) { return; }
            rowsLoading = true;
            showDialogBusy(true);
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'CollectionEfficiency/GetOverdueAgingRows',
                type: 'GET',
                cache: false,
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    rowsLoading = false;
                    showDialogBusy(false);

                    if (data && data.error) {
                        renderAgingPage({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo, totalAmount: 0 });
                        return;
                    }
                    renderAgingPage(data || {});
                },
                error: function () {
                    rowsLoading = false;
                    showDialogBusy(false);
                    renderAgingPage({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo, totalAmount: 0 });
                }
            });
        }

        function renderAgingPage(data) {
            var rows = pick(data, "Rows", "rows") || [];
            totalRecords = Number(pick(data, "TotalRecords", "totalRecords") || 0);
            totalPages = Number(pick(data, "TotalPages", "totalPages") || 0);

            var serverPage = pick(data, "PageNo", "pageNo");
            if (typeof serverPage !== "undefined" && serverPage !== null) {
                pageNo = Number(serverPage);
            }
            if (pageNo > totalPages && totalPages > 0) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            renderAgingRows(rows);

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);
            updatePagerControls(from, to);
        }

        function ageChipClass(ageDays) {
            var d = Number(ageDays || 0);
            if (d > 30) { return 'vas-ce-chip-danger'; }
            if (d >= 15) { return 'vas-ce-chip-warning'; }
            return 'vas-ce-chip-success';
        }

        function renderAgingRows(rows) {
            if (!$dialogTbody) { return; }
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-ce-d-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_001_NoOverdueInvoices", "No overdue invoices")) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var docNo = pick(row, "DocumentNo", "documentNo") || "";
                var customer = pick(row, "Customer", "customer") || "";
                var dueDate = formatDate(pick(row, "DueDate", "dueDate"));
                var ageDays = Number(pick(row, "AgeDays", "ageDays") || 0);
                var stdPrecision = Number(pick(row, "StdPrecision", "stdPrecision") || 2);
                var amount = Number(pick(row, "DueAmount", "dueAmount") || 0);
                var iso = pick(row, "CurrencyIso", "currencyIso") || "";
                var sym = pick(row, "CurSymbol", "curSymbol") || iso || "";

                var amtText = formatExactAmount(amount, stdPrecision);
                var amtHtml = (sym ? '<span class="vas-ce-cur-inline">' + escapeHtml(sym) + '</span>' : '') + escapeHtml(amtText);

                var chipCls = ageChipClass(ageDays);
                var ageText = ageDays + ' ' + lbl("VAS_001_Days", "days");

                var $tr = $(
                    '<tr>' +
                    '<td class="vas-ce-d-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-ce-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-ce-d-td-cust" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-ce-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-ce-d-td-date" title="' + escapeHtml(dueDate) + '">' + escapeHtml(dueDate) + '</td>' +
                    '<td class="vas-ce-d-td-age">' +
                    '<span class="vas-ce-chip ' + chipCls + '" title="' + escapeHtml(ageText) + '">' + escapeHtml(ageText) + '</span>' +
                    '</td>' +
                    '<td class="vas-ce-d-td-cur" title="' + escapeHtml(iso) + '">' + escapeHtml(iso) + '</td>' +
                    '<td class="vas-ce-d-td-amount" title="' + escapeHtml((sym ? sym + ' ' : '') + amtText) + '">' + amtHtml + '</td>' +
                    '</tr>'
                );
                $dialogTbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    /* Append the entity noun ("overdue invoices") to the
                       result range, matching the previous receipt modals
                       (e.g. "Showing 1–10 of 33 Receipts"). */
                    var helperLabel = totalRecords === 1
                        ? lbl("VAS_001_OverdueInvoiceLabel", "overdue invoice")
                        : lbl("VAS_001_OverdueInvoicesLabel", "overdue invoices");

                    $pagerHelper.text(
                        lbl("VAS_Showing", "Showing") + ' ' +
                        (from + 1) + '–' + to + ' ' +
                        lbl("VAS_Of", "of") + ' ' + totalRecords + ' ' + helperLabel
                    );
                } else {
                    $pagerHelper.text("");
                }
            }
            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? (pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages) : "");
            }
            if ($pagerPrev) { $pagerPrev.prop("disabled", rowsLoading || pageNo <= 1); }
            if ($pagerNext) { $pagerNext.prop("disabled", rowsLoading || totalPages <= 1 || pageNo >= totalPages); }
        }

        /* ── DOM scaffolding ────────────────────────────────────────── */

        function clockIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<polyline points="12 6 12 12 16 14"/>' +
                '</svg>';
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-ce-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-ce-dialog-scrim"></div>' +
                '<div class="vas-ce-dialog-card">' +

                '<div class="vas-ce-dialog-header">' +
                '<span class="vas-ce-dialog-hicon">' + clockIconSvg() + '</span>' +
                '<div class="vas-ce-dialog-title-group">' +
                '<div class="vas-ce-dialog-title">' + escapeHtml(lbl("VAS_001_OverdueInvoiceAging", "Overdue invoice aging")) + '</div>' +
                '<div class="vas-ce-dialog-sub">' + escapeHtml(lbl("VAS_001_OpenPastDue", "Open invoices past their due date")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-ce-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-ce-dialog-body">' +
                '<div class="vas-ce-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-ce-dialog-table">' +
                '<thead><tr>' +
                '<th class="vas-ce-d-th-doc">' + escapeHtml(lbl("VAS_InvoiceNo", "Invoice No.")) + '</th>' +
                '<th class="vas-ce-d-th-cust">' + escapeHtml(lbl("VAS_Customer", "Customer")) + '</th>' +
                '<th class="vas-ce-d-th-date">' + escapeHtml(lbl("VAS_DueDate", "Due date")) + '</th>' +
                '<th class="vas-ce-d-th-age">' + escapeHtml(lbl("VAS_001_Age", "Age")) + '</th>' +
                '<th class="vas-ce-d-th-cur">' + escapeHtml(lbl("VAS_Currency", "Currency")) + '</th>' +
                '<th class="vas-ce-d-th-amount">' + escapeHtml(lbl("VAS_001_OverdueAmount", "Overdue amount")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-ce-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                /* Bottom section — result-range helper (left) + compact
                   pager (right), matching the previous receipt modals. */
                '<div class="vas-ce-dialog-footer">' +
                '<span class="vas-ce-pager-helper"></span>' +
                '<div class="vas-ce-pager">' +
                '<button type="button" class="vas-ce-pager-btn vas-ce-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-ce-pager-text"></span>' +
                '<button type="button" class="vas-ce-pager-btn vas-ce-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogBusy = $dialog.find('.vas-ce-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $dialogTbody = $dialog.find('.vas-ce-dialog-tbody');
            $pagerHelper = $dialog.find('.vas-ce-pager-helper');
            $pagerPrev = $dialog.find('.vas-ce-pager-prev');
            $pagerNext = $dialog.find('.vas-ce-pager-next');
            $pagerText = $dialog.find('.vas-ce-pager-text');

            $dialog.find('.vas-ce-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });
            $dialog.find('.vas-ce-dialog-scrim').on('click', function () { closeDialog(); });

            $pagerPrev.on('click', function () {
                if (rowsLoading || pageNo <= 1) { return; }
                pageNo--;
                loadAging();
            });
            $pagerNext.on('click', function () {
                if (rowsLoading || pageNo >= totalPages) { return; }
                pageNo++;
                loadAging();
            });

            $(document).on('keydown.vas-ce', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            $card = $(
                '<div class="vas-ce-card" role="button" tabindex="0">' +

                '<div class="vas-ce-head">' +
                '<div class="vas-ce-head-left">' +
                '<span class="vas-ce-icon-wrap">' + clockIconSvg() + '</span>' +
                '<div class="vas-ce-title-group">' +
                '<div class="vas-ce-title">' + escapeHtml(lbl("VAS_001_CollectionEfficiency", "Collection efficiency")) + '</div>' +
                '<div class="vas-ce-subtitle">' + escapeHtml(lbl("VAS_001_CollectionSubtitle", "DSO · last 30 days")) + '</div>' +
                '</div>' +
                '</div>' +
                '<span class="vas-ce-open-hint">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7"/><path d="M7 7h10v10"/></svg>' +
                escapeHtml(lbl("VAS_001_Aging", "Aging")) +
                '</span>' +
                '</div>' +

                '<div class="vas-ce-body">' +
                '<div class="vas-ce-donut">' +
                '<svg viewBox="0 0 140 140" aria-hidden="true">' +
                '<circle cx="70" cy="70" r="56" fill="none" stroke="#EDF2F6" stroke-width="14"/>' +
                '<circle class="vas-ce-ring-fg" cx="70" cy="70" r="56" fill="none" stroke="#019D89" stroke-width="14" pathLength="100" stroke-dasharray="0 100" stroke-linecap="round" transform="rotate(-90 70 70)"/>' +
                '</svg>' +
                '<div class="vas-ce-donut-center">' +
                '<span class="vas-ce-percent">0%</span>' +
                '<span class="vas-ce-on-time">' + escapeHtml(lbl("VAS_001_OnTimeCap", "ON-TIME")) + '</span>' +
                '</div>' +
                '</div>' +
                '<div class="vas-ce-meta">' +
                '<div class="vas-ce-dso"><strong>' + escapeHtml(lbl("VAS_001_DSO", "DSO")) + '</strong> 0 ' + escapeHtml(lbl("VAS_001_DaysTarget", "days · target")) + ' 22</div>' +
                '<div class="vas-ce-overdue">0 ' + escapeHtml(lbl("VAS_001_OverdueDot", "overdue ·")) + ' 0 ' + escapeHtml(lbl("VAS_001_Invoices", "invoices")) + '</div>' +
                '</div>' +
                '</div>' +

                '</div>'
            );

            $percentText = $card.find('.vas-ce-percent');
            $dsoText = $card.find('.vas-ce-dso');
            $overdueText = $card.find('.vas-ce-overdue');
            $ringFg = $card.find('.vas-ce-ring-fg');

            $card.on('click', function (e) {
                e.stopPropagation();
                openDialog();
            });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-ce-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            createDialog();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-ce');
            $('body').removeClass('vas-ce-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_001_CollectionEfficiencyWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_001_CollectionEfficiencyWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_001_CollectionEfficiencyWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_001_CollectionEfficiencyWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
