/**
 * VAS_212_ExpectedLandedCostWidget
 * 3x2 Read-only Widget for Purchase Order Dashboard.
 * Displays aggregated expected landed cost amounts by dynamic cost element (M_CostElement)
 * for open purchase orders in the selected period (month / year).
 *
 * Summary Message Table
 *  # | Fallback Text                                    | Message Key
 * ---+--------------------------------------------------+-----------------------------------
 *  1 | Expected Landed Cost on PO                       | VAS_ExpectedLandedCostOnPO
 *  2 | Across {0} open POs                              | VAS_AcrossOpenPOs
 *  3 | Across {0} open PO                               | VAS_AcrossOpenPO
 *  4 | Showing {0}–{1} of {2} cost elements             | VAS_ShowingCostElements
 *  5 | No expected landed cost for the selected month.  | VAS_NoExpectedLandedCost
 *  6 | Couldn't load expected landed cost.              | VAS_CouldntLoadExpectedCost
 *  7 | Month                                            | VAS_Month
 *  8 | Year                                             | VAS_Year
 *  9 | Previous                                         | VAS_Previous
 * 10 | Next                                             | VAS_Next
 * 11 | of                                               | VAS_Of
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

    var MONTH_NAMES = [
        'January', 'February', 'March', 'April', 'May', 'June',
        'July', 'August', 'September', 'October', 'November', 'December'
    ];

    var PASTEL_COLORS = [
        '#A9D2FF', // Blue
        '#A3E0D4', // Teal
        '#FFDCA1', // Amber
        '#CFC9F5', // Lilac
        '#FFC7C7', // Rose
        '#C8F0DF', // Mint
        '#D7E3EE'  // Slate
    ];

    VAS.VAS_212_ExpectedLandedCostWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-elc-wrapper">');
        var $root = $('<div class="vas-elc-widget">');
        var $head;
        var $topSummary;
        var $totalEl;
        var $metaEl;
        var $listContainer;
        var $emptyEl;
        var $foot;
        var $helperEl;
        var $pagerEl;
        var $prevBtn;
        var $nextBtn;
        var $pageTxt;
        var $monthSelect;
        var $yearSelect;
        var $busy;

        var now = new Date();
        var selectedMonth = now.getMonth() + 1;
        var selectedYear = now.getFullYear();

        var costElements = [];
        var totalCost = 0;
        var openPOCount = 0;
        var curSymbol = '';
        var curIso = '';
        var stdPrecision = 2;

        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 1;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated !== key && translated !== '[' + key + ']' && translated.charAt(0) !== '[') ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function formatCurrency(amount, symbol, precision) {
            var val = Number(amount || 0);
            var prefix = symbol ? (symbol + ' ') : '';
            var prec = (precision !== undefined && precision !== null) ? precision : 2;

            if (curSymbol === '₹' || curIso === 'INR' || (!curSymbol && !curIso)) {
                if (val >= 10000000) {
                    return prefix + (val / 10000000).toFixed(2) + ' Cr';
                } else if (val >= 100000) {
                    return prefix + (val / 100000).toFixed(2) + ' L';
                }
            } else {
                if (val >= 1000000) {
                    return prefix + (val / 1000000).toFixed(2) + ' M';
                } else if (val >= 1000) {
                    return prefix + (val / 1000).toFixed(1) + ' k';
                }
            }

            return prefix + val.toLocaleString(window.navigator.language, {
                minimumFractionDigits: (val % 1 === 0) ? 0 : Math.min(prec, 2),
                maximumFractionDigits: prec
            });
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-elc-hidden', !show);
        }

        function createWidget() {
            $wrapper.empty();
            $root.empty();

            // 1. Header with Title & Arrow-less Month / Year Filters
            $head = $('<div class="vas-elc-head">');
            var $titleWrap = $('<div class="vas-elc-head-txt">');
            $titleWrap.append($('<p class="vas-elc-title">').text(label('VAS_ExpectedLandedCostOnPO', 'Expected Landed Cost on PO')));

            var $filterWrap = $('<div class="vas-elc-mfilter">');
            $monthSelect = $('<select class="vas-elc-select vas-elc-select-month">').attr('aria-label', label('VAS_Month', 'Month'));
            for (var m = 0; m < 12; m++) {
                var optM = $('<option>').val(m + 1).text(MONTH_NAMES[m]);
                if (m + 1 === selectedMonth) { optM.prop('selected', true); }
                $monthSelect.append(optM);
            }

            $yearSelect = $('<select class="vas-elc-select vas-elc-select-year">').attr('aria-label', label('VAS_Year', 'Year'));
            var baseYear = now.getFullYear();
            for (var y = baseYear - 2; y <= baseYear + 1; y++) {
                var optY = $('<option>').val(y).text(y);
                if (y === selectedYear) { optY.prop('selected', true); }
                $yearSelect.append(optY);
            }

            $monthSelect.on('change', function () {
                selectedMonth = parseInt($(this).val(), 10);
                pageNo = 1;
                loadData();
            });

            $yearSelect.on('change', function () {
                selectedYear = parseInt($(this).val(), 10);
                pageNo = 1;
                loadData();
            });

            $filterWrap.append($monthSelect).append($yearSelect);
            $head.append($titleWrap).append($filterWrap);
            $root.append($head);

            // 2. Summary Block (Top)
            $topSummary = $('<div class="vas-elc-top">');
            var $summaryLeft = $('<div>');
            $totalEl = $('<div class="vas-elc-big">').text('—');
            $metaEl = $('<div class="vas-elc-meta">').text('—');
            $summaryLeft.append($totalEl).append($metaEl);
            $topSummary.append($summaryLeft);
            $root.append($topSummary);

            // 3. Cost Elements List Area
            $listContainer = $('<div class="vas-elc-list">');
            $emptyEl = $('<div class="vas-elc-empty vas-elc-hidden">');
            $root.append($listContainer).append($emptyEl);

            // 4. Footer Pager (compact)
            $foot = $('<div class="vas-elc-foot">');
            $helperEl = $('<span class="vas-elc-helper">');

            $pagerEl = $('<span class="vas-elc-pager">');
            $prevBtn = $('<button type="button" class="vas-elc-pbtn" aria-label="' + label('VAS_Previous', 'Previous') + '">')
                .html('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>');
            $pageTxt = $('<span class="vas-elc-ptxt">').text('1 of 1');
            $nextBtn = $('<button type="button" class="vas-elc-pbtn" aria-label="' + label('VAS_Next', 'Next') + '">')
                .html('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>');

            $prevBtn.on('click', function (e) {
                e.stopPropagation();
                if (pageNo > 1) {
                    pageNo--;
                    renderList();
                }
            });

            $nextBtn.on('click', function (e) {
                e.stopPropagation();
                if (pageNo < totalPages) {
                    pageNo++;
                    renderList();
                }
            });

            $pagerEl.append($prevBtn).append($pageTxt).append($nextBtn);
            $foot.append($helperEl).append($pagerEl);
            $root.append($foot);

            // 5. Busy indicator
            $busy = $('<div class="vas-elc-busy vas-elc-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_212_ExpectedLandedCostWidget/GetExpectedLandedCost',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) {
                        renderError();
                        return;
                    }

                    totalCost = Number(data.totalExpectedCost || 0);
                    openPOCount = Number(data.openPOCount || 0);
                    curSymbol = data.curSymbol || '';
                    curIso = data.curIso || '';
                    stdPrecision = data.stdPrecision !== undefined ? data.stdPrecision : 2;
                    costElements = data.costElements || [];

                    pageNo = 1;
                    renderData();
                },
                error: function () {
                    renderError();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderData() {
            // Render Top Summary
            var formattedTotal = formatCurrency(totalCost, curSymbol || curIso, stdPrecision);
            $totalEl.text(formattedTotal).attr('title', formattedTotal);

            var metaText = openPOCount === 1
                ? label('VAS_AcrossOpenPO', 'Across {0} open PO').replace('{0}', openPOCount)
                : label('VAS_AcrossOpenPOs', 'Across {0} open POs').replace('{0}', openPOCount);
            $metaEl.text(metaText).attr('title', metaText);

            renderList();
        }

        function renderList() {
            $listContainer.empty();

            if (!costElements || costElements.length === 0) {
                $listContainer.addClass('vas-elc-hidden');
                $emptyEl.removeClass('vas-elc-hidden')
                    .text(label('VAS_NoExpectedLandedCost', 'No expected landed cost for the selected month.'));
                $foot.addClass('vas-elc-hidden');
                return;
            }

            $emptyEl.addClass('vas-elc-hidden');
            $listContainer.removeClass('vas-elc-hidden');

            totalPages = Math.max(1, Math.ceil(costElements.length / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(startIndex + pageSize, costElements.length);
            var pageItems = costElements.slice(startIndex, endIndex);

            // Find maximum amount in the full dataset for proportional bar calculation
            var maxAmount = 0;
            for (var i = 0; i < costElements.length; i++) {
                if (costElements[i].totalAmount > maxAmount) {
                    maxAmount = costElements[i].totalAmount;
                }
            }
            if (maxAmount <= 0) { maxAmount = 1; }

            for (var j = 0; j < pageItems.length; j++) {
                var item = pageItems[j];
                var globalIndex = startIndex + j;
                var color = PASTEL_COLORS[globalIndex % PASTEL_COLORS.length];
                var percentage = Math.min(100, Math.max(2, (item.totalAmount / maxAmount) * 100));

                var $row = $('<div class="vas-elc-row vas-elc-row-click">')
                    .attr('data-cost-element-id', item.costElementId)
                    .attr('role', 'button')
                    .attr('tabindex', 0)
                    .attr('title', item.costElementName);

                var $line = $('<div class="vas-elc-line">');
                var $name = $('<span class="vas-elc-name">').text(item.costElementName).attr('title', item.costElementName);
                var formattedAmount = formatCurrency(item.totalAmount, curSymbol || curIso, stdPrecision);
                var $val = $('<span class="vas-elc-val">').text(formattedAmount).attr('title', formattedAmount);

                $line.append($name).append($val);

                var $track = $('<div class="vas-elc-track">');
                var $fill = $('<span class="vas-elc-fill">')
                    .css({ 'width': percentage + '%', 'background-color': color });
                $track.append($fill);

                $row.append($line).append($track);
                $listContainer.append($row);
            }

            // Clicking a cost element opens its purchase-order breakdown.
            $listContainer.off('click.elc keydown.elc')
                .on('click.elc', '.vas-elc-row-click', function () {
                    var id = parseInt($(this).attr('data-cost-element-id'), 10);
                    if (id > 0) { openCostModal(id, $(this).attr('title')); }
                })
                .on('keydown.elc', '.vas-elc-row-click', function (e) {
                    if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                        e.preventDefault();
                        $(this).trigger('click');
                    }
                });

            // Footer Pager rendering
            if (costElements.length > pageSize) {
                $foot.removeClass('vas-elc-hidden');
                var helperTpl = label('VAS_ShowingCostElements', 'Showing {0}–{1} of {2} cost elements');
                var helperText = helperTpl
                    .replace('{0}', startIndex + 1)
                    .replace('{1}', endIndex)
                    .replace('{2}', costElements.length);
                $helperEl.text(helperText);

                $pageTxt.text(pageNo + ' ' + label('VAS_Of', 'of') + ' ' + totalPages);
                $prevBtn.prop('disabled', pageNo <= 1);
                $nextBtn.prop('disabled', pageNo >= totalPages);
            } else {
                $foot.addClass('vas-elc-hidden');
            }
        }

        function renderError() {
            $totalEl.text('—');
            $metaEl.text('—');
            $listContainer.empty().addClass('vas-elc-hidden');
            $emptyEl.removeClass('vas-elc-hidden')
                .text(label('VAS_CouldntLoadExpectedCost', "Couldn't load expected landed cost."));
            $foot.addClass('vas-elc-hidden');
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $wrapper;
        };


        /* ============================================================
           COST ELEMENT DRILL-DOWN MODAL
           Clicking a cost element row opens the open purchase orders that make
           up that element's expected landed cost for the selected month.
           ============================================================ */
        var $elcMask = null;

        function ensureCostModal() {
            if ($elcMask && $elcMask.length && document.body.contains($elcMask[0])) { return; }

            $elcMask = $(
                '<div class="vas-elc-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-elc-modal">' +
                        '<div class="vas-elc-modal-header">' +
                            '<div class="vas-elc-mhead-txt">' +
                                '<h2 class="vas-elc-mtitle"></h2>' +
                                '<div class="vas-elc-msub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-elc-mclose" aria-label="' + escapeHtml(label('VAS_Close', 'Close')) + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="vas-elc-modal-body"></div>' +
                        '<div class="vas-elc-modal-foot">' +
                            '<span class="vas-elc-mnote"></span>' +
                            '<button type="button" class="vas-elc-mbtn">' + escapeHtml(label('VAS_Close', 'Close')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($elcMask);
            $elcMask.find('.vas-elc-mclose, .vas-elc-mbtn').on('click', closeCostModal);
            $elcMask.on('mousedown', function (e) {
                if (e.target === $elcMask[0]) { closeCostModal(); }
            });
        }

        function closeCostModal() {
            if ($elcMask) { $elcMask.removeClass('vas-elc-mask-open'); }
        }

        function openCostModal(costElementId, costElementName) {
            ensureCostModal();
            $elcMask.find('.vas-elc-mtitle').text(costElementName || label('VAS_LandedCost', 'Landed cost'));
            $elcMask.find('.vas-elc-msub').text(label('VAS_Loading', 'Loading...'));
            $elcMask.find('.vas-elc-modal-body').empty();
            $elcMask.find('.vas-elc-mnote').text('');
            $elcMask.addClass('vas-elc-mask-open');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_212_ExpectedLandedCostWidget/GetCostElementDetail',
                type: 'GET',
                cache: false,
                data: { costElementId: costElementId, month: selectedMonth, year: selectedYear },
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') { try { data = JSON.parse(data); } catch (e) { } }
                    if (typeof data === 'string') { try { data = JSON.parse(data); } catch (e) { } }
                    data = data || {};
                    if (!data.success) { renderCostModalState(label('VAS_FailedToLoad', 'Failed to load data')); return; }
                    renderCostModalRows(data);
                },
                error: function () {
                    renderCostModalState(label('VAS_FailedToLoad', 'Failed to load data'));
                }
            });
        }

        function renderCostModalState(text) {
            $elcMask.find('.vas-elc-msub').text('');
            $elcMask.find('.vas-elc-modal-body')
                .html('<div class="vas-elc-mempty">' + escapeHtml(text) + '</div>');
        }

        function renderCostModalRows(data) {
            var rows = data.rows || [];
            var sym = data.curSymbol || data.curIso || '';
            var prec = (typeof data.stdPrecision === 'number') ? data.stdPrecision : 2;

            $elcMask.find('.vas-elc-msub').text(
                label('VAS_ExpectedLandedCost', 'Expected landed cost') + ' · ' +
                formatCurrency(data.totalAmount, sym, prec));

            if (!rows.length) {
                renderCostModalState(label('VAS_NoPOsFound', 'No purchase orders found'));
                return;
            }

            var head =
                '<div class="vas-elc-mrow vas-elc-mhead">' +
                    '<span class="vas-elc-mcell">' + escapeHtml(label('VAS_PurchaseOrder', 'Purchase order')) + '</span>' +
                    '<span class="vas-elc-mcell">' + escapeHtml(label('VAS_Vendor', 'Vendor')) + '</span>' +
                    '<span class="vas-elc-mcell">' + escapeHtml(label('VAS_Warehouse', 'Warehouse')) + '</span>' +
                    '<span class="vas-elc-mcell">' + escapeHtml(label('VAS_PODate', 'PO date')) + '</span>' +
                    '<span class="vas-elc-mcell">' + escapeHtml(label('VAS_Status', 'Status')) + '</span>' +
                    '<span class="vas-elc-mcell vas-elc-mright">' + escapeHtml(label('VAS_Amount', 'Amount')) + '</span>' +
                '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var amt = formatCurrency(r.amount, sym, prec);
                body +=
                    '<div class="vas-elc-mrow">' +
                        '<span class="vas-elc-mcell" title="' + escapeHtml(r.purchaseOrderNo) + '">' + escapeHtml(r.purchaseOrderNo) + '</span>' +
                        '<span class="vas-elc-mcell" title="' + escapeHtml(r.vendorName) + '">' + escapeHtml(r.vendorName) + '</span>' +
                        '<span class="vas-elc-mcell" title="' + escapeHtml(r.warehouseName) + '">' + escapeHtml(r.warehouseName) + '</span>' +
                        '<span class="vas-elc-mcell">' + escapeHtml(r.orderDate) + '</span>' +
                        '<span class="vas-elc-mcell">' + escapeHtml(docStatusText(r.docStatus)) + '</span>' +
                        '<span class="vas-elc-mcell vas-elc-mright">' + escapeHtml(amt) + '</span>' +
                    '</div>';
            }

            $elcMask.find('.vas-elc-modal-body').html(head + '<div class="vas-elc-mbody">' + body + '</div>');
            $elcMask.find('.vas-elc-mnote').text(
                label('VAS_Showing', 'Showing') + ' ' + rows.length + ' ' +
                label('VAS_PurchaseOrders', 'purchase orders'));
        }

        function docStatusText(code) {
            var c = String(code || '').toUpperCase();
            if (c === 'DR') { return label('VAS_Drafted', 'Drafted'); }
            if (c === 'IP') { return label('VAS_InProcess', 'In process'); }
            if (c === 'CO') { return label('VAS_Completed', 'Completed'); }
            if (c === 'CL') { return label('VAS_Closed', 'Closed'); }
            return c;
        }

        this.disposeComponent = function () {
            // The modal lives on <body>, so it must be removed explicitly.
            if ($elcMask) { $elcMask.off(); $elcMask.remove(); $elcMask = null; }
            $wrapper.remove();
        };
    };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }
        this.windowNo = windowNo;
        this.Initalize();
        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_212_ExpectedLandedCostWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
