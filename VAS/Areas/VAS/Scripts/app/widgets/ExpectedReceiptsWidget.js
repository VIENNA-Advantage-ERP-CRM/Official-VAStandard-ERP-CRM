/**
 * Expected Receipts Widget
 * Purpose - Show AR invoices expected to be received, with pagination and filters.
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ExpectedReceiptsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-er-root">');
        var $listBody;
        var $pageText;
        var $filterText;
        var $customPanel;
        var $fromDate;
        var $toDate;
        var $searchInput;

        var selectedFilter = "Next7Days";
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            setLoading();

            var requestData = {
                filterType: selectedFilter,
                pageNo: pageNo,
                pageSize: pageSize,
                searchText: $searchInput ? $searchInput.val() : ""
            };

            if (selectedFilter === "Custom") {
                requestData.fromDate = $fromDate ? $fromDate.val() : "";
                requestData.toDate = $toDate ? $toDate.val() : "";
            }

            $.ajax({
                url: VIS.Application.contextUrl + 'ExpectedReceipts/GetExpectedReceipts',
                type: 'GET',
                data: requestData,
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

                    renderRows(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($listBody) {
                $listBody.html('<div class="vas-er-nodata">' + lbl("VIS_Loading", "Loading…") + '</div>');
            }
        }

        function setNoData() {
            if ($listBody) {
                $listBody.html('<div class="vas-er-nodata">' + lbl("VIS_NoData", "No data") + '</div>');
            }

            totalPages = 0;
            updatePager();
        }

        function getFilterLabel() {
            if (selectedFilter === "ThisMonth") {
                return lbl("VIS_ThisMonth", "This Month");
            }

            if (selectedFilter === "Custom") {
                return lbl("VIS_CustomDate", "Custom Date");
            }

            return lbl("VIS_Next7Days", "Next 7 Days");
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

            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }

        function formatDateLabel(value) {
            if (!value) {
                return "";
            }

            var d = new Date(value + "T00:00:00");

            if (isNaN(d.getTime())) {
                return value;
            }

            var days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
            var months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

            return days[d.getDay()] + " " + d.getDate() + " " + months[d.getMonth()];
        }

        function renderRows(data) {
            if (!$listBody) {
                return;
            }

            $listBody.empty();

            var rows = data && data.rows ? data.rows : [];
            totalPages = data && data.totalPages ? data.totalPages : 0;
            pageNo = data && data.pageNo ? data.pageNo : pageNo;

            if (!rows || rows.length === 0) {
                setNoData();
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var lineColor = i % 2 === 0 ? "#4FA08E" : "#4F7FEA";
                var border = i === rows.length - 1 ? "" : "border-bottom:1px solid #E6EDF2;";

                var $row = $(
                    '<div class="vas-er-row" style="' + border + '">' +
                    '<div class="vas-er-line" style="background:' + lineColor + ';"></div>' +
                    '<div class="vas-er-info">' +
                    '<div class="vas-er-invoice">' +
                    (r.documentNo || "—") + ' · ' + (r.customerName || "—") +
                    '</div>' +
                    '<div class="vas-er-meta">' +
                    formatDateLabel(r.dueDate) + ' · ' + (r.paymentMethodName || "Expected") +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-er-amount">' +
                    formatCompactAmount(r.expectedAmount) +
                    '</div>' +
                    '</div>'
                );

                $listBody.append($row);
            }

            updatePager();
        }

        function updatePager() {
            if ($pageText) {
                if (totalPages <= 0) {
                    $pageText.text("0 / 0");
                }
                else {
                    $pageText.text(pageNo + " / " + totalPages);
                }
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-er-card">' +
                '<div class="vas-er-topbar">' +
                '<div class="vas-er-header">' +
                '<div class="vas-er-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#4F8EEA" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>' +
                '<line x1="16" y1="2" x2="16" y2="6"/>' +
                '<line x1="8" y1="2" x2="8" y2="6"/>' +
                '<line x1="3" y1="10" x2="21" y2="10"/>' +
                '</svg>' +
                '</div>' +
                '<div>' +
                '<div class="vas-er-title">' +
                lbl("VIS_ExpectedReceipts", "Expected receipts") +
                '</div>' +
                '<div class="vas-er-subtitle">' +
                lbl("VIS_Next7Days", "NEXT 7 DAYS") +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-filter-wrap">' +
                '<button type="button" class="vas-er-filter-btn">' +
                '<span class="vas-er-filter-text">' + lbl("VIS_Next7Days", "Next 7 Days") + '</span>' +
                '<span class="vas-er-filter-arrow">▾</span>' +
                '</button>' +
                '<div class="vas-er-filter-menu">' +
                '<div class="vas-er-filter-item" data-filter="Next7Days">' + lbl("VIS_Next7Days", "Next 7 Days") + '</div>' +
                '<div class="vas-er-filter-item" data-filter="ThisMonth">' + lbl("VIS_ThisMonth", "This Month") + '</div>' +
                '<div class="vas-er-filter-item" data-filter="Custom">' + lbl("VIS_CustomDate", "Custom Date") + '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-tools">' +
                '<input type="text" class="vas-er-search" placeholder="' + lbl("VIS_Search", "Search invoice/customer") + '" />' +
                '<button type="button" class="vas-er-search-btn">' + lbl("VIS_Search", "Search") + '</button>' +
                '</div>' +

                '<div class="vas-er-custom-panel">' +
                '<div class="vas-er-custom-row">' +
                '<label>' + lbl("VIS_FromDate", "From Date") + '</label>' +
                '<input type="date" class="vas-er-from-date" />' +
                '</div>' +
                '<div class="vas-er-custom-row">' +
                '<label>' + lbl("VIS_ToDate", "To Date") + '</label>' +
                '<input type="date" class="vas-er-to-date" />' +
                '</div>' +
                '<div class="vas-er-custom-actions">' +
                '<button type="button" class="vas-er-clear-btn">' + lbl("VIS_Clear", "Clear") + '</button>' +
                '<button type="button" class="vas-er-apply-btn">' + lbl("VIS_Apply", "Apply") + '</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-er-list">' +
                '<div class="vas-er-nodata">' + lbl("VIS_Loading", "Loading…") + '</div>' +
                '</div>' +

                '<div class="vas-er-pager">' +
                '<button type="button" class="vas-er-prev">‹</button>' +
                '<span class="vas-er-page-text">0 / 0</span>' +
                '<button type="button" class="vas-er-next">›</button>' +
                '</div>' +
                '</div>'
            );

            $listBody = $card.find('.vas-er-list');
            $pageText = $card.find('.vas-er-page-text');
            $filterText = $card.find('.vas-er-filter-text');
            $customPanel = $card.find('.vas-er-custom-panel');
            $fromDate = $card.find('.vas-er-from-date');
            $toDate = $card.find('.vas-er-to-date');
            $searchInput = $card.find('.vas-er-search');

            $card.find('.vas-er-filter-btn').on('click', function () {
                $card.find('.vas-er-filter-menu').toggleClass('vas-er-filter-menu-show');
            });

            $card.find('.vas-er-filter-item').on('click', function () {
                selectedFilter = $(this).data('filter');
                pageNo = 1;

                $card.find('.vas-er-filter-menu').removeClass('vas-er-filter-menu-show');
                $filterText.text(getFilterLabel());

                if (selectedFilter === "Custom") {
                    $customPanel.addClass('vas-er-custom-panel-show');
                    return;
                }

                $customPanel.removeClass('vas-er-custom-panel-show');
                loadData();
            });

            $card.find('.vas-er-clear-btn').on('click', function () {
                $fromDate.val("");
                $toDate.val("");
            });

            $card.find('.vas-er-apply-btn').on('click', function () {
                if (!$fromDate.val() || !$toDate.val()) {
                    return;
                }

                selectedFilter = "Custom";
                pageNo = 1;
                $filterText.text(getFilterLabel());
                $customPanel.removeClass('vas-er-custom-panel-show');
                loadData();
            });

            $card.find('.vas-er-search-btn').on('click', function () {
                pageNo = 1;
                loadData();
            });

            $searchInput.on('keypress', function (e) {
                if (e.which === 13) {
                    pageNo = 1;
                    loadData();
                }
            });

            $card.find('.vas-er-prev').on('click', function () {
                if (pageNo > 1) {
                    pageNo--;
                    loadData();
                }
            });

            $card.find('.vas-er-next').on('click', function () {
                if (pageNo < totalPages) {
                    pageNo++;
                    loadData();
                }
            });

            $root.append($card);
        }

        this.refreshWidget = function () {
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