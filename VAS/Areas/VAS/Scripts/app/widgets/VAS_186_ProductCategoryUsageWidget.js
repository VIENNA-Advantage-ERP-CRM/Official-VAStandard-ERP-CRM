/**
 * VAS_186_ProductCategoryUsageWidget
 * 4x2 Horizontal Bar Chart Widget for Inventory Use dashboard.
 * Displays internal-use consumption grouped by product category for selected period,
 * toggleable between Quantity and Value, capped at 10 bars with reconciling footnote,
 * and Category Drill-down Modal.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Product Category Usage                 | VAS_186_ProductCategoryUsage
 *  2 | Internal use by category              | VAS_186_InternalUseByCategory
 *  3 | Qty                                    | VAS_186_Qty
 *  4 | Value                                  | VAS_186_Value
 *  5 | Couldn't load                           | VAS_186_CouldntLoad
 *  6 | Products issued                        | VAS_186_ProductsIssued
 *  7 | Close                                  | VAS_186_Close
 *  8 | Doc No.                                | VAS_186_DocNo
 *  9 | Product                                | VAS_186_Product
 * 10 | UoM                                    | VAS_186_UoM
 * 11 | WH + Loc                               | VAS_186_WarehouseLocator
 * 12 | Date                                   | VAS_186_Date
 * 13 | Each                                   | VAS_186_Each
 * 14 | Loading...                             | VAS_186_Loading
 * 15 | No lines for this period.              | VAS_186_NoLinesForPeriod
 * 16 | of                                     | VAS_186_Of
 * 17 | Page                                   | VAS_186_Page
 * 18 | lines                                  | VAS_186_Lines
 * 19 | categories                             | VAS_186_Categories
 * 20 | categories shown                       | VAS_186_CategoriesShown
 * 21 | Others                                 | VAS_186_Others
 * 22 | All                                    | VAS_186_All
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Design height of one category row, in em against the body's font size.
       MUST stay in sync with  min-height: 2em  on .vas-pcu-row in
       VAS_186_ProductCategoryUsageWidget.css - the stylesheet enforces the floor, this constant
       decides how many rows fit. See recalcPageSize() for why the rendered height is not used. */
    var VAS_PCU_ROW_MIN_EM = 2;

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

    VAS.VAS_186_ProductCategoryUsageWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-pcu-root">');
        var $card;
        var $body;
        var $footnote;
        var $pagerText;
        var $prevBtn;
        var $nextBtn;
        var $monthSelect;
        var $yearSelect;
        var $qtyPill;
        var $valPill;
        var $busy;
        var $modal;

        var selectedMonth = DateTimeNowMonth();
        var selectedYear = DateTimeNowYear();
        var activeMeasure = "qty"; // "qty" or "val"
        var categoriesData = [];
        // Starting guess only: recalcPageSize() replaces this with however many bars actually fit.
        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 1;
        var isRefitting = false;

        function DateTimeNowMonth() { return new Date().getMonth() + 1; }
        function DateTimeNowYear() { return new Date().getFullYear(); }

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

// ===== NEW CODE START — currency format (agent A08, 2026-08-19) =====
        var currencyIso = "";
        var currencySymbol = "";
        var indianIsos = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function isIndianIso(iso) {
            if (!iso) { return false; }
            return indianIsos.indexOf(String(iso).toUpperCase()) !== -1;
        }

        function formatQty(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatCurrency(value) {
            var val = Number(value || 0);
            if (isNaN(val)) { val = 0; }
            var sym = currencySymbol || currencyIso || "";
            var iso = (currencyIso || "").toUpperCase();
            var isIndian = isIndianIso(iso);

            var formatted = "";
            var absVal = Math.abs(val);

            if (isIndian) {
                if (absVal >= 10000000) {
                    formatted = (val / 10000000).toFixed(1) + 'Cr';
                } else if (absVal >= 100000) {
                    formatted = (val / 100000).toFixed(1) + 'L';
                } else if (absVal >= 1000) {
                    formatted = (val / 1000).toFixed(1) + 'k';
                } else {
                    formatted = val.toLocaleString('en-IN');
                }
            } else {
                if (absVal >= 1000000000) {
                    formatted = (val / 1000000000).toFixed(1) + 'B';
                } else if (absVal >= 1000000) {
                    formatted = (val / 1000000).toFixed(1) + 'M';
                } else if (absVal >= 1000) {
                    formatted = (val / 1000).toFixed(1) + 'k';
                } else {
                    formatted = val.toLocaleString(window.navigator.language);
                }
            }

            if (sym) {
                return sym + (sym.length > 1 && !sym.match(/^[₹$€£¥]$/) ? " " : "") + formatted;
            }
            return formatted;
        }

        function formatCurrencyExact(value) {
            var val = Number(value || 0);
            if (isNaN(val)) { val = 0; }
            var sym = currencySymbol || currencyIso || "";
            var iso = (currencyIso || "").toUpperCase();
            var isIndian = isIndianIso(iso);
            var formatted = isIndian ? val.toLocaleString('en-IN') : val.toLocaleString(window.navigator.language);
            if (sym) {
                return sym + (sym.length > 1 && !sym.match(/^[₹$€£¥]$/) ? " " : "") + formatted;
            }
            return formatted;
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function formatQty(value) {
//            var n = Number(value || 0);
//            return n.toLocaleString(window.navigator.language);
//        }
//
//        function formatINR(value) {
//            var val = Number(value || 0);
//            if (val >= 100000) {
//                return '₹' + (val / 100000).toFixed(1) + 'L';
//            } else if (val >= 1000) {
//                return '₹' + (val / 1000).toFixed(1) + 'k';
//            }
//            return '₹' + val.toLocaleString(window.navigator.language);
//        }
// ----- END OLD CODE -----

        function formatMonthName(m) {
            var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            return monthNames[Math.max(0, Math.min(11, m - 1))];
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-pcu-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadCategoryUsage();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                    /* Resizing changes both the track height and the clamp() font size, so the
                       number of bars that fit can change in either direction. */
                    if (!isRefitting && categoriesData.length > 0) {
                        isRefitting = true;
                        var changed = recalcPageSize();
                        isRefitting = false;
                        if (changed) { renderCategories(); }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

// ===== NEW CODE START — currency format (agent A08, 2026-08-19) =====
        function loadCategoryUsage() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_186_ProductCategoryUsageWidget/GetCategoryUsage',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    categoriesData = data.categories || [];
                    if (data.currency) {
                        currencyIso = data.currency.iso || "";
                        currencySymbol = data.currency.symbol || "";
                    }
                    pageNo = 1;
                    renderCategories();
                },
                error: function () {
                    categoriesData = [];
                    pageNo = 1;
                    renderCategories();
                },
                complete: function () { showBusy(false); }
            });
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function loadCategoryUsage() {
//            showBusy(true);
//
//            $.ajax({
//                url: VIS.Application.contextUrl + 'VAS_186_ProductCategoryUsageWidget/GetCategoryUsage',
//                type: 'GET',
//                data: { month: selectedMonth, year: selectedYear },
//                cache: false,
//                success: function (res) {
//                    var data = parseResponse(res);
//                    categoriesData = data.categories || [];
//                    pageNo = 1;
//                    renderCategories();
//                },
//                error: function () {
//                    categoriesData = [];
//                    pageNo = 1;
//                    renderCategories();
//                },
//                complete: function () { showBusy(false); }
//            });
//        }
// ----- END OLD CODE -----

        function renderCategories() {
            if (!$body) { return; }

            if (categoriesData.length === 0) {
                $body.html('<div class="vas-pcu-empty">No usage recorded for ' + escapeHtml(formatMonthName(selectedMonth) + ' ' + selectedYear) + '.</div>');
                if ($footnote) { $footnote.addClass('vas-pcu-hidden'); }
                return;
            }

            if ($footnote) { $footnote.removeClass('vas-pcu-hidden'); }

            // Sort categories by active measure
            categoriesData.sort(function (a, b) {
                var valA = activeMeasure === "val" ? a.totalValue : a.totalQty;
                var valB = activeMeasure === "val" ? b.totalValue : b.totalQty;
                return valB - valA;
            });

            // Calculate Period Totals across all categories
            var periodTotalQty = 0;
            var periodTotalVal = 0;
            for (var c = 0; c < categoriesData.length; c++) {
                periodTotalQty += categoriesData[c].totalQty;
                periodTotalVal += categoriesData[c].totalValue;
            }

            var activePeriodTotal = activeMeasure === "val" ? periodTotalVal : periodTotalQty;
            var topCategoryMeasure = activeMeasure === "val" ? categoriesData[0].totalValue : categoriesData[0].totalQty;
            if (topCategoryMeasure <= 0) { topCategoryMeasure = 1; }

            /* Paginate. Previously every category was written into the body at once; the body has
               overflow:hidden, so anything past the visible height was silently clipped while the
               footnote still claimed all of them were shown. */
            totalPages = Math.max(1, Math.ceil(categoriesData.length / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(categoriesData.length, startIndex + pageSize);
            var rowsHtml = '';
            var shownTotalMeasure = 0;

            for (var i = startIndex; i < endIndex; i++) {
                var cat = categoriesData[i];
                var catMeasure = activeMeasure === "val" ? cat.totalValue : cat.totalQty;
                shownTotalMeasure += catMeasure;

                var barPct = Math.max(3, Math.round((catMeasure / topCategoryMeasure) * 100));
                var sharePct = activePeriodTotal > 0 ? Math.round((catMeasure / activePeriodTotal) * 100) : 0;
// ===== NEW CODE START — currency format (agent A08, 2026-08-19) =====
                var measureStr = activeMeasure === "val" ? formatCurrency(cat.totalValue) : formatQty(cat.totalQty);
                var exactMeasureStr = activeMeasure === "val" ? formatCurrencyExact(cat.totalValue) : formatQty(cat.totalQty);

                rowsHtml +=
                    '<button type="button" class="vas-pcu-row" data-catid="' + cat.categoryId + '" data-catname="' + escapeHtml(cat.categoryName) + '">' +
                    '<div class="vas-pcu-cat-name" title="' + escapeHtml(cat.categoryName) + '">' + escapeHtml(cat.categoryName) + '</div>' +
                    '<div class="vas-pcu-bar-track"><div class="vas-pcu-bar-fill" style="width:' + barPct + '%;"></div></div>' +
                    '<div class="vas-pcu-measure-num" title="' + escapeHtml(exactMeasureStr) + '">' + escapeHtml(measureStr) + '</div>' +
                    '<div class="vas-pcu-share-pct">' + sharePct + '%</div>' +
                    '</button>';
            }

            $body.html(rowsHtml);

            // Footnote counts the bars actually on screen, so it can no longer overstate the page.
            var shownCount = endIndex - startIndex;
            if (categoriesData.length > shownCount) {
                var restMeasure = activePeriodTotal - shownTotalMeasure;
                var restPct = activePeriodTotal > 0 ? Math.round((restMeasure / activePeriodTotal) * 100) : 0;
                var restStr = activeMeasure === "val" ? formatCurrency(restMeasure) : (formatQty(restMeasure) + ' units');
                $footnote.text(shownCount + ' ' + label("VAS_186_Of", "of") + ' ' + categoriesData.length +
                    ' ' + label("VAS_186_Categories", "categories") + ' · ' +
                    label("VAS_186_Others", "Others") + ': ' + restStr + ' (' + restPct + '%)');
            } else {
                $footnote.text(label("VAS_186_All", "All") + ' ' + categoriesData.length + ' ' +
                    label("VAS_186_CategoriesShown", "categories shown"));
            }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                var measureStr = activeMeasure === "val" ? formatINR(cat.totalValue) : formatQty(cat.totalQty);
//
//                rowsHtml +=
//                    '<button type="button" class="vas-pcu-row" data-catid="' + cat.categoryId + '" data-catname="' + escapeHtml(cat.categoryName) + '">' +
//                    '<div class="vas-pcu-cat-name" title="' + escapeHtml(cat.categoryName) + '">' + escapeHtml(cat.categoryName) + '</div>' +
//                    '<div class="vas-pcu-bar-track"><div class="vas-pcu-bar-fill" style="width:' + barPct + '%;"></div></div>' +
//                    '<div class="vas-pcu-measure-num" title="' + escapeHtml(measureStr) + '">' + escapeHtml(measureStr) + '</div>' +
//                    '<div class="vas-pcu-share-pct">' + sharePct + '%</div>' +
//                    '</button>';
//            }
//
//            $body.html(rowsHtml);
//
//            // Footnote counts the bars actually on screen, so it can no longer overstate the page.
//            var shownCount = endIndex - startIndex;
//            if (categoriesData.length > shownCount) {
//                var restMeasure = activePeriodTotal - shownTotalMeasure;
//                var restPct = activePeriodTotal > 0 ? Math.round((restMeasure / activePeriodTotal) * 100) : 0;
//                var restStr = activeMeasure === "val" ? formatINR(restMeasure) : (formatQty(restMeasure) + ' units');
//                $footnote.text(shownCount + ' ' + label("VAS_Of", "of") + ' ' + categoriesData.length +
//                    ' ' + label("VAS_Categories", "categories") + ' · ' +
//                    label("VAS_Others", "Others") + ': ' + restStr + ' (' + restPct + '%)');
//            } else {
//                $footnote.text(label("VAS_All", "All") + ' ' + categoriesData.length + ' ' +
//                    label("VAS_CategoriesShown", "categories shown"));
//            }
// ----- END OLD CODE -----

            if ($pagerText) {
                $pagerText.text(label("VAS_186_Page", "Page") + ' ' + pageNo + ' ' + label("VAS_186_Of", "of") + ' ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }

            /* Bars are on screen now, so their real height is measurable. If the body can hold a
               different number than we just drew, adopt it and redraw once. recalcPageSize only
               reports a change when the value actually differs, so this cannot loop. */
            if (!isRefitting) {
                isRefitting = true;
                if (recalcPageSize()) { renderCategories(); }
                isRefitting = false;
            }
        }

        /* Fit as many bars as the body can hold instead of a fixed count, so the widget fills its
           grid track at any dashboard size / display resolution. Row height is measured from a
           rendered row rather than assumed, because it scales with the card's clamp() font size.
           Returns true when the page size changed and a re-render is needed. */
        function recalcPageSize() {
            if (!$body || !$body[0]) { return false; }

            var bodyH = $body[0].clientHeight;
            if (bodyH <= 0) { return false; }

            /* Rows are flex: 1 1 0 so they STRETCH to fill the body. Measuring a rendered row
               would therefore measure the stretch, not the content, and feed that back into this
               calculation - a loop that locks the row count to whatever it happened to be first.
               Use the design row height instead (VAS_PCU_ROW_MIN_EM, mirrored by min-height on
               .vas-pcu-row in the stylesheet) so the count depends only on the body height. */
            var bodyFontPx = parseFloat(window.getComputedStyle($body[0]).fontSize) || 16;
            var rowH = VAS_PCU_ROW_MIN_EM * bodyFontPx;
            if (rowH <= 0) { return false; }

            // .vas-pcu-body uses a .25em flex gap, which sits between rows but not after the last.
            var gap = parseFloat(window.getComputedStyle($body[0]).rowGap) || 0;
            var fits = Math.max(1, Math.floor((bodyH + gap + 0.5) / (rowH + gap)));
            if (fits === pageSize) { return false; }

            pageSize = fits;
            return true;
        }

        function openCategoryDrilldownModal(categoryId, categoryName) {
            $(document).off("keydown.vas-pcu-modal"); if ($modal) { $modal.remove(); }

            var monthFull = formatMonthName(selectedMonth) + ' ' + selectedYear;

            var showingNone = '0 ' + label("VAS_186_Of", "of") + ' 0 ' + label("VAS_186_Lines", "lines");

            $modal = $(
                '<div class="vas-pcu-modal-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-pcu-modal-card">' +
                '<div class="vas-pcu-modal-head">' +
                '<div class="vas-pcu-modal-headtext">' +
                '<h3 class="vas-pcu-modal-title" title="' + escapeHtml(categoryName) + '">' + escapeHtml(categoryName) + '</h3>' +
                '<div class="vas-pcu-modal-sub">' + escapeHtml(label("VAS_186_ProductsIssued", "Products issued") + ' · ' + monthFull) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-pcu-modal-close" aria-label="' + escapeHtml(label("VAS_186_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-pcu-modal-body">' +
                '<table class="vas-pcu-lines-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(label("VAS_186_DocNo", "Doc No.")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_186_Product", "Product")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_186_UoM", "UoM")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_186_WarehouseLocator", "WH + Loc")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_186_Qty", "Qty")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_186_Date", "Date")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-pcu-m-tbody"><tr><td colspan="6" class="vas-pcu-m-msgcell">' + escapeHtml(label("VAS_186_Loading", "Loading...")) + '</td></tr></tbody>' +
                '</table>' +
                '<div class="vas-pcu-modal-foot">' +
                '<div class="vas-pcu-m-helper">' + escapeHtml(showingNone) + '</div>' +
                '<div class="vas-pcu-modal-pager">' +
                '<button type="button" class="vas-pcu-pager-btn vas-pcu-m-prev" disabled>&lsaquo;</button>' +
                '<span class="vas-pcu-m-pager-txt">' + escapeHtml(label("VAS_186_Page", "Page") + ' 1 ' + label("VAS_186_Of", "of") + ' 1') + '</span>' +
                '<button type="button" class="vas-pcu-pager-btn vas-pcu-m-next" disabled>&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            var issueLines = [];
            var mPageNo = 1;
            var mPageSize = 6;

            function renderLinesTable() {
                var mTotalPages = Math.max(1, Math.ceil(issueLines.length / mPageSize));
                if (mPageNo > mTotalPages) { mPageNo = mTotalPages; }

                var $tbody = $modal.find('.vas-pcu-m-tbody');
                var $mHelper = $modal.find('.vas-pcu-m-helper');
                var $mPagerTxt = $modal.find('.vas-pcu-m-pager-txt');
                var $mPrev = $modal.find('.vas-pcu-m-prev');
                var $mNext = $modal.find('.vas-pcu-m-next');

                var ofTxt = label("VAS_186_Of", "of");

                /* The popup is exactly one page tall, so every page must render mPageSize rows.
                   Pages holding fewer records are padded with spacer rows to stop the modal from
                   shrinking (and the footer from jumping) on the last page or an empty result. */
                function fillerRows(count) {
                    var html = '';
                    for (var f = 0; f < count; f++) {
                        html += '<tr class="vas-pcu-m-filler" aria-hidden="true"><td colspan="6">&nbsp;</td></tr>';
                    }
                    return html;
                }

                /* Footer wording: the left side counts the rows visible on THIS page, the pager
                   counts pages. The previous "Showing 1-6 of 16" read as a promise of how many
                   rows were on screen, because both the range start and the grand total are
                   numbers larger than the six rows actually rendered. */
                if (issueLines.length === 0) {
                    $tbody.html('<tr><td colspan="6" class="vas-pcu-m-msgcell">' +
                        escapeHtml(label("VAS_186_NoLinesForPeriod", "No lines for this period.")) + '</td></tr>' +
                        fillerRows(mPageSize - 1));
                    $mHelper.text('0 ' + ofTxt + ' 0 ' + label("VAS_186_Lines", "lines"));
                    $mPagerTxt.text(label("VAS_186_Page", "Page") + ' 1 ' + ofTxt + ' 1');
                    $mPrev.prop('disabled', true);
                    $mNext.prop('disabled', true);
                    return;
                }

                var mStart = (mPageNo - 1) * mPageSize;
                var mEnd = Math.min(issueLines.length, mStart + mPageSize);
                var tbodyHtml = '';

                for (var j = mStart; j < mEnd; j++) {
                    var rec = issueLines[j];
                    tbodyHtml +=
                        '<tr>' +
                        '<td title="' + escapeHtml(rec.documentNo) + '">' + escapeHtml(rec.documentNo) + '</td>' +
                        '<td title="' + escapeHtml(rec.productName) + '">' +
                        '<span class="vas-pcu-m-product">' + escapeHtml(rec.productName) + '</span>' +
                        (rec.attribute ? ('<span class="vas-pcu-m-attr">' + escapeHtml(rec.attribute) + '</span>') : '') +
                        '</td>' +
                        '<td>' + escapeHtml(rec.uomName || label("VAS_186_Each", "Each")) + '</td>' +
                        '<td title="' + escapeHtml(rec.whLoc) + '">' + escapeHtml(rec.whLoc) + '</td>' +
                        '<td>' + escapeHtml(formatQty(rec.qty)) + '</td>' +
                        '<td>' + escapeHtml(rec.movementDate) + '</td>' +
                        '</tr>';
                }

                $tbody.html(tbodyHtml + fillerRows(mPageSize - (mEnd - mStart)));
                $mHelper.text((mEnd - mStart) + ' ' + ofTxt + ' ' + issueLines.length + ' ' + label("VAS_186_Lines", "lines"));
                $mPagerTxt.text(label("VAS_186_Page", "Page") + ' ' + mPageNo + ' ' + ofTxt + ' ' + mTotalPages);
                $mPrev.prop('disabled', mPageNo <= 1);
                $mNext.prop('disabled', mPageNo >= mTotalPages);
            }

            /* The close button and the scrim need separate handlers. Binding them together under
               an `e.target === this` guard made the button a dead zone: it contains an <svg> that
               fills it, so e.target is the icon and never the button, and the guard rejected the
               click unless it landed on the thin padding ring. The same call also tried to match
               the overlay with .find(), which only searches descendants -- $modal IS the overlay,
               so the scrim click never bound at all. */
            function closeModal() {
                $(document).off('keydown.vas-pcu-modal');
                if ($modal) { $modal.remove(); }
            }

            $modal.find('.vas-pcu-modal-close').on('click', function (e) {
                e.stopPropagation();
                closeModal();
            });

            $modal.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas-pcu-modal', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            $modal.find('.vas-pcu-m-prev').on('click', function () {
                if (mPageNo > 1) { mPageNo--; renderLinesTable(); }
            });
            $modal.find('.vas-pcu-m-next').on('click', function () {
                var mTotalPages = Math.ceil(issueLines.length / mPageSize);
                if (mPageNo < mTotalPages) { mPageNo++; renderLinesTable(); }
            });

            $('body').append($modal);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_186_ProductCategoryUsageWidget/GetCategoryIssueLines',
                type: 'GET',
                data: { categoryId: categoryId, month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    issueLines = data.lines || [];
                    mPageNo = 1;
                    renderLinesTable();
                }
            });
        }

        function createWidget() {
            var title = label("VAS_186_ProductCategoryUsage", "Product Category Usage");
            var sub = label("VAS_186_InternalUseByCategory", "Internal use by category");

            $card = $(
                '<div class="vas-pcu-card vas-widget-bg">' +
                '<div class="vas-pcu-head">' +
                '<div class="vas-pcu-head-left">' +
                '<span class="vas-pcu-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>' +
                '</span>' +
                // Needs its own class: as an unstyled flex child this wrapper defaulted to
                // min-width:auto, so it refused to shrink below the title's nowrap width and
                // pushed the filter controls out over the header instead of letting the
                // title's own ellipsis do the work.
                '<div class="vas-pcu-head-text">' +
                '<div class="vas-pcu-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-pcu-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-pcu-controls">' +
                '<select class="vas-pcu-select vas-pcu-m-sel"></select>' +
                '<select class="vas-pcu-select vas-pcu-y-sel"></select>' +
                '<div class="vas-pcu-divider"></div>' +
                '<div class="vas-pcu-toggle-grp">' +
                '<button type="button" class="vas-pcu-pill vas-pcu-qty-pill active">Qty</button>' +
                '<button type="button" class="vas-pcu-pill vas-pcu-val-pill">Value</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-pcu-body"></div>' +
                '<div class="vas-pcu-foot">' +
                '<div class="vas-pcu-footnote"></div>' +
                '<div class="vas-pcu-pager">' +
                '<button type="button" class="vas-pcu-pager-btn vas-pcu-prev" disabled>&lsaquo;</button>' +
                '<span class="vas-pcu-pager-txt">' + escapeHtml(label("VAS_186_Page", "Page") + ' 1 ' + label("VAS_186_Of", "of") + ' 1') + '</span>' +
                '<button type="button" class="vas-pcu-pager-btn vas-pcu-next" disabled>&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-pcu-body');
            $footnote = $card.find('.vas-pcu-footnote');
            $pagerText = $card.find('.vas-pcu-pager-txt');
            $prevBtn = $card.find('.vas-pcu-prev');
            $nextBtn = $card.find('.vas-pcu-next');

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; renderCategories(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; renderCategories(); }
            });

            $monthSelect = $card.find('.vas-pcu-m-sel');
            $yearSelect = $card.find('.vas-pcu-y-sel');
            $qtyPill = $card.find('.vas-pcu-qty-pill');
            $valPill = $card.find('.vas-pcu-val-pill');

            var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            for (var m = 1; m <= 12; m++) {
                $monthSelect.append('<option value="' + m + '" ' + (m === selectedMonth ? 'selected' : '') + '>' + monthNames[m - 1] + '</option>');
            }

            var currentYear = DateTimeNowYear();
            for (var y = currentYear - 3; y <= currentYear + 1; y++) {
                $yearSelect.append('<option value="' + y + '" ' + (y === selectedYear ? 'selected' : '') + '>' + y + '</option>');
            }

            $monthSelect.on('change', function () {
                selectedMonth = Number($(this).val());
                loadCategoryUsage();
            });

            $yearSelect.on('change', function () {
                selectedYear = Number($(this).val());
                loadCategoryUsage();
            });

            $qtyPill.on('click', function () {
                if (activeMeasure === "qty") { return; }
                activeMeasure = "qty";
                $valPill.removeClass('active');
                $qtyPill.addClass('active');
                renderCategories();
            });

            $valPill.on('click', function () {
                if (activeMeasure === "val") { return; }
                activeMeasure = "val";
                $qtyPill.removeClass('active');
                $valPill.addClass('active');
                renderCategories();
            });

            $body.on('click', '.vas-pcu-row', function () {
                var catId = Number($(this).data('catid') || 0);
                var catName = $(this).data('catname');
                openCategoryDrilldownModal(catId, catName);
            });

            $root.append($card);

            $busy = $('<div class="vas-pcu-busy vas-pcu-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadCategoryUsage();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off("keydown.vas-pcu-modal"); if ($modal) { $modal.remove(); }
            $root.remove();
        };
    };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_186_ProductCategoryUsageWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);

