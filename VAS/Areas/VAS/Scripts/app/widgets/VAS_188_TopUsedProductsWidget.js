/**
 * VAS_188_TopUsedProductsWidget
 * 4x2 Ranked List Widget for Inventory Use dashboard.
 * Displays top 10 products consumed ranked by quantity or value for selected period,
 * rank badges (#1 gold, #2 silver, #3 bronze, #4-10 gray), paginated at 4 items per page,
 * and Product Usage Detail Modal.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Top Used Products                      | VAS_188_TopUsedProducts
 *  2 | Most consumed items by volume         | VAS_188_MostConsumedItemsByVolume
 *  3 | Qty                                    | VAS_188_Qty
 *  4 | Value                                  | VAS_188_Value
 *  5 | Couldn't load                           | VAS_188_CouldntLoad
 *  6 | Close                                  | VAS_188_Close
 *  7 | Month                                  | VAS_188_Month
 *  8 | Consumed Qty                           | VAS_188_ConsumedQty
 *  9 | Consumed Value                         | VAS_188_ConsumedValue
 * 10 | Issue Lines                            | VAS_188_IssueLines
 * 11 | Doc No.                                | VAS_188_DocNo
 * 12 | Date                                   | VAS_188_Date
 * 13 | WH + Loc                               | VAS_188_WarehouseLocator
 * 14 | Loading...                             | VAS_188_Loading
 * 15 | No usage lines found.                  | VAS_188_NoUsageLinesFound
 * 16 | of                                     | VAS_188_Of
 * 17 | Page                                   | VAS_188_Page
 * 18 | lines                                  | VAS_188_Lines
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_188_TopUsedProductsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-tup-root">');
        var $card;
        var $body;
        var $footHelper;
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
        var productsData = [];
        var pageNo = 1;
        var pageSize = 4;
        var totalPages = 1;

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

        function formatQty(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
        var currencyIso = '';
        var currencySymbol = '';

        function isIndianISO(iso) {
            if (!iso) { return false; }
            var indianIsos = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
            return indianIsos.indexOf(String(iso).toUpperCase()) !== -1;
        }

        function getDisplaySymbol() {
            return currencySymbol || currencyIso || '';
        }

        function formatCurrencyCompact(value) {
            var val = Number(value || 0);
            if (isNaN(val)) { val = 0; }
            var sym = getDisplaySymbol();
            var prefix = sym ? (sym + (sym.length > 2 ? ' ' : '')) : '';
            var sign = val < 0 ? '-' : '';
            var absVal = Math.abs(val);

            if (isIndianISO(currencyIso)) {
                if (absVal >= 10000000) {
                    return sign + prefix + (absVal / 10000000).toFixed(1) + 'Cr';
                } else if (absVal >= 100000) {
                    return sign + prefix + (absVal / 100000).toFixed(1) + 'L';
                } else if (absVal >= 1000) {
                    return sign + prefix + (absVal / 1000).toFixed(1) + 'k';
                }
                return sign + prefix + absVal.toLocaleString('en-IN');
            } else {
                if (absVal >= 1000000000) {
                    return sign + prefix + (absVal / 1000000000).toFixed(1) + 'B';
                } else if (absVal >= 1000000) {
                    return sign + prefix + (absVal / 1000000).toFixed(1) + 'M';
                } else if (absVal >= 1000) {
                    return sign + prefix + (absVal / 1000).toFixed(1) + 'k';
                }
                return sign + prefix + absVal.toLocaleString(window.navigator.language || 'en-US');
            }
        }

        function formatCurrencyFull(value) {
            var val = Number(value || 0);
            if (isNaN(val)) { val = 0; }
            var sym = getDisplaySymbol();
            var prefix = sym ? (sym + (sym.length > 2 ? ' ' : '')) : '';
            var sign = val < 0 ? '-' : '';
            var absVal = Math.abs(val);

            if (isIndianISO(currencyIso)) {
                return sign + prefix + absVal.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else {
                return sign + prefix + absVal.toLocaleString(window.navigator.language || 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//      function formatINR(value) {
//          var val = Number(value || 0);
//          if (val >= 100000) {
//              return '₹' + (val / 100000).toFixed(1) + 'L';
//          } else if (val >= 1000) {
//              return '₹' + (val / 1000).toFixed(1) + 'k';
//          }
//          return '₹' + val.toLocaleString(window.navigator.language);
//      }
// ----- END OLD CODE -----

        function formatMonthName(m) {
            var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            return monthNames[Math.max(0, Math.min(11, m - 1))];
        }

        function getRankBadge(rank) {
            if (rank === 1) {
                return '<span class="vas-tup-rank-badge r1">#1</span>';
            } else if (rank === 2) {
                return '<span class="vas-tup-rank-badge r2">#2</span>';
            } else if (rank === 3) {
                return '<span class="vas-tup-rank-badge r3">#3</span>';
            }
            return '<span class="vas-tup-rank-badge r-other">#' + rank + '</span>';
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-tup-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadTopProducts();
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
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        function loadTopProducts() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_188_TopUsedProductsWidget/GetTopProducts',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear, measure: activeMeasure },
                cache: false,
                success: function (res) {
// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
                    var data = parseResponse(res);
                    if (data.currency) {
                        currencyIso = data.currency.iso || currencyIso;
                        currencySymbol = data.currency.symbol || currencySymbol;
                    }
                    productsData = data.products || [];
                    pageNo = 1;
                    renderProducts();
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                  var data = parseResponse(res);
//                  productsData = data.products || [];
//                  pageNo = 1;
//                  renderProducts();
// ----- END OLD CODE -----
                },
                error: function () {
                    productsData = [];
                    renderProducts();
                },
                complete: function () { showBusy(false); }
            });
        }

        function renderProducts() {
            if (!$body) { return; }

            totalPages = Math.max(1, Math.ceil(productsData.length / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }

            if (productsData.length === 0) {
                $body.html('<div class="vas-tup-empty">No products consumed in ' + escapeHtml(formatMonthName(selectedMonth) + ' ' + selectedYear) + '.</div>');
                if ($footHelper) { $footHelper.text('0 of 0'); }
                if ($pagerText) { $pagerText.text('1 of 1'); }
                if ($prevBtn) { $prevBtn.prop('disabled', true); }
                if ($nextBtn) { $nextBtn.prop('disabled', true); }
                return;
            }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(productsData.length, startIndex + pageSize);
            var rowsHtml = '';

            for (var i = startIndex; i < endIndex; i++) {
                var item = productsData[i];
                var rank = i + 1;
                var metaStr = (item.attribute ? (item.attribute + ' · ') : '') + (item.categoryName || 'General');

                rowsHtml +=
                    '<button type="button" class="vas-tup-row" data-pid="' + item.productId + '" data-pname="' + escapeHtml(item.productName) + '" data-cat="' + escapeHtml(item.categoryName || "General") + '" data-uom="' + escapeHtml(item.uomName || "Nos") + '" data-qty="' + item.totalQty + '" data-val="' + item.totalValue + '">' +
                    getRankBadge(rank) +
                    '<div class="vas-tup-row-left">' +
                    '<div class="vas-tup-p-name" title="' + escapeHtml(item.productName) + '">' + escapeHtml(item.productName) + '</div>' +
                    '<div class="vas-tup-p-meta" title="' + escapeHtml(metaStr) + '">' + escapeHtml(metaStr) + '</div>' +
                    '</div>' +
                    '<div class="vas-tup-row-right">' +
                    '<div class="vas-tup-qty-uom">' + formatQty(item.totalQty) + ' ' + escapeHtml(item.uomName || "Nos") + '</div>' +
// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
                    '<div class="vas-tup-val" title="' + escapeHtml(formatCurrencyFull(item.totalValue)) + '">' + escapeHtml(formatCurrencyCompact(item.totalValue)) + '</div>' +
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                  '<div class="vas-tup-val">' + formatINR(item.totalValue) + '</div>' +
// ----- END OLD CODE -----
                    '</div>' +
                    '</button>';
            }

            $body.html(rowsHtml);

            if ($footHelper) {
                $footHelper.text((startIndex + 1) + '–' + endIndex + ' of ' + productsData.length);
            }
            if ($pagerText) {
                $pagerText.text(pageNo + ' of ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }
        }

        function openProductUsageDetailModal(pid, pname, categoryName, uomName, totalQty, totalValue) {
            $(document).off("keydown.vas-tup-modal"); if ($modal) { $modal.remove(); }

            var monthFull = formatMonthName(selectedMonth) + ' ' + selectedYear;

            $modal = $(
                '<div class="vas-tup-modal-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-tup-modal-card">' +
                '<div class="vas-tup-modal-head">' +
                '<div class="vas-tup-modal-title-wrap">' +
                '<h3 class="vas-tup-modal-title" title="' + escapeHtml(pname) + '">' + escapeHtml(pname) + '</h3>' +
                '<span class="vas-tup-cat-badge">' + escapeHtml(categoryName) + '</span>' +
                '</div>' +
                '<button type="button" class="vas-tup-modal-close" aria-label="' + escapeHtml(label("VAS_188_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-tup-modal-body">' +
                '<div class="vas-tup-summary-grid">' +
                '<div class="vas-tup-summary-field"><div class="vas-tup-field-lbl">' + escapeHtml(label("VAS_188_Month", "Month")) + '</div><div class="vas-tup-field-val">' + escapeHtml(monthFull) + '</div></div>' +
                '<div class="vas-tup-summary-field"><div class="vas-tup-field-lbl">' + escapeHtml(label("VAS_188_ConsumedQty", "Consumed Qty")) + '</div><div class="vas-tup-field-val">' + escapeHtml(formatQty(totalQty) + ' ' + uomName) + '</div></div>' +
                '<div class="vas-tup-summary-field"><div class="vas-tup-field-lbl">' + escapeHtml(label("VAS_188_ConsumedValue", "Consumed Value")) + '</div><div class="vas-tup-field-val" title="' + escapeHtml(formatCurrencyFull(totalValue)) + '">' + escapeHtml(formatCurrencyCompact(totalValue)) + '</div></div>' +
                '<div class="vas-tup-summary-field"><div class="vas-tup-field-lbl">' + escapeHtml(label("VAS_188_IssueLines", "Issue Lines")) + '</div><div class="vas-tup-field-val vas-tup-m-lines-cnt">—</div></div>' +
                '</div>' +
                '<table class="vas-tup-lines-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(label("VAS_188_DocNo", "Doc No.")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_188_Date", "Date")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_188_WarehouseLocator", "WH + Loc")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_188_Qty", "Qty")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_188_Value", "Value")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-tup-m-tbody"><tr><td colspan="5" class="vas-tup-m-msgcell">' + escapeHtml(label("VAS_188_Loading", "Loading...")) + '</td></tr></tbody>' +
                '</table>' +
                '<div class="vas-tup-modal-foot">' +
                '<div class="vas-tup-m-helper">' + escapeHtml('0 ' + label("VAS_188_Of", "of") + ' 0 ' + label("VAS_188_Lines", "lines")) + '</div>' +
                '<div class="vas-tup-modal-pager">' +
                '<button type="button" class="vas-tup-modal-pager-btn vas-tup-m-prev" disabled>&lsaquo;</button>' +
                '<span class="vas-tup-m-pager-txt">' + escapeHtml(label("VAS_188_Page", "Page") + ' 1 ' + label("VAS_188_Of", "of") + ' 1') + '</span>' +
                '<button type="button" class="vas-tup-modal-pager-btn vas-tup-m-next" disabled>&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            var usageLines = [];
            var mPageNo = 1;
            var mPageSize = 5;

            function renderLinesTable() {
                var mTotalPages = Math.max(1, Math.ceil(usageLines.length / mPageSize));
                if (mPageNo > mTotalPages) { mPageNo = mTotalPages; }

                var $tbody = $modal.find('.vas-tup-m-tbody');
                var $mHelper = $modal.find('.vas-tup-m-helper');
                var $mPagerTxt = $modal.find('.vas-tup-m-pager-txt');
                var $mPrev = $modal.find('.vas-tup-m-prev');
                var $mNext = $modal.find('.vas-tup-m-next');

                var ofTxt = label("VAS_188_Of", "of");
                var linesTxt = label("VAS_188_Lines", "lines");
                var pageTxt = label("VAS_188_Page", "Page");

                /* The popup is exactly one page tall, so every page must render mPageSize rows.
                   Pages holding fewer records are padded with spacer rows to stop the modal from
                   shrinking (and the footer from jumping) on the last page or an empty result.
                   The helper counts rows on THIS page so the numbers can't be read as a promise
                   of how many are visible. */
                function fillerRows(count) {
                    var html = '';
                    for (var f = 0; f < count; f++) {
                        html += '<tr class="vas-tup-m-filler" aria-hidden="true"><td colspan="5">&nbsp;</td></tr>';
                    }
                    return html;
                }

                if (usageLines.length === 0) {
                    $tbody.html('<tr><td colspan="5" class="vas-tup-m-msgcell">' +
                        escapeHtml(label("VAS_188_NoUsageLinesFound", "No usage lines found.")) + '</td></tr>' +
                        fillerRows(mPageSize - 1));
                    $mHelper.text('0 ' + ofTxt + ' 0 ' + linesTxt);
                    $mPagerTxt.text(pageTxt + ' 1 ' + ofTxt + ' 1');
                    $mPrev.prop('disabled', true);
                    $mNext.prop('disabled', true);
                    return;
                }

                var mStart = (mPageNo - 1) * mPageSize;
                var mEnd = Math.min(usageLines.length, mStart + mPageSize);
                var tbodyHtml = '';

                for (var j = mStart; j < mEnd; j++) {
                    var rec = usageLines[j];
                    tbodyHtml +=
                        '<tr>' +
                        '<td title="' + escapeHtml(rec.documentNo) + '">' + escapeHtml(rec.documentNo) + '</td>' +
                        '<td>' + escapeHtml(rec.movementDate) + '</td>' +
                        '<td title="' + escapeHtml(rec.whLoc) + '">' + escapeHtml(rec.whLoc) + '</td>' +
                        '<td>' + escapeHtml(formatQty(rec.qty)) + '</td>' +
// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
                        '<td title="' + escapeHtml(formatCurrencyFull(rec.value)) + '">' + escapeHtml(formatCurrencyCompact(rec.value)) + '</td>' +
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                      '<td>' + escapeHtml(formatINR(rec.value)) + '</td>' +
// ----- END OLD CODE -----
                        '</tr>';
                }

                $tbody.html(tbodyHtml + fillerRows(mPageSize - (mEnd - mStart)));
                $mHelper.text((mEnd - mStart) + ' ' + ofTxt + ' ' + usageLines.length + ' ' + linesTxt);
                $mPagerTxt.text(pageTxt + ' ' + mPageNo + ' ' + ofTxt + ' ' + mTotalPages);
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
                $(document).off('keydown.vas-tup-modal');
                if ($modal) { $modal.remove(); }
            }

            $modal.find('.vas-tup-modal-close').on('click', function (e) {
                e.stopPropagation();
                closeModal();
            });

            $modal.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas-tup-modal', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            $modal.find('.vas-tup-m-prev').on('click', function () {
                if (mPageNo > 1) { mPageNo--; renderLinesTable(); }
            });
            $modal.find('.vas-tup-m-next').on('click', function () {
                var mTotalPages = Math.ceil(usageLines.length / mPageSize);
                if (mPageNo < mTotalPages) { mPageNo++; renderLinesTable(); }
            });

            $('body').append($modal);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_188_TopUsedProductsWidget/GetProductUsageDetails',
                type: 'GET',
                data: { productId: pid, month: selectedMonth, year: selectedYear },
// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.currency) {
                        currencyIso = data.currency.iso || currencyIso;
                        currencySymbol = data.currency.symbol || currencySymbol;
                    }
                    usageLines = data.lines || [];
                    $modal.find('.vas-tup-m-lines-cnt').text(usageLines.length);
                    mPageNo = 1;
                    renderLinesTable();
                }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//              success: function (res) {
//                  var data = parseResponse(res);
//                  usageLines = data.lines || [];
//                  $modal.find('.vas-tup-m-lines-cnt').text(usageLines.length);
//                  mPageNo = 1;
//                  renderLinesTable();
//              }
// ----- END OLD CODE -----
            });
        }

        function createWidget() {
            var title = label("VAS_188_TopUsedProducts", "Top Used Products");
            var sub = label("VAS_188_MostConsumedItemsByVolume", "Most consumed items by volume");

            $card = $(
                '<div class="vas-tup-card vas-widget-bg">' +
                '<div class="vas-tup-head">' +
                '<div class="vas-tup-head-left">' +
                '<span class="vas-tup-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path><polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline><line x1="12" y1="22.08" x2="12" y2="12"></line></svg>' +
                '</span>' +
                '<div>' +
                '<div class="vas-tup-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-tup-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-tup-controls">' +
                '<select class="vas-tup-select vas-tup-m-sel"></select>' +
                '<select class="vas-tup-select vas-tup-y-sel"></select>' +
                '<div class="vas-tup-divider"></div>' +
                '<div class="vas-tup-toggle-grp">' +
                '<button type="button" class="vas-tup-pill vas-tup-qty-pill active">Qty</button>' +
                '<button type="button" class="vas-tup-pill vas-tup-val-pill">Value</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-tup-body"></div>' +
                '<div class="vas-tup-foot">' +
                '<div class="vas-tup-foot-helper">0 of 0</div>' +
                '<div class="vas-tup-pager">' +
                '<button type="button" class="vas-tup-pager-btn vas-tup-prev">&lsaquo;</button>' +
                '<span class="vas-tup-pager-txt">1 of 1</span>' +
                '<button type="button" class="vas-tup-pager-btn vas-tup-next">&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-tup-body');
            $footHelper = $card.find('.vas-tup-foot-helper');
            $pagerText = $card.find('.vas-tup-pager-txt');
            $prevBtn = $card.find('.vas-tup-prev');
            $nextBtn = $card.find('.vas-tup-next');
            $monthSelect = $card.find('.vas-tup-m-sel');
            $yearSelect = $card.find('.vas-tup-y-sel');
            $qtyPill = $card.find('.vas-tup-qty-pill');
            $valPill = $card.find('.vas-tup-val-pill');

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
                loadTopProducts();
            });

            $yearSelect.on('change', function () {
                selectedYear = Number($(this).val());
                loadTopProducts();
            });

            $qtyPill.on('click', function () {
                if (activeMeasure === "qty") { return; }
                activeMeasure = "qty";
                $valPill.removeClass('active');
                $qtyPill.addClass('active');
                loadTopProducts();
            });

            $valPill.on('click', function () {
                if (activeMeasure === "val") { return; }
                activeMeasure = "val";
                $qtyPill.removeClass('active');
                $valPill.addClass('active');
                loadTopProducts();
            });

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; renderProducts(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; renderProducts(); }
            });

            $body.on('click', '.vas-tup-row', function () {
                var pid = Number($(this).data('pid') || 0);
                var pname = $(this).data('pname');
                var cat = $(this).data('cat');
                var uom = $(this).data('uom');
                var qty = Number($(this).data('qty') || 0);
                var val = Number($(this).data('val') || 0);
                openProductUsageDetailModal(pid, pname, cat, uom, qty, val);
            });

            $root.append($card);

            $busy = $('<div class="vas-tup-busy vas-tup-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadTopProducts();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off("keydown.vas-tup-modal"); if ($modal) { $modal.remove(); }
            $root.remove();
        };
    };

    VAS.VAS_188_TopUsedProductsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_188_TopUsedProductsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_188_TopUsedProductsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_188_TopUsedProductsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_188_TopUsedProductsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_188_TopUsedProductsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);

