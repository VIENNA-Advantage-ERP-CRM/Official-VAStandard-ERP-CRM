
/**
 * Expected Receipts Widget
 * Purpose - Show upcoming AR receipts expected from unpaid invoice pay schedules.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                 | Message Key
 * ----+------------------------------+--------------------------------
 *  1  | Expected receipts            | VIS_ExpectedReceipts
 *  2  | Next 7 days                  | VIS_Next7Days
 *  3  | This Month                   | VIS_ThisMonth
 *  4  | Loading…                     | VIS_Loading
 *  5  | No data                      | VIS_NoData
 *  6  | Expected                     | VIS_Expected
 *  7  | Previous                     | VIS_Previous
 *  8  | Next                         | VIS_Next
 * ─────────────────────────────────────────────────────────────────────
 */
/**
 * Expected Receipts Widget
 * Purpose - Show upcoming AR receipts expected from unpaid invoice pay schedules.
 */


; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ExpectedReceiptsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-er-root">');
        var $listBody;
        var $subtitle;
        var $prevBtn;
        var $nextBtn;
        var $pageText;

        var $filterBtn;
        var $datePopup;
        var $fromDateInput;
        var $toDateInput;
        var $clearBtn;
        var $applyBtn;
        var $closePopupBtn;

        var $next7Btn;
        var $nextMonthBtn;

        var selectedFilter = "Next7Days";
        var customFromDate = "";
        var customToDate = "";

        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function () {
            createWidget();
            setDefaultNext7Days();
            loadData();
        };

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'ExpectedReceipts/GetExpectedReceipts',
                type: 'GET',
                data: {
                    filterType: selectedFilter,
                    fromDate: customFromDate,
                    toDate: customToDate,
                    pageNo: pageNo,
                    pageSize: pageSize
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
                }
            });
        }

        function setLoading() {
            if ($listBody) {
                $listBody.html(
                    '<div class="vas-er-nodata">' +
                    lbl("VIS_Loading", "Loading…") +
                    '</div>'
                );
            }

            updatePager();
        }

        function setNoData() {
            totalPages = 0;

            if ($listBody) {
                $listBody.html(
                    '<div class="vas-er-nodata">' +
                    lbl("VIS_NoData", "No data") +
                    '</div>'
                );
            }

            updatePager();
        }

        function renderData(data) {
            var rows = data && data.rows ? data.rows : [];

            pageNo = Number(data && data.pageNo || pageNo);
            totalPages = Number(data && data.totalPages || 0);

            if (!$listBody) {
                return;
            }

            $listBody.empty();

            if (!rows || rows.length === 0) {
                setNoData();
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                $listBody.append(buildRow(rows[i], i));
            }

            updatePager();
        }

        function buildRow(row, index) {
            var documentNo = row.documentNo || "";
            var customerName = row.customerName || "";
            var dueDateText = formatDueDate(row.dueDate);
            var methodName = normalizeMethodName(row.paymentMethodName);
            var amount = formatAmount(row.expectedAmount);
            var barClass = getBarClass(methodName, index);

            return $(
                '<div class="vas-er-row">' +
                '<span class="vas-er-bar ' + barClass + '"></span>' +
                '<div class="vas-er-info">' +
                '<div class="vas-er-row-title">' +
                escapeHtml(documentNo) + ' · ' + escapeHtml(customerName) +
                '</div>' +
                '<div class="vas-er-meta">' +
                escapeHtml(dueDateText) + ' · ' + escapeHtml(methodName) +
                '</div>' +
                '</div>' +
                '<span class="vas-er-amount">' + escapeHtml(amount) + '</span>' +
                '</div>'
            );
        }

        function normalizeMethodName(name) {
            if (!name) {
                return lbl("VIS_Expected", "Expected");
            }

            var n = name.toString().trim();

            if (n === "Bank Transfer") {
                return "NEFT expected";
            }

            if (n === "Direct Debit") {
                return "UPI auto-debit";
            }

            if (n === "Cheque" || n === "Check") {
                return "Cheque expected";
            }

            if (n === "On Credit") {
                return "Credit expected";
            }

            return n;
        }

        function getBarClass(methodName, index) {
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
                month: "short"
            });
        }

        function formatAmount(value) {
            var absVal = Number(value || 0);
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return absVal.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function updatePager() {
            if ($subtitle) {
                $subtitle.text(getSubtitle());
            }

            if ($filterBtn) {
                $filterBtn.text(getFilterButtonText());
            }

            setActiveQuickFilter();

            if ($pageText) {
                if (totalPages > 1) {
                    $pageText.text(pageNo + " / " + totalPages);
                }
                else {
                    $pageText.text("");
                }
            }

            if ($prevBtn) {
                $prevBtn.prop("disabled", pageNo <= 1 || totalPages <= 1);
            }

            if ($nextBtn) {
                $nextBtn.prop("disabled", totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function getSubtitle() {
            if (selectedFilter === "Custom") {
                if (customFromDate && customToDate) {
                    return formatShortDate(customFromDate) + " - " + formatShortDate(customToDate);
                }

                return "Custom date";
            }

            if (selectedFilter === "Next7Days") {
                return "Next 7 days";
            }

            if (selectedFilter === "NextMonth") {
                return "Next Month";
            }

            return "Next 7 days";
        }

        function getFilterButtonText() {
            if (selectedFilter === "Next7Days") {
                return "NEXT 7 DAYS ▾";
            }

            if (selectedFilter === "NextMonth") {
                return "NEXT MONTH ▾";
            }

            if (selectedFilter === "Custom") {
                return "CUSTOM DATE ▾";
            }

            return "NEXT 7 DAYS ▾";
        }

        function setActiveQuickFilter() {
            if (!$next7Btn || !$nextMonthBtn) {
                return;
            }

            $next7Btn.removeClass("active");
            $nextMonthBtn.removeClass("active");

            if (selectedFilter === "Next7Days") {
                $next7Btn.addClass("active");
            }
            else if (selectedFilter === "NextMonth") {
                $nextMonthBtn.addClass("active");
            }
        }

        function formatShortDate(value) {
            if (!value) {
                return "";
            }

            var d = new Date(value);

            if (isNaN(d.getTime())) {
                return value;
            }

            return d.toLocaleDateString(window.navigator.language, {
                day: "2-digit",
                month: "short"
            });
        }

        function setDefaultNext7Days() {
            var today = new Date();
            var to = new Date();

            to.setDate(today.getDate() + 6);

            customFromDate = toInputDate(today);
            customToDate = toInputDate(to);

            if ($fromDateInput) {
                $fromDateInput.val(customFromDate);
            }

            if ($toDateInput) {
                $toDateInput.val(customToDate);
            }
        }

        function setDefaultNextMonth() {
            var today = new Date();
            var from = new Date(today.getFullYear(), today.getMonth() + 1, 1);
            var to = new Date(today.getFullYear(), today.getMonth() + 2, 0);

            customFromDate = toInputDate(from);
            customToDate = toInputDate(to);

            if ($fromDateInput) {
                $fromDateInput.val(customFromDate);
            }

            if ($toDateInput) {
                $toDateInput.val(customToDate);
            }
        }

        function toInputDate(date) {
            var y = date.getFullYear();
            var m = String(date.getMonth() + 1).padStart(2, "0");
            var d = String(date.getDate()).padStart(2, "0");

            return y + "-" + m + "-" + d;
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
            var $card = $(
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
                '<span class="vas-er-title">' +
                lbl("VIS_ExpectedReceipts", "Expected receipts") +
                '</span>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-pager">' +
                '<button type="button" class="vas-er-page-btn vas-er-prev" aria-label="Previous">‹</button>' +
                '<span class="vas-er-page-text"></span>' +
                '<button type="button" class="vas-er-page-btn vas-er-next" aria-label="Next">›</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-filter-row">' +
                '<button type="button" class="vas-er-filter-btn">NEXT 7 DAYS ▾</button>' +
                '<span class="vas-er-subtitle">Next 7 days</span>' +
                '</div>' +

                '<div class="vas-er-date-popup" style="display:none;">' +
                '<button type="button" class="vas-er-popup-close">×</button>' +

                '<div class="vas-er-quick-filters">' +
                '<button type="button" class="vas-er-quick-filter vas-er-next7-btn active">Next 7 Days</button>' +
                '<button type="button" class="vas-er-quick-filter vas-er-next-month-btn">Next Month</button>' +
                '</div>' +

                '<div class="vas-er-date-grid">' +

                '<div class="vas-er-date-field">' +
                '<label class="vas-er-date-label">From Date</label>' +
                '<input type="date" class="vas-er-date-input vas-er-from-date" />' +
                '</div>' +

                '<div class="vas-er-date-field">' +
                '<label class="vas-er-date-label">To Date</label>' +
                '<input type="date" class="vas-er-date-input vas-er-to-date" />' +
                '</div>' +

                '</div>' +

                '<div class="vas-er-popup-actions">' +
                '<button type="button" class="vas-er-clear-btn">Clear</button>' +
                '<button type="button" class="vas-er-apply-btn">Apply</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-body">' +
                '<div class="vas-er-nodata">' +
                lbl("VIS_Loading", "Loading…") +
                '</div>' +
                '</div>' +

                '</div>'
            );

            $listBody = $card.find('.vas-er-body');
            $subtitle = $card.find('.vas-er-subtitle');
            $prevBtn = $card.find('.vas-er-prev');
            $nextBtn = $card.find('.vas-er-next');
            $pageText = $card.find('.vas-er-page-text');

            $filterBtn = $card.find('.vas-er-filter-btn');
            $datePopup = $card.find('.vas-er-date-popup');
            $fromDateInput = $card.find('.vas-er-from-date');
            $toDateInput = $card.find('.vas-er-to-date');
            $clearBtn = $card.find('.vas-er-clear-btn');
            $applyBtn = $card.find('.vas-er-apply-btn');
            $closePopupBtn = $card.find('.vas-er-popup-close');

            $next7Btn = $card.find('.vas-er-next7-btn');
            $nextMonthBtn = $card.find('.vas-er-next-month-btn');

            $filterBtn.on('click', function () {
                $datePopup.toggle();
            });

            $closePopupBtn.on('click', function () {
                $datePopup.hide();
            });

            $next7Btn.on('click', function () {
                selectedFilter = "Next7Days";
                setDefaultNext7Days();
                pageNo = 1;
                $datePopup.hide();
                loadData();
            });

            $nextMonthBtn.on('click', function () {
                selectedFilter = "NextMonth";
                setDefaultNextMonth();
                pageNo = 1;
                $datePopup.hide();
                loadData();
            });

            $clearBtn.on('click', function () {
                selectedFilter = "Next7Days";
                setDefaultNext7Days();
                pageNo = 1;
                $datePopup.hide();
                loadData();
            });

            $applyBtn.on('click', function () {
                var fromValue = $fromDateInput.val();
                var toValue = $toDateInput.val();

                if (!fromValue || !toValue) {
                    return;
                }

                customFromDate = fromValue;
                customToDate = toValue;
                selectedFilter = "Custom";
                pageNo = 1;

                $datePopup.hide();
                loadData();
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
        }

        this.refreshWidget = function () {
            pageNo = 1;

            if (selectedFilter === "Next7Days") {
                setDefaultNext7Days();
            }
            else if (selectedFilter === "NextMonth") {
                setDefaultNextMonth();
            }

            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.ExpectedReceiptsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.ExpectedReceiptsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.ExpectedReceiptsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.ExpectedReceiptsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);