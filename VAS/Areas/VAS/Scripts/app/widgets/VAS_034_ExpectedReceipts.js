/**
 * Expected Receipts Widget
 * Purpose - Show upcoming AR receipts expected from unpaid invoice pay schedules
 *           and sales-order advance pay schedules.
 *
 * Data/business logic is inherited from the Expected Payment widget: it calls the
 * shared, multi-DB, server-paged endpoint VAS/PoReceipt/GetExpectedPaymentData with
 * ISOtrx = true (AR side). That endpoint UNIONs AR invoice pay-schedules and
 * sales-order advance pay-schedules and returns an exact record count, which drives
 * the design.md footer pager. The vas-er glass visual design is retained for the card
 * and list; the filter flyout reuses the shared VIS lookup controls (Type / Financial
 * Period / Customer / From / To) opened from a funnel icon in the header.
 *
 * Each row's document number is a zoom link: it opens the source transaction through
 * VAS.ZoomUtil — sales orders in VAS_SalesOrder, AR invoices in VAS_ARInvoice. The
 * endpoint already resolves those AD_Window_IDs (Record_ID / Window_ID / Primary_ID
 * per row); the window NAMES here are only the fallback for a row that arrives with
 * Window_ID 0. The row's document type (C_DocType name) leads the metadata line, so a
 * credit memo is distinguishable from an invoice at a glance.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                 | Message Key
 * ----+------------------------------+--------------------------------
 *  1  | Expected Receipts            | VAS_034_ExpectedReceipts
 *  3  | No data                      | VIS_NoData
 *  4  | Expected                     | VIS_Expected
 * 4a  | NEFT expected                | VAS_034_NEFTExpected
 * 4b  | UPI auto-debit               | VAS_034_UPIAutoDebit
 * 4c  | Cheque expected              | VAS_034_ChequeExpected
 * 4d  | Credit expected              | VAS_034_CreditExpected
 *  5  | Previous                     | VAS_Previous
 *  6  | Next                         | VAS_Next
 *  7  | Filter                       | VAS_034_Filter
 *  8  | Type                         | VAS_DocTypeExPayDiv
 *  9  | Financial Period             | VAS_FinancialPeriodDiv
 * 10  | Customer                     | VAS_CustomerPartner
 * 11  | From Date                    | VAS_FromDate
 * 12  | To Date                      | VAS_ToDate
 * 13  | Clear                        | VAS_Clear
 * 14  | Apply                        | VAS_Apply
 * 15  | Zoom                         | VAS_Zoom
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

    VAS.VAS_034_ExpectedReceipts = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var ctx = VIS.Env.getCtx();

        var $root = $('<div class="vas-er-root">');
        var $card;
        var $listBody;
        var $footer;
        var $pageInfo;
        var $prevBtn;
        var $nextBtn;
        var $pageText;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        var $filterIconBtn;
        var $filterPanel;
        var isFilterOpen = false;

        /* Reference column ids for the lookup controls (resolved once, like the payment widget). */
        var ColumnIds = null;
        var ColumnData = [
            { refernceName: "VAS_DocTypeExPayPaymentList" },
            { refernceName: "VAS_FinancialPeriodPaymentList" },
            { ColumnName: "C_BPartner_ID" }
        ];

        /* Filter controls. */
        var vDocTypeList = null;
        var vFinancialPeriodList = null;
        var vSearchBPartner = null;
        var $FromDate = null;
        var $ToDate = null;

        /* Applied filter state (only updated on Apply). docTypeValue: 01 = invoices + order
           advances, 03 = invoices, 02 = sales orders. */
        var docTypeValue = "01";
        var financialPeriodValue = null;
        var cBPartnerId = null;
        var fromDate = "";
        var toDate = "";

        /* Zoom targets for the document-number link. The endpoint sends the AD_Window_ID
           per row; these names are the fallback when it comes back as 0 (ids differ per
           environment, names do not). resolvedId caches whatever the helper resolved so
           subsequent clicks on the same kind of record skip the lookup. */
        var ZOOM_WINDOW_ORDER = { newName: 'VAS_SalesOrder', oldName: 'Sales Order', resolvedId: 0 };
        var ZOOM_WINDOW_INVOICE = { newName: 'VAS_ARInvoice', oldName: 'Invoice (Customer)', resolvedId: 0 };

        var pageNo = 1;
        var pageSize = 6;
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

        this.Initalize = function () {
            getColumnIds();
            createWidget();
            buildFilterPanel();
            loadData();
        };

        function getColumnIds() {
            ColumnIds = VIS.dataContext.getJSONData(
                VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnIDForExpPayment",
                { "refernceName": JSON.stringify(ColumnData) },
                null
            );
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/PoReceipt/GetExpectedPaymentData',
                type: 'GET',
                data: {
                    ISOtrx: true,
                    pageNo: pageNo,
                    pageSize: pageSize,
                    FinancialPeriodValue: financialPeriodValue || "",
                    C_BPartner_ID: cBPartnerId || "",
                    fromDate: fromDate || "",
                    toDate: toDate || "",
                    docTypeValue: docTypeValue || "01"
                },
                success: function (res) {
                    var data = res;

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    setNoData();
                },
                complete: function () { showBusy(false); }
            });
        }

        function setNoData() {
            totalPages = 0;
            totalRecords = 0;

            if ($listBody) {
                $listBody.html(
                    '<div class="vas-er-nodata">' +
                    lbl("VAS_034_NoData", "No Data Found") +
                    '</div>'
                );
            }

            updatePager();
        }

        function renderData(data) {
            /* GetExpectedPaymentData returns a flat array; total count rides on each row. */
            var rows = (data && data.length) ? data : [];

            if (!$listBody) {
                return;
            }

            if (rows.length === 0) {
                setNoData();
                return;
            }

            totalRecords = Number(rows[0].recordCount || 0);
            totalPages = pageSize > 0 ? Math.ceil(totalRecords / pageSize) : 0;

            $listBody.empty();

            for (var i = 0; i < rows.length; i++) {
                $listBody.append(buildRow(rows[i], i));
            }

            updatePager();
        }

        function buildRow(row, index) {
            var documentNo = row.DocumentNo || "";
            var customerName = row.Name || "";
            var dueDateText = formatDueDate(row.OrderdDate);
            var methodName = normalizeMethodName(row.PayMethod);
            var amount = formatAmount(row);
            var barClass = getBarClass(methodName, index, row.windowType);
            var docTypeName = row.DocTypeName || "";

            /* Metadata line: document type first (invoice vs credit memo vs order),
               then due date and expected payment method. Empty parts are dropped so
               no stray separator is left behind. */
            var metaParts = [];
            if (docTypeName) { metaParts.push(docTypeName); }
            if (dueDateText) { metaParts.push(dueDateText); }
            if (methodName) { metaParts.push(methodName); }

            var $row = $(
                '<div class="vas-er-row">' +
                '<span class="vas-er-bar ' + barClass + '"></span>' +
                '<div class="vas-er-info">' +
                '<div class="vas-er-row-title"></div>' +
                '<div class="vas-er-meta" title="' + escapeHtml(metaParts.join(' · ')) + '">' +
                escapeHtml(metaParts.join(' · ')) +
                '</div>' +
                '</div>' +
                '<span class="vas-er-amount">' + escapeHtml(amount) + '</span>' +
                '</div>'
            );

            $row.find('.vas-er-row-title')
                .append(buildDocumentNoCell(row, documentNo))
                .append(document.createTextNode(customerName ? ' · ' + customerName : ''))
                .attr('title', documentNo + (customerName ? ' · ' + customerName : ''));

            return $row;
        }

        /* The document number is a zoom link to the source transaction. A row whose
           record cannot be identified degrades to plain text — never a dead link. */
        function buildDocumentNoCell(row, documentNo) {
            var target = getZoomTarget(row);

            if (!documentNo || !target) {
                return $('<span>').text(documentNo);
            }

            var $link = $('<a class="vas-er-doclink" role="link" tabindex="0">')
                .attr('title', lbl("VAS_Zoom", "Zoom") + ': ' + documentNo)
                .text(documentNo);

            $link.on('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                zoomToRecord(target);
            });

            /* An <a> without href gets no implicit key activation. */
            $link.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
                    e.preventDefault();
                    e.stopPropagation();
                    zoomToRecord(target);
                }
            });

            return $link;
        }

        /* Builds the zoom descriptor for a row from what the endpoint already sends
           (Record_ID / Primary_ID / Window_ID). Window_ID may arrive as 0 — the window
           names are the fallback the shared helper resolves against. */
        function getZoomTarget(row) {
            var recordId = Number(row && row.Record_ID || 0);

            if (!recordId || isNaN(recordId) || recordId <= 0) {
                return null;
            }

            var isOrder = row.windowType === "Order";
            var primaryColumn = row.Primary_ID || (isOrder ? "C_Order_ID" : "C_Invoice_ID");
            var names = isOrder ? ZOOM_WINDOW_ORDER : ZOOM_WINDOW_INVOICE;

            return {
                recordId: recordId,
                primaryColumn: primaryColumn,
                windowId: Number(row.Window_ID || 0) || 0,
                names: names
            };
        }

        function zoomToRecord(target) {
            if (!target || !VAS.ZoomUtil) {
                return;
            }

            /* Zoom is best-effort: an unresolvable window simply does not navigate. */
            try {
                VAS.ZoomUtil.zoomToRecord(
                    target.primaryColumn,
                    target.recordId,
                    target.windowId || target.names.resolvedId,
                    target.names.newName,
                    target.names.oldName
                ).done(function (windowId) {
                    /* Cache the resolved id so later clicks skip the lookup. */
                    if (windowId > 0) {
                        target.names.resolvedId = windowId;
                    }
                });
            }
            catch (e) { /* zoom is best-effort */ }
        }

        function normalizeMethodName(name) {
            if (!name) {
                return lbl("VIS_Expected", "Expected");
            }

            var n = name.toString().trim();

            if (n === "Bank Transfer") {
                return lbl("VAS_034_NEFTExpected", "NEFT expected");
            }

            if (n === "Direct Debit") {
                return lbl("VAS_034_UPIAutoDebit", "UPI auto-debit");
            }

            if (n === "Cheque" || n === "Check") {
                return lbl("VAS_034_ChequeExpected", "Cheque expected");
            }

            if (n === "On Credit") {
                return lbl("VAS_034_CreditExpected", "Credit expected");
            }

            return n;
        }

        function getBarClass(methodName, index, windowType) {
            var value = (methodName || "").toLowerCase();

            if (value.indexOf("upi") >= 0 || value.indexOf("debit") >= 0) {
                return "upi";
            }

            if (value.indexOf("cheque") >= 0 || value.indexOf("check") >= 0) {
                return "card";
            }

            if (value.indexOf("bank") >= 0 || value.indexOf("neft") >= 0 || value.indexOf("rtgs") >= 0) {
                return "neft";
            }

            /* Sales-order advances get a distinct tint from invoices. */
            if (windowType === "Order") {
                return "card";
            }

            return index % 2 === 0 ? "neft" : "upi";
        }

        function formatDueDate(value) {
            if (!value) {
                return "";
            }

            var d = new Date(value);

            if (isNaN(d.getTime())) {
                return value;
            }

            return d.toLocaleDateString(window.navigator.language, {
                weekday: "short",
                day: "2-digit",
                month: "short",
                year: "numeric"
            });
        }

        function formatAmount(row) {
            /* Use the raw signed value to determine the sign; format the magnitude only. */
            var rawVal = Number(row && row.TotalAmt || 0);
            var sign = rawVal < 0 ? '-' : '';
            var absVal = Math.abs(rawVal);
            var precision = Number(row && row.stdPrecision || 0);

            var text = absVal.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            var symbol = row && row.Symbol ? row.Symbol.toString() : "";

            if (!symbol) {
                /* No symbol: sign then magnitude. */
                return sign + text;
            }

            /* 3-char ISO codes read better after the amount; glyph symbols before.
               Sign is always the very first character, before the currency symbol. */
            return symbol.length === 3 ? (sign + text + " " + symbol) : (sign + symbol + text);
        }

        function updatePager() {
            /* Right pager: "X of N" between the arrows. */
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + " " + lbl("VAS_Of", "of") + " " + totalPages) : "");
            }

            /* Left helper: "Showing X–Y of Z" result range. */
            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_Showing", "Showing") + " " + from + "–" + to + " " + lbl("VAS_Of", "of") + " " + totalRecords);
                }
                else {
                    $pageInfo.text("");
                }
            }

            if ($prevBtn) {
                $prevBtn.prop("disabled", pageNo <= 1 || totalPages <= 1);
            }

            if ($nextBtn) {
                $nextBtn.prop("disabled", totalPages <= 1 || pageNo >= totalPages);
            }

            if ($footer) {
                $footer.css("display", totalPages > 1 ? "flex" : "none");
            }
        }

        function toInputDate(date) {
            var y = date.getFullYear();
            var m = String(date.getMonth() + 1).padStart(2, "0");
            var d = String(date.getDate()).padStart(2, "0");

            return y + "-" + m + "-" + d;
        }

        function getDateParam(ctrl) {
            if (!ctrl) {
                return "";
            }

            var v = ctrl.getValue();

            if (v === null || v === undefined || v === "") {
                return "";
            }

            var d = new Date(v);

            return isNaN(d.getTime()) ? "" : toInputDate(d);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function createWidget() {
            /* Footer-pager chevrons (design.md "Widget Footer Pager"). */
            var chevL = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

            /* Funnel/filter icon that opens the filter flyout. */
            var funnel = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"></polygon></svg>';

            $card = $(
                '<div class="vas-er-card">' +

                '<div class="vas-er-head">' +
                '<div class="vas-er-head-left">' +
                '<div class="vas-er-title-line">' +
                '<span class="vas-er-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>' +
                '<line x1="16" y1="2" x2="16" y2="6"></line>' +
                '<line x1="8" y1="2" x2="8" y2="6"></line>' +
                '<line x1="3" y1="10" x2="21" y2="10"></line>' +
                '</svg>' +
                '</span>' +
                '<div class="vas-er-title-group">' +
                '<span class="vas-er-title">' +
                lbl("VAS_034_ExpectedReceipts", "Expected Receipts") +
                '</span>' +
                '<span class="vas-er-subtitle">' +
                lbl("VAS_034_ReceiptsExpectedSoon", "Receipts Expected Soon") +
                '</span>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="vas-er-filter-icon-btn" aria-label="' + lbl("VAS_034_Filter", "Filter") + '">' +
                funnel +
                '</button>' +
                '</div>' +

                '<div class="vas-er-body"></div>' +

                /* Footer pager (design.md): left "Showing X–Y of Z" helper + right compact pager. */
                '<div class="vas-er-footer" style="display:none;">' +
                '<span class="vas-er-footer-info"></span>' +
                '<div class="vas-er-pager">' +
                '<button type="button" class="vas-er-page-btn vas-er-prev" aria-label="' + lbl("VAS_Previous", "Previous") + '">' + chevL + '</button>' +
                '<span class="vas-er-page-text"></span>' +
                '<button type="button" class="vas-er-page-btn vas-er-next" aria-label="' + lbl("VAS_Next", "Next") + '">' + chevR + '</button>' +
                '</div>' +
                '</div>' +

                '</div>'
            );

            $listBody = $card.find('.vas-er-body');
            $footer = $card.find('.vas-er-footer');
            $pageInfo = $card.find('.vas-er-footer-info');
            $prevBtn = $card.find('.vas-er-prev');
            $nextBtn = $card.find('.vas-er-next');
            $pageText = $card.find('.vas-er-page-text');
            $filterIconBtn = $card.find('.vas-er-filter-icon-btn');

            /* Funnel toggles the flyout; clicking again closes it without applying. */
            $filterIconBtn.on('click', function () {
                isFilterOpen = !isFilterOpen;

                if (isFilterOpen) {
                    $filterPanel.show();
                    $filterIconBtn.addClass('active');
                }
                else {
                    $filterPanel.hide();
                    $filterIconBtn.removeClass('active');
                }
            });

            $prevBtn.on('click', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                loadData();
            });

            $nextBtn.on('click', function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadData();
            });

            $root.append($card);

            /* Busy/loading overlay over the card (core spinner classes). Hidden until a fetch runs. */
            $busy = $('<div class="vas-er-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        /* Builds the filter flyout once, reusing the shared VIS lookup controls so it
           matches the Expected Payment filter. Customers only (IsCustomer = 'Y'). */
        function buildFilterPanel() {
            $filterPanel = $('<div class="vas-er-filter-flyout vas-expay-filter-flyout" style="display:none;">');

            /* Type (document type) list. */
            var typeDiv = $('<div class="VAS-DocTypeExPayListDiv">');
            var $typeInner = $('<div class="input-group vis-input-wrap">');
            var typeLookup = VIS.MLookupFactory.get(ctx, $self.windowNo, 0, VIS.DisplayType.List, "VAS_DocTypeExPayPaymentList", ColumnIds.VAS_DocTypeExPayPaymentList, false);
            vDocTypeList = new VIS.Controls.VComboBox("VAS_DocTypeExPayPaymentList", true, false, true, typeLookup, 20);
            vDocTypeList.setValue("01");
            var $typeWrap = $('<div class="vis-control-wrap">');
            $typeInner.append($typeWrap);
            $typeWrap
                .append(vDocTypeList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '))
                .append('<label style="top:-9px;background-color:#FFFFFF;background-image:linear-gradient(rgba(var(--v-c-on-secondary),.03),rgba(var(--v-c-on-secondary),.03));color:black;">' + lbl("VAS_DocTypeExPayDiv", "Type") + '</label>');
            typeDiv.append($typeInner);

            /* Financial period list. */
            var fpDiv = $('<div class="VAS-FinancialPeriodListDiv">');
            var $fpInner = $('<div class="input-group vis-input-wrap">');
            var fpLookup = VIS.MLookupFactory.get(ctx, $self.windowNo, 0, VIS.DisplayType.List, "VAS_FinancialPeriodPaymentList", ColumnIds.VAS_FinancialPeriodPaymentList, false);
            vFinancialPeriodList = new VIS.Controls.VComboBox("VAS_FinancialPeriodPaymentList", false, false, true, fpLookup, 20);
            var $fpWrap = $('<div class="vis-control-wrap">');
            $fpInner.append($fpWrap);
            $fpWrap
                .append(vFinancialPeriodList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '))
                .append('<label style="top:-9px;background-color:#FFFFFF;background-image:linear-gradient(rgba(var(--v-c-on-secondary),.03),rgba(var(--v-c-on-secondary),.03));color:black;">' + lbl("VAS_FinancialPeriodDiv", "Financial Period") + '</label>');
            fpDiv.append($fpInner);

            /* Customer search. */
            var bpValidation = "C_BPartner.IsActive='Y' AND C_BPartner.IsCustomer = 'Y' AND C_BPartner.IsSummary = 'N'";
            var bpDiv = $('<div class="vas-BPartnerDiv">');
            var $bpInner = $('<div class="input-group vis-input-wrap">');
            var bpLookup = VIS.MLookupFactory.get(ctx, $self.windowNo, ColumnIds.C_BPartner_ID, VIS.DisplayType.Search, "C_BPartner_ID", 0, false, bpValidation);
            vSearchBPartner = new VIS.Controls.VTextBoxButton("C_BPartner_ID", false, false, true, VIS.DisplayType.Search, bpLookup);
            vSearchBPartner.setCustomInfo('VAS_Customer');
            var $bpWrap = $('<div class="vis-control-wrap">');
            var $bpBtnWrap = $('<div class="input-group-append">');
            $bpInner.append($bpWrap);
            $bpWrap
                .append(vSearchBPartner.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '))
                .append('<label class="vas-exinvd-bpLabel" style="color:#000; left: 9px;">' + lbl("VAS_CustomerPartner", "Customer") + '</label>');
            $bpBtnWrap.append(vSearchBPartner.getBtn(0));
            $bpInner.append($bpBtnWrap);
            bpDiv.append($bpInner);
            vSearchBPartner.fireValueChanged = function () {
                cBPartnerId = vSearchBPartner.value;
            };

            /* From / To dates. */
            var fromDiv = $('<div class="vas-expay-dateWidth vas-FromdateDiv">');
            var $fromInner = $('<div class="input-group vis-input-wrap">');
            $FromDate = new VIS.Controls.VDate("DateReport", false, false, true, VIS.DisplayType.Date, "DateReport");
            var $fromWrap = $('<div class="vis-control-wrap">');
            $fromInner.append($fromWrap);
            $fromWrap
                .append($FromDate.getControl().attr('placeholder', ' ').attr('data-placeholder', ''))
                .append('<label class="vas-expay-lablels" style="color:black;">' + lbl("VAS_FromDate", "From Date") + '</label>');
            fromDiv.append($fromInner);

            var toDiv = $('<div class="vas-expay-dateWidth vas-todateDiv">');
            var $toInner = $('<div class="input-group vis-input-wrap">');
            $ToDate = new VIS.Controls.VDate("DateReport", false, false, true, VIS.DisplayType.Date, "DateReport");
            var $toWrap = $('<div class="vis-control-wrap">');
            $toInner.append($toWrap);
            $toWrap
                .append($ToDate.getControl().attr('placeholder', ' ').attr('data-placeholder', ''))
                .append('<label class="vas-expay-lablels" style="color:black;">' + lbl("VAS_ToDate", "To Date") + '</label>');
            toDiv.append($toInner);

            var $datesDiv = $('<div class="vas-expay-datefilter">');
            $datesDiv.append(fromDiv).append(toDiv);

            /* Clear / Apply. */
            var $btnDiv = $('<div class="vas-expay-btndiv">');
            var $clear = $('<button type="button" class="VIS_Pref_btn-2 vas-expay-filtbtn vas-er-clear-btn">' + lbl("VAS_Clear", "Clear") + '</button>');
            var $apply = $('<button type="button" class="VIS_Pref_btn-2 vas-expay-filtbtn vas-er-apply-btn">' + lbl("VAS_Apply", "Apply") + '</button>');
            $btnDiv.append($clear).append($apply);

            $filterPanel
                .append(typeDiv)
                .append(fpDiv)
                .append(bpDiv)
                .append($datesDiv)
                .append($btnDiv);

            /* Clear resets the filter inputs only; it neither applies nor closes the flyout. */
            $clear.on('click', function () {
                vDocTypeList.setValue("01");
                vFinancialPeriodList.setValue(null);
                vSearchBPartner.setValue(null);
                $FromDate.setValue(null);
                $ToDate.setValue(null);
                cBPartnerId = null;
            });

            /* Apply commits the filter and closes the flyout. */
            $apply.on('click', function () {
                if ($FromDate.getValue() && $ToDate.getValue() && $FromDate.getValue() > $ToDate.getValue()) {
                    VIS.ADialog.info('VAS_PlzEnterCorrectDate');
                    $ToDate.setValue(null);
                    return;
                }

                docTypeValue = vDocTypeList.getValue() || "01";
                financialPeriodValue = vFinancialPeriodList.getValue();
                cBPartnerId = vSearchBPartner.value;
                fromDate = getDateParam($FromDate);
                toDate = getDateParam($ToDate);

                pageNo = 1;
                isFilterOpen = false;
                $filterPanel.hide();
                $filterIconBtn.removeClass('active');
                loadData();
            });

            $card.append($filterPanel);
        }

        this.refreshWidget = function () {
            pageNo = 1;

            docTypeValue = "01";
            financialPeriodValue = null;
            cBPartnerId = null;
            fromDate = "";
            toDate = "";

            if (vDocTypeList) { vDocTypeList.setValue("01"); }
            if (vFinancialPeriodList) { vFinancialPeriodList.setValue(null); }
            if (vSearchBPartner) { vSearchBPartner.setValue(null); }
            if ($FromDate) { $FromDate.setValue(null); }
            if ($ToDate) { $ToDate.setValue(null); }

            isFilterOpen = false;
            if ($filterPanel) { $filterPanel.hide(); }
            if ($filterIconBtn) { $filterIconBtn.removeClass('active'); }

            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_034_ExpectedReceipts.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_034_ExpectedReceipts.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_034_ExpectedReceipts.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_034_ExpectedReceipts.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
