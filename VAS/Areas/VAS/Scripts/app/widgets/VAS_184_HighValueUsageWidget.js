/**
 * VAS_184_HighValueUsageWidget
 * 2x2 List Widget for Inventory Use dashboard.
 * Displays top 10 products consumed ranked by current cost price descending.
 * Row click opens Product Issues Modal paginated at 6 entries per page (scroll-free).
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | High-Value Usage                       | VAS_184_HighValueUsage
 *  2 | No usage for                           | VAS_184_NoUsageForPeriod
 *  3 | Couldn't load                           | VAS_184_CouldntLoad
 *  4 | Close                                  | VAS_184_Close
 *  5 | Attribute                              | VAS_184_Attribute
 *  6 | UoM                                    | VAS_184_UoM
 *  7 | Current Cost Price                     | VAS_184_CurrentCostPrice
 *  8 | Total Issued                           | VAS_184_TotalIssued
 *  9 | Doc No.                                | VAS_184_DocNo
 * 10 | Date                                   | VAS_184_Date
 * 11 | WH + Loc                               | VAS_184_WarehouseLocator
 * 12 | Qty                                    | VAS_184_Qty
 * 13 | Value                                  | VAS_184_Value
 * 14 | Loading...                             | VAS_184_Loading
 * 15 | No issues found                        | VAS_184_NoIssuesFound
 * 16 | of                                     | VAS_184_Of
 * 17 | Page                                   | VAS_184_Page
 * 18 | lines                                  | VAS_184_Lines
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

    VAS.VAS_184_HighValueUsageWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-hvu-root">');
        var $card;
        var $body;
        var $footHelper;
        var $pagerText;
        var $prevBtn;
        var $nextBtn;
        var $monthSelect;
        var $yearSelect;
        var $busy;
        var $modal;

        var selectedMonth = DateTimeNowMonth();
        var selectedYear = DateTimeNowYear();
        var productsData = [];
        var pageNo = 1;
        // Starting guess only: recalcPageSize() replaces this with however many rows actually fit.
        var pageSize = 3;
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

        function formatQty(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatINR(value) {
            var val = Number(value || 0);
            if (val >= 100000) {
                return '₹' + (val / 100000).toFixed(1) + 'L';
            } else if (val >= 1000) {
                return '₹' + (val / 1000).toFixed(1) + 'k';
            }
            return '₹' + val.toLocaleString(window.navigator.language);
        }

        function formatMonthName(m) {
            var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            return monthNames[Math.max(0, Math.min(11, m - 1))];
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-hvu-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadProducts();
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
                       number of rows that fit can change in either direction. */
                    if (!isRefitting && productsData.length > 0) {
                        isRefitting = true;
                        var changed = recalcPageSize();
                        isRefitting = false;
                        if (changed) { renderProducts(); }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        function loadProducts() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_184_HighValueUsageWidget/GetHighValueProducts',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    productsData = data.products || [];
                    pageNo = 1;
                    renderProducts();
                },
                error: function () {
                    productsData = [];
                    renderProducts();
                },
                complete: function () { showBusy(false); }
            });
        }

        /* Fit as many rows as the body can hold instead of a fixed count, so the widget fills its
           grid track at any dashboard size / display resolution. Row height is measured from a
           rendered row rather than assumed, because it scales with the card's clamp() font size.
           Returns true when the page size changed and a re-render is needed. */
        function recalcPageSize() {
            if (!$body || !$body[0]) { return false; }

            var bodyH = $body[0].clientHeight;
            if (bodyH <= 0) { return false; }

            var $firstRow = $body.children('.vas-hvu-row').first();
            if (!$firstRow.length) { return false; }

            var rowH = $firstRow[0].getBoundingClientRect().height;
            if (rowH <= 0) { return false; }

            // Allow a hair of tolerance so a row that fits within a fraction of a pixel counts.
            var fits = Math.max(1, Math.floor((bodyH + 0.5) / rowH));
            if (fits === pageSize) { return false; }

            pageSize = fits;
            return true;
        }

        function renderProducts() {
            if (!$body) { return; }

            totalPages = Math.max(1, Math.ceil(productsData.length / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }

            if (productsData.length === 0) {
                $body.html('<div class="vas-hvu-empty">No usage for ' + escapeHtml(formatMonthName(selectedMonth) + ' ' + selectedYear) + '.</div>');
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
                var attrMeta = item.attribute ? (item.attribute + ' · ') : '';
                attrMeta += formatQty(item.issuedQty) + ' ' + (item.uomName || 'Nos');

                rowsHtml +=
                    '<button type="button" class="vas-hvu-row" data-pid="' + item.productId + '" data-pname="' + escapeHtml(item.productName) + '" data-cost="' + item.costPrice + '" data-attr="' + escapeHtml(item.attribute || "-") + '" data-uom="' + escapeHtml(item.uomName || "Nos") + '" data-qty="' + item.issuedQty + '" data-val="' + item.issuedValue + '">' +
                    '<div class="vas-hvu-row-left">' +
                    '<div class="vas-hvu-p-name" title="' + escapeHtml(item.productName) + '">' + escapeHtml(item.productName) + '</div>' +
                    '<div class="vas-hvu-p-meta" title="' + escapeHtml(attrMeta) + '">' + escapeHtml(attrMeta) + '</div>' +
                    '</div>' +
                    '<div class="vas-hvu-p-cost" title="Cost price ' + escapeHtml(formatINR(item.costPrice)) + '">' + escapeHtml(formatINR(item.costPrice)) + '</div>' +
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

            /* Rows are on screen now, so their real height is measurable. If the body can hold a
               different number than we just drew, adopt it and redraw once. recalcPageSize only
               reports a change when the value actually differs, so this cannot loop. */
            if (!isRefitting) {
                isRefitting = true;
                if (recalcPageSize()) { renderProducts(); }
                isRefitting = false;
            }
        }

        function openProductIssuesModal(pid, pname, cost, attr, uom, issuedQty, issuedValue) {
            $(document).off("keydown.vas-hvu-modal"); if ($modal) { $modal.remove(); }

            var monthFull = formatMonthName(selectedMonth) + ' ' + selectedYear;

            $modal = $(
                '<div class="vas-hvu-modal-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-hvu-modal-card">' +
                '<div class="vas-hvu-modal-head">' +
                '<div class="vas-hvu-modal-title-wrap">' +
                '<h3 class="vas-hvu-modal-title" title="' + escapeHtml(pname) + '">' + escapeHtml(pname) + '</h3>' +
                '<span class="vas-hvu-cost-chip">' + escapeHtml(formatINR(cost)) + '</span>' +
                '</div>' +
                '<button type="button" class="vas-hvu-modal-close" aria-label="' + escapeHtml(label("VAS_184_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-hvu-modal-body">' +
                '<div class="vas-hvu-modal-grid">' +
                '<div class="vas-hvu-modal-field"><div class="vas-hvu-m-lbl">' + escapeHtml(label("VAS_184_Attribute", "Attribute")) + '</div><div class="vas-hvu-m-val" title="' + escapeHtml(attr) + '">' + escapeHtml(attr) + '</div></div>' +
                '<div class="vas-hvu-modal-field"><div class="vas-hvu-m-lbl">' + escapeHtml(label("VAS_184_UoM", "UoM")) + '</div><div class="vas-hvu-m-val">' + escapeHtml(uom) + '</div></div>' +
                '<div class="vas-hvu-modal-field"><div class="vas-hvu-m-lbl">' + escapeHtml(label("VAS_184_CurrentCostPrice", "Current Cost Price")) + '</div><div class="vas-hvu-m-val">' + escapeHtml(formatINR(cost)) + '</div></div>' +
                '<div class="vas-hvu-modal-field"><div class="vas-hvu-m-lbl">' + escapeHtml(label("VAS_184_TotalIssued", "Total Issued")) + '</div><div class="vas-hvu-m-val">' + escapeHtml(formatQty(issuedQty) + ' ' + uom + ' · ' + formatINR(issuedValue)) + '</div></div>' +
                '</div>' +
                '<table class="vas-hvu-issues-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(label("VAS_184_DocNo", "Doc No.")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_184_Date", "Date")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_184_WarehouseLocator", "WH + Loc")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_184_Qty", "Qty")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_184_Value", "Value")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-hvu-m-tbody"><tr><td colspan="5" class="vas-hvu-m-msgcell">' + escapeHtml(label("VAS_184_Loading", "Loading...")) + '</td></tr></tbody>' +
                '</table>' +
                '<div class="vas-hvu-foot">' +
                '<div class="vas-hvu-foot-helper vas-hvu-m-helper">' + escapeHtml('0 ' + label("VAS_184_Of", "of") + ' 0 ' + label("VAS_184_Lines", "lines")) + '</div>' +
                '<div class="vas-hvu-pager">' +
                '<button type="button" class="vas-hvu-pager-btn vas-hvu-m-prev" disabled>&lsaquo;</button>' +
                '<span class="vas-hvu-pager-txt vas-hvu-m-pager-txt">' + escapeHtml(label("VAS_184_Page", "Page") + ' 1 ' + label("VAS_184_Of", "of") + ' 1') + '</span>' +
                '<button type="button" class="vas-hvu-pager-btn vas-hvu-m-next" disabled>&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            var issueHistory = [];
            var mPageNo = 1;
            var mPageSize = 6;

            function renderIssueTable() {
                var mTotalPages = Math.max(1, Math.ceil(issueHistory.length / mPageSize));
                if (mPageNo > mTotalPages) { mPageNo = mTotalPages; }

                var $tbody = $modal.find('.vas-hvu-m-tbody');
                var $mHelper = $modal.find('.vas-hvu-m-helper');
                var $mPagerTxt = $modal.find('.vas-hvu-m-pager-txt');
                var $mPrev = $modal.find('.vas-hvu-m-prev');
                var $mNext = $modal.find('.vas-hvu-m-next');

                var ofTxt = label("VAS_184_Of", "of");
                var linesTxt = label("VAS_184_Lines", "lines");
                var pageTxt = label("VAS_184_Page", "Page");

                /* The popup is exactly one page tall, so every page must render mPageSize rows.
                   Pages holding fewer records are padded with spacer rows to stop the modal from
                   shrinking (and the footer from jumping) on the last page or an empty result.
                   The helper counts rows on THIS page so the numbers can't be read as a promise
                   of how many are visible. */
                function fillerRows(count) {
                    var html = '';
                    for (var f = 0; f < count; f++) {
                        html += '<tr class="vas-hvu-m-filler" aria-hidden="true"><td colspan="5">&nbsp;</td></tr>';
                    }
                    return html;
                }

                if (issueHistory.length === 0) {
                    $tbody.html('<tr><td colspan="5" class="vas-hvu-m-msgcell">' +
                        escapeHtml(label("VAS_184_NoIssuesFound", "No issues found")) + '</td></tr>' +
                        fillerRows(mPageSize - 1));
                    $mHelper.text('0 ' + ofTxt + ' 0 ' + linesTxt);
                    $mPagerTxt.text(pageTxt + ' 1 ' + ofTxt + ' 1');
                    $mPrev.prop('disabled', true);
                    $mNext.prop('disabled', true);
                    return;
                }

                var mStart = (mPageNo - 1) * mPageSize;
                var mEnd = Math.min(issueHistory.length, mStart + mPageSize);
                var tbodyHtml = '';

                for (var j = mStart; j < mEnd; j++) {
                    var rec = issueHistory[j];
                    tbodyHtml +=
                        '<tr>' +
                        '<td title="' + escapeHtml(rec.documentNo) + '">' + escapeHtml(rec.documentNo) + '</td>' +
                        '<td>' + escapeHtml(rec.movementDate) + '</td>' +
                        '<td title="' + escapeHtml(rec.warehouseLoc) + '">' + escapeHtml(rec.warehouseLoc) + '</td>' +
                        '<td>' + escapeHtml(formatQty(rec.qty)) + '</td>' +
                        '<td>' + escapeHtml(formatINR(rec.value)) + '</td>' +
                        '</tr>';
                }

                $tbody.html(tbodyHtml + fillerRows(mPageSize - (mEnd - mStart)));
                $mHelper.text((mEnd - mStart) + ' ' + ofTxt + ' ' + issueHistory.length + ' ' + linesTxt);
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
                $(document).off('keydown.vas-hvu-modal');
                if ($modal) { $modal.remove(); }
            }

            $modal.find('.vas-hvu-modal-close').on('click', function (e) {
                e.stopPropagation();
                closeModal();
            });

            $modal.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas-hvu-modal', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            $modal.find('.vas-hvu-m-prev').on('click', function () {
                if (mPageNo > 1) { mPageNo--; renderIssueTable(); }
            });
            $modal.find('.vas-hvu-m-next').on('click', function () {
                var mTotalPages = Math.ceil(issueHistory.length / mPageSize);
                if (mPageNo < mTotalPages) { mPageNo++; renderIssueTable(); }
            });

            $('body').append($modal);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_184_HighValueUsageWidget/GetProductIssueHistory',
                type: 'GET',
                data: { productId: pid, month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    issueHistory = data.issues || [];
                    mPageNo = 1;
                    renderIssueTable();
                }
            });
        }

        function createWidget() {
            var title = label("VAS_184_HighValueUsage", "High-Value Usage");

            $card = $(
                '<div class="vas-hvu-card vas-widget-bg">' +
                '<div class="vas-hvu-head">' +
                '<span class="vas-hvu-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>' +
                '</span>' +
                '<div class="vas-hvu-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                '</div>' +
                '<div class="vas-hvu-filters">' +
                '<select class="vas-hvu-select vas-hvu-m-sel"></select>' +
                '<select class="vas-hvu-select vas-hvu-y-sel"></select>' +
                '</div>' +
                '<div class="vas-hvu-body"></div>' +
                '<div class="vas-hvu-foot">' +
                '<div class="vas-hvu-foot-helper">0 of 0</div>' +
                '<div class="vas-hvu-pager">' +
                '<button type="button" class="vas-hvu-pager-btn vas-hvu-prev">&lsaquo;</button>' +
                '<span class="vas-hvu-pager-txt">1 of 1</span>' +
                '<button type="button" class="vas-hvu-pager-btn vas-hvu-next">&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-hvu-body');
            $footHelper = $card.find('.vas-hvu-foot-helper');
            $pagerText = $card.find('.vas-hvu-pager-txt');
            $prevBtn = $card.find('.vas-hvu-prev');
            $nextBtn = $card.find('.vas-hvu-next');
            $monthSelect = $card.find('.vas-hvu-m-sel');
            $yearSelect = $card.find('.vas-hvu-y-sel');

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
                loadProducts();
            });

            $yearSelect.on('change', function () {
                selectedYear = Number($(this).val());
                loadProducts();
            });

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; renderProducts(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; renderProducts(); }
            });

            $body.on('click', '.vas-hvu-row', function () {
                var pid = Number($(this).data('pid') || 0);
                var pname = $(this).data('pname');
                var cost = Number($(this).data('cost') || 0);
                var attr = $(this).data('attr');
                var uom = $(this).data('uom');
                var qty = Number($(this).data('qty') || 0);
                var val = Number($(this).data('val') || 0);
                openProductIssuesModal(pid, pname, cost, attr, uom, qty, val);
            });

            $root.append($card);

            $busy = $('<div class="vas-hvu-busy vas-hvu-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadProducts();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off("keydown.vas-hvu-modal"); if ($modal) { $modal.remove(); }
            $root.remove();
        };
    };

    VAS.VAS_184_HighValueUsageWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_184_HighValueUsageWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_184_HighValueUsageWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_184_HighValueUsageWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_184_HighValueUsageWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_184_HighValueUsageWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
