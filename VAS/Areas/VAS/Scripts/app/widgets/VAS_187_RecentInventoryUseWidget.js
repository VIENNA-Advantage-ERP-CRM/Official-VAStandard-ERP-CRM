/**
 * VAS_187_RecentInventoryUseWidget
 * 4x2 Recent Issues List Widget for Inventory Use dashboard.
 * Displays real-time operational activity log of recent material issue transactions with status filtering,
 * paginated at 4 items per page, and direct tab opening via widgetFirevalueChanged.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Recent Inventory Use                   | VAS_RecentInventoryUse
 *  2 | Latest material issue transactions     | VAS_LatestMaterialIssueTxns
 *  3 | All Statuses                           | VAS_AllStatuses
 *  4 | Completed                              | VAS_Completed
 *  5 | Drafted                                | VAS_Drafted
 *  6 | Couldn't load                           | VAS_CouldntLoad
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

    VAS.VAS_187_RecentInventoryUseWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-riu-root">');
        var $card;
        var $body;
        var $footHelper;
        var $pagerText;
        var $prevBtn;
        var $nextBtn;
        var $statusSelect;
        var $busy;

        var selectedStatus = "ALL";
        var pageNo = 1;
        var pageSize = 4;
        var totalRecords = 0;
        var totalPages = 1;
        var recordsData = [];

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

        function getStatusBadge(status) {
            if (status === 'CO' || status === 'CL') {
                return '<span class="vas-riu-badge co">' + escapeHtml(label("VAS_Completed", "Completed")) + '</span>';
            } else if (status === 'DR') {
                return '<span class="vas-riu-badge dr">' + escapeHtml(label("VAS_Drafted", "Drafted")) + '</span>';
            }
            return '<span class="vas-riu-badge ip">' + escapeHtml(status) + '</span>';
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-riu-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadRecentIssues();
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

        function loadRecentIssues() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_187_RecentInventoryUseWidget/GetRecentIssues',
                type: 'GET',
                data: { status: selectedStatus, pageNo: pageNo, pageSize: pageSize },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    recordsData = data.records || [];
                    totalRecords = data.totalRecords || 0;
                    renderRecords();
                },
                error: function () {
                    recordsData = [];
                    totalRecords = 0;
                    renderRecords();
                },
                complete: function () { showBusy(false); }
            });
        }

        function renderRecords() {
            if (!$body) { return; }

            totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }

            if (recordsData.length === 0) {
                $body.html('<div class="vas-riu-empty">No recent inventory use transactions.</div>');
                if ($footHelper) { $footHelper.text('0 of 0'); }
                if ($pagerText) { $pagerText.text('1 of 1'); }
                if ($prevBtn) { $prevBtn.prop('disabled', true); }
                if ($nextBtn) { $nextBtn.prop('disabled', true); }
                return;
            }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(totalRecords, startIndex + recordsData.length);
            var rowsHtml = '';

            for (var i = 0; i < recordsData.length; i++) {
                var item = recordsData[i];
                var metaStr = item.orgName + ' · ' + (item.warehouseName || 'Warehouse') + ' · ' + item.movementDate;

                rowsHtml +=
                    '<button type="button" class="vas-riu-row" data-invid="' + item.inventoryId + '">' +
                    '<div class="vas-riu-row-left">' +
                    '<div class="vas-riu-doc-head">' +
                    '<span class="vas-riu-doc-no">' + escapeHtml(item.documentNo) + '</span>' +
                    getStatusBadge(item.docStatus) +
                    '</div>' +
                    '<div class="vas-riu-doc-meta" title="' + escapeHtml(metaStr) + '">' + escapeHtml(metaStr) + '</div>' +
                    '</div>' +
                    '<div class="vas-riu-row-right">' +
                    '<div class="vas-riu-lines-qty">' + item.lineCount + ' lines · ' + formatQty(item.totalQty) + '</div>' +
                    '<div class="vas-riu-val">' + formatINR(item.totalValue) + '</div>' +
                    '</div>' +
                    '</button>';
            }

            $body.html(rowsHtml);

            if ($footHelper) {
                $footHelper.text((startIndex + 1) + '–' + endIndex + ' of ' + totalRecords);
            }
            if ($pagerText) {
                $pagerText.text(pageNo + ' of ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }
        }

        function openInventoryRecord(inventoryId) {
            var windowParam = {
                "Record_ID": inventoryId,
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = label("VAS_RecentInventoryUse", "Recent Inventory Use");
            var sub = label("VAS_LatestMaterialIssueTxns", "Latest material issue transactions");

            $card = $(
                '<div class="vas-riu-card vas-widget-bg">' +
                '<div class="vas-riu-head">' +
                '<div class="vas-riu-head-left">' +
                '<span class="vas-riu-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>' +
                '</span>' +
                '<div>' +
                '<div class="vas-riu-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-riu-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<select class="vas-riu-select vas-riu-status-sel">' +
                '<option value="ALL">' + escapeHtml(label("VAS_AllStatuses", "All Statuses")) + '</option>' +
                '<option value="CO">' + escapeHtml(label("VAS_Completed", "Completed")) + '</option>' +
                '<option value="DR">' + escapeHtml(label("VAS_Drafted", "Drafted")) + '</option>' +
                '</select>' +
                '</div>' +
                '<div class="vas-riu-body"></div>' +
                '<div class="vas-riu-foot">' +
                '<div class="vas-riu-foot-helper">0 of 0</div>' +
                '<div class="vas-riu-pager">' +
                '<button type="button" class="vas-riu-pager-btn vas-riu-prev">&lsaquo;</button>' +
                '<span class="vas-riu-pager-txt">1 of 1</span>' +
                '<button type="button" class="vas-riu-pager-btn vas-riu-next">&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-riu-body');
            $footHelper = $card.find('.vas-riu-foot-helper');
            $pagerText = $card.find('.vas-riu-pager-txt');
            $prevBtn = $card.find('.vas-riu-prev');
            $nextBtn = $card.find('.vas-riu-next');
            $statusSelect = $card.find('.vas-riu-status-sel');

            $statusSelect.on('change', function () {
                selectedStatus = $(this).val();
                pageNo = 1;
                loadRecentIssues();
            });

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; loadRecentIssues(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; loadRecentIssues(); }
            });

            $body.on('click', '.vas-riu-row', function () {
                var invId = Number($(this).data('invid') || 0);
                openInventoryRecord(invId);
            });

            $root.append($card);

            $busy = $('<div class="vas-riu-busy vas-riu-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadRecentIssues();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
