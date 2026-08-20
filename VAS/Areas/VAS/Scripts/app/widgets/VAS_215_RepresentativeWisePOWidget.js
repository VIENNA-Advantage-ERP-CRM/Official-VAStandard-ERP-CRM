/**
 * VAS_215_RepresentativeWisePOWidget
 * Purchase Order Dashboard - Widget 13: Representative Wise PO (3x2 Ranked Bar List)
 *
 * Summary:
 *   A ranked bar list of Purchase Order value by buying representative (C_Order.SalesRep_ID)
 *   for the selected month, paginated five at a time, each opening that representative's
 *   purchase orders with full modal drill-down, average cycle time calculation,
 *   PO line inspection, and PO record navigation.
 *
 * Message Table:
 *   # | Fallback Text                                    | Message Key
 *  ---+--------------------------------------------------+-----------------------------------
 *   1 | Representative wise PO                           | VAS_RepresentativeWisePO
 *   2 | Month                                            | VAS_Month
 *   3 | Year                                             | VAS_Year
 *   4 | Ranked by PO value                               | VAS_RankedByPOValue
 *   5 | Showing                                          | VAS_Showing
 *   6 | of                                               | VAS_Of
 *   7 | PO value                                         | VAS_POValue
 *   8 | POs raised                                       | VAS_POsRaised
 *   9 | Share of month                                   | VAS_ShareOfMonth
 *  10 | Avg cycle                                        | VAS_AvgCycle
 *  11 | Purchase orders                                  | VAS_PurchaseOrders
 *  12 | Purchase order lines                             | VAS_PurchaseOrderLines
 *  13 | Purchase orders raised by this representative    | VAS_PurchaseOrdersRaisedByRep
 *  14 | PO No                                            | VAS_PONo
 *  15 | PO date                                          | VAS_PODate
 *  16 | Vendor                                           | VAS_Vendor
 *  17 | Warehouse                                        | VAS_Warehouse
 *  18 | Representative                                   | VAS_Representative
 *  19 | Value                                            | VAS_Value
 *  20 | Delivery                                         | VAS_Delivery
 *  21 | Status                                           | VAS_Status
 *  22 | Product                                          | VAS_Product
 *  23 | Attribute                                        | VAS_Attribute
 *  24 | UoM                                              | VAS_UoM
 *  25 | Ordered                                          | VAS_Ordered
 *  26 | Received                                         | VAS_Received
 *  27 | Pending                                          | VAS_Pending
 *  28 | Rate                                             | VAS_Rate
 *  29 | Amount                                           | VAS_Amount
 *  30 | Line status                                      | VAS_LineStatus
 *  31 | Lines                                            | VAS_Lines
 *  32 | Qty ordered                                      | VAS_QtyOrdered
 *  33 | Qty pending                                      | VAS_QtyPending
 *  34 | Back                                             | VAS_Back
 *  35 | Close                                            | VAS_Close
 *  36 | select a PO number to open the record            | VAS_SelectPOToOpen
 *  37 | Lines of                                         | VAS_LinesOf
 *  38 | No representatives found for this period         | VAS_NoRepresentativesFound
 *  39 | Failed to load representative data               | VAS_FailedToLoadRepresentativeData
 *  40 | Retry                                            | VAS_Retry
 *  41 | Loading...                                       | VAS_Loading
 *  42 | No purchase orders found                         | VAS_NoPOsFound
 *  43 | No lines found                                   | VAS_NoLinesFound
 *  44 | Previous                                         | VAS_Previous
 *  45 | Next                                             | VAS_Next
 *  46 | Open in Window                                   | VAS_OpenInWindow
 *  47 | Drafted                                          | VAS_Drafted
 *  48 | In process                                       | VAS_InProcess
 *  49 | Completed                                        | VAS_Completed
 *  50 | Closed                                           | VAS_Closed
 *  51 | Voided                                           | VAS_Voided
 *  52 | Reversed                                         | VAS_Reversed
 *  53 | Fully delivered                                  | VAS_FullyDelivered
 *  54 | Partial                                          | VAS_Partial
 *  55 | Not applicable                                   | VAS_NotApplicable
 *  56 | Partial received                                 | VAS_PartialReceived
 *  57 | Day                                              | VAS_Day
 *  58 | Days                                             | VAS_Days
 *  59 | Purchase order                                   | VAS_PurchaseOrder
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .vis-widget-body, body')[0] || document.documentElement;
        var write = function () {
            var w = container.clientWidth || window.innerWidth;
            if (w > 0) {
                document.documentElement.style.setProperty('--dash-inline-size', w + 'px');
            }
        };

        if (!window.__vasDashInlineSizeObserver && typeof ResizeObserver !== 'undefined') {
            window.__vasDashInlineSizeObserver = new ResizeObserver(write);
            window.__vasDashInlineSizeObserver.observe(container);
            window.addEventListener('resize', write);
        }
        write();
    }

    VAS.VAS_215_RepresentativeWisePOWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-rwpo-container"></div>');
        var $root = $('<div class="vas-rwpo-root"></div>');
        var $header, $listContainer, $footer, $helperText, $pagerContainer, $prevBtn, $nextBtn, $pageText;
        var $monthSelect, $yearSelect, $busy;
        var widgetObserver = null;

        var MONTH_NAMES = [
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        ];
        var MONTH_SHORT = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        var BAR_COLORS = ['#A9D2FF', '#A3E0D4', '#FFDCA1', '#CFC9F5', '#FFC7C7'];

        var now = new Date();
        var selectedMonth = now.getMonth() + 1; // 1-12
        var selectedYear = now.getFullYear();

        var representativesData = [];
        var totalMonthValue = 0;
        var totalMonthPOs = 0;
        var curSymbol = "₹";
        var curIso = "INR";
        var stdPrecision = 2;

        var currentPage = 0; // 0-indexed
        var pageSize = 5;
        var totalPages = 1;

        // Modal Stack State
        var modalStack = [];
        var currentModalCfg = null;
        var $modalHost = null;

        function lbl(key, fallback) {
            if (window.VIS && VIS.Msg && VIS.Msg.getMsg) {
                var msg = VIS.Msg.getMsg(key);
                if (msg && msg !== key && msg.indexOf('**') === -1) {
                    return msg;
                }
            }
            return fallback;
        }

        function getStatusLabel(text) {
            if (!text) return '';
            switch (text) {
                case 'Drafted': return lbl('VAS_Drafted', 'Drafted');
                case 'In process': return lbl('VAS_InProcess', 'In process');
                case 'Completed': return lbl('VAS_Completed', 'Completed');
                case 'Closed': return lbl('VAS_Closed', 'Closed');
                case 'Voided': return lbl('VAS_Voided', 'Voided');
                case 'Reversed': return lbl('VAS_Reversed', 'Reversed');
                case 'Fully delivered': return lbl('VAS_FullyDelivered', 'Fully delivered');
                case 'Partial': return lbl('VAS_Partial', 'Partial');
                case 'Not applicable': return lbl('VAS_NotApplicable', 'Not applicable');
                case 'Received': return lbl('VAS_Received', 'Received');
                case 'Partial received': return lbl('VAS_PartialReceived', 'Partial received');
                case 'Pending': return lbl('VAS_Pending', 'Pending');
                default: return text;
            }
        }

        function formatCycleText(avgCycleDays, serverText) {
            if (avgCycleDays != null && avgCycleDays >= 0) {
                return avgCycleDays === 1 ? ('1 ' + lbl('VAS_Day', 'day')) : (avgCycleDays + ' ' + lbl('VAS_Days', 'days'));
            }
            if (serverText && serverText.indexOf('day') !== -1) {
                var numVal = parseInt(serverText, 10);
                if (!isNaN(numVal)) {
                    return numVal === 1 ? ('1 ' + lbl('VAS_Day', 'day')) : (numVal + ' ' + lbl('VAS_Days', 'days'));
                }
            }
            return serverText || '—';
        }

        function esc(s) {
            return String(s == null ? '' : s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function fmtMoney(v) {
            var val = Number(v || 0);
            var sym = curSymbol || '₹';
            if (curIso === 'INR' || sym === '₹') {
                if (Math.abs(val) >= 1e7) {
                    return sym + ' ' + (val / 1e7).toFixed(2) + ' Cr';
                }
                if (Math.abs(val) >= 1e5) {
                    return sym + ' ' + (val / 1e5).toFixed(2) + ' L';
                }
                return sym + ' ' + Math.round(val).toLocaleString('en-IN');
            } else {
                if (Math.abs(val) >= 1e6) {
                    return sym + ' ' + (val / 1e6).toFixed(2) + ' M';
                }
                if (Math.abs(val) >= 1e3) {
                    return sym + ' ' + (val / 1e3).toFixed(1) + ' k';
                }
                return sym + ' ' + Math.round(val).toLocaleString();
            }
        }

        function num(v) {
            var val = Number(v || 0);
            return (curIso === 'INR' || curSymbol === '₹')
                ? Math.round(val).toLocaleString('en-IN')
                : Math.round(val).toLocaleString();
        }

        function getPeriodLabel() {
            var mName = MONTH_SHORT[selectedMonth - 1] || "";
            try {
                var d = new Date(2026, selectedMonth - 1, 1);
                var loc = (window.VIS && VIS.Env && VIS.Env.getLanguage()) ? VIS.Env.getLanguage() : undefined;
                mName = d.toLocaleString(loc || 'default', { month: 'short' });
            } catch (e) { }
            return mName + " " + selectedYear;
        }

        this.Initalize = function () {
            createWidgetUI();
            setupResizeObserver();
            loadRepresentativeWisePO();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined' || !$wrapper[0]) return;
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($wrapper[0]);
            } catch (e) { }
        }

        function createWidgetUI() {
            $root.empty();

            // 1. Header with Title and Month/Year Dropdown filters (Rule 13 Arrow-less Standard)
            $header = $('<div class="vas-rwpo-head"></div>');
            var $headTxt = $('<div class="vas-rwpo-head-txt"><p class="vas-rwpo-title" title="' + esc(lbl('VAS_RepresentativeWisePO', 'Representative wise PO')) + '">' + esc(lbl('VAS_RepresentativeWisePO', 'Representative wise PO')) + '</p></div>');

            var $mfilter = $('<div class="vas-rwpo-mfilter"></div>');
            $monthSelect = $('<select class="vas-rwpo-sel vas-rwpo-sel-month" aria-label="' + esc(lbl('VAS_Month', 'Month')) + '"></select>');
            $yearSelect = $('<select class="vas-rwpo-sel vas-rwpo-sel-year" aria-label="' + esc(lbl('VAS_Year', 'Year')) + '"></select>');

            for (var m = 1; m <= 12; m++) {
                var mName = MONTH_NAMES[m - 1];
                try {
                    var d = new Date(2026, m - 1, 1);
                    var loc = (window.VIS && VIS.Env && VIS.Env.getLanguage()) ? VIS.Env.getLanguage() : undefined;
                    mName = d.toLocaleString(loc || 'default', { month: 'long' });
                } catch (e) { }
                var mOpt = $('<option value="' + m + '">' + mName + '</option>');
                if (m === selectedMonth) mOpt.prop('selected', true);
                $monthSelect.append(mOpt);
            }

            var startY = selectedYear - 2;
            var endY = selectedYear + 1;
            for (var y = startY; y <= endY; y++) {
                var yOpt = $('<option value="' + y + '">' + y + '</option>');
                if (y === selectedYear) yOpt.prop('selected', true);
                $yearSelect.append(yOpt);
            }

            $mfilter.append($monthSelect).append($yearSelect);
            $header.append($headTxt).append($mfilter);

            // 2. Ranked Bar List Container
            $listContainer = $('<div class="vas-rwpo-hlist" id="vas_rwpo_list_' + Math.random().toString(36).substr(2, 6) + '"></div>');

            // 3. Footer with Helper and Pager
            $footer = $('<div class="vas-rwpo-wfoot"></div>');
            $helperText = $('<span class="vas-rwpo-helper"></span>');
            $pagerContainer = $('<div class="vas-rwpo-pager"></div>');

            $prevBtn = $('<button type="button" class="vas-rwpo-pbtn" aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '" disabled>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>');
            $pageText = $('<span class="vas-rwpo-ptxt">1 ' + lbl('VAS_Of', 'of') + ' 1</span>');
            $nextBtn = $('<button type="button" class="vas-rwpo-pbtn" aria-label="' + esc(lbl('VAS_Next', 'Next')) + '" disabled>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>');

            $pagerContainer.append($prevBtn).append($pageText).append($nextBtn);
            $footer.append($helperText).append($pagerContainer);

            // 4. Busy Indicator
            $busy = $('<div class="vas-rwpo-busy vas-rwpo-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            $root.append($header).append($listContainer).append($footer).append($busy);
            $wrapper.append($root);

            // Event Listeners
            $monthSelect.on('change', function (e) {
                e.stopPropagation();
                selectedMonth = parseInt($(this).val(), 10);
                loadRepresentativeWisePO();
            });

            $yearSelect.on('change', function (e) {
                e.stopPropagation();
                selectedYear = parseInt($(this).val(), 10);
                loadRepresentativeWisePO();
            });

            $prevBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage > 0) {
                    currentPage--;
                    renderRepresentativePage();
                }
            });

            $nextBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage < totalPages - 1) {
                    currentPage++;
                    renderRepresentativePage();
                }
            });

            $listContainer.on('click', '.vas-rwpo-hrow', function (e) {
                e.stopPropagation();
                var repId = parseInt($(this).data('rep-id'), 10);
                var repName = $(this).data('rep-name');
                if (repId > 0) {
                    openRepresentativeDetailModal(repId, repName);
                }
            });
        }

        function showBusy(show) {
            if ($busy && $busy[0]) {
                $busy.toggleClass('vas-rwpo-hidden', !show);
            }
        }

        function loadRepresentativeWisePO() {
            showBusy(true);
            renderSkeletonList();

            var url = VIS.Application.contextUrl + 'VAS_215_RepresentativeWisePOWidget/GetRepresentativeWisePO';
            $.ajax({
                url: url,
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.success) {
                        curSymbol = data.curSymbol || "₹";
                        curIso = data.curIso || "INR";
                        stdPrecision = data.stdPrecision || 2;
                        totalMonthValue = data.totalMonthValue || 0;
                        totalMonthPOs = data.totalMonthPOs || 0;
                        representativesData = data.representatives || [];
                    } else {
                        representativesData = [];
                    }
                    currentPage = 0;
                    renderRepresentativePage();
                },
                error: function () {
                    representativesData = [];
                    renderErrorState();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            return data || {};
        }

        function renderSkeletonList() {
            $listContainer.empty();
            var skeletonHtml = '';
            for (var i = 0; i < 5; i++) {
                skeletonHtml += '<div class="vas-rwpo-hrow vas-rwpo-skeleton">' +
                    '<span class="vas-rwpo-line"><span class="vas-rwpo-nm" style="width:45%;height:1em;background:#EAF1F7;border-radius:4px;"></span>' +
                    '<span class="vas-rwpo-vl" style="width:25%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '<span class="vas-rwpo-track"><span class="vas-rwpo-fill" style="width:0%;"></span></span></div>';
            }
            $listContainer.html(skeletonHtml);
            $helperText.text(lbl('VAS_Loading', 'Loading...'));
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevBtn.prop('disabled', true);
            $nextBtn.prop('disabled', true);
        }

        function renderErrorState() {
            $listContainer.empty();
            var errHtml = $('<div class="vas-rwpo-empty-box">' +
                '<p class="vas-rwpo-empty-msg">' + esc(lbl('VAS_FailedToLoadRepresentativeData', 'Failed to load representative data')) + '</p>' +
                '<button type="button" class="vas-rwpo-retry-btn">' + esc(lbl('VAS_Retry', 'Retry')) + '</button>' +
                '</div>');
            errHtml.find('.vas-rwpo-retry-btn').on('click', function () {
                loadRepresentativeWisePO();
            });
            $listContainer.append(errHtml);
            $helperText.text('0 ' + lbl('VAS_Of', 'of') + ' 0');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevBtn.prop('disabled', true);
            $nextBtn.prop('disabled', true);
        }

        function renderRepresentativePage() {
            $listContainer.empty();
            var count = representativesData.length;

            if (count === 0) {
                var emptyHtml = '<div class="vas-rwpo-empty-box">' +
                    '<p class="vas-rwpo-empty-msg">' + esc(lbl('VAS_NoRepresentativesFound', 'No representatives found for this period')) + '</p>' +
                    '</div>';
                $listContainer.html(emptyHtml);
                $helperText.text('0 ' + lbl('VAS_Of', 'of') + ' 0');
                $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            totalPages = Math.max(1, Math.ceil(count / pageSize));
            if (currentPage > totalPages - 1) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;

            var startIdx = currentPage * pageSize;
            var endIdx = Math.min(count, startIdx + pageSize);
            var pageReps = representativesData.slice(startIdx, endIdx);

            var rowsHtml = '';
            for (var i = 0; i < pageReps.length; i++) {
                var r = pageReps[i];
                var colorIndex = (startIdx + i) % BAR_COLORS.length;
                var barColor = BAR_COLORS[colorIndex];
                var formattedVal = fmtMoney(r.value);
                var valueLine = formattedVal + ' · ' + r.pos;
                var barPct = Math.max(2, Math.min(100, r.pct || 0));

                rowsHtml += '<button type="button" class="vas-rwpo-hrow pickable" data-rep-id="' + r.representativeId + '" data-rep-name="' + esc(r.name) + '">' +
                    '<span class="vas-rwpo-line">' +
                    '<span class="vas-rwpo-nm" title="' + esc(r.name) + '">' + esc(r.name) + '</span>' +
                    '<span class="vas-rwpo-vl" title="' + esc(valueLine) + '">' + esc(valueLine) + '</span>' +
                    '</span>' +
                    '<span class="vas-rwpo-track">' +
                    '<span class="vas-rwpo-fill" style="width:' + barPct + '%;background:' + barColor + ';"></span>' +
                    '</span>' +
                    '</button>';
            }

            $listContainer.html(rowsHtml);

            // Update footer helper and pager
            var helperString = lbl('VAS_Showing', 'Showing') + ' ' + (startIdx + 1) + '–' + endIdx + ' ' +
                lbl('VAS_Of', 'of') + ' ' + count + ' · ' + lbl('VAS_RankedByPOValue', 'ranked by PO value');
            $helperText.text(helperString);
            $pageText.text((currentPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);

            $prevBtn.prop('disabled', currentPage === 0);
            $nextBtn.prop('disabled', currentPage >= totalPages - 1);
        }

        /* ============================================================
           MODAL ENGINE & DRILL-DOWNS
           ============================================================ */

        function getModalHost() {
            if (!$modalHost || !$modalHost[0] || !document.body.contains($modalHost[0])) {
                $('#vas_rwpo_modal_mask').remove();
                $modalHost = $('<div class="vas-rwpo-mask" id="vas_rwpo_modal_mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-rwpo-modal" id="vas_rwpo_modal_box">' +
                    '  <div class="vas-rwpo-modal-header">' +
                    '    <div class="vas-rwpo-mhead-left">' +
                    '      <button type="button" class="vas-rwpo-xbtn vas-rwpo-mback" id="vas_rwpo_mBack" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" hidden>' +
                    '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                    '      </button>' +
                    '      <div class="vas-rwpo-htxt">' +
                    '        <h2 id="vas_rwpo_mTitle"></h2>' +
                    '        <div class="vas-rwpo-msub" id="vas_rwpo_mSub"></div>' +
                    '      </div>' +
                    '    </div>' +
                    '    <div class="vas-rwpo-hact">' +
                    '      <button type="button" class="vas-rwpo-xbtn" id="vas_rwpo_mClose" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                    '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                    '      </button>' +
                    '    </div>' +
                    '  </div>' +
                    '  <div class="vas-rwpo-modal-body" id="vas_rwpo_mBody"></div>' +
                    '  <div class="vas-rwpo-modal-foot" id="vas_rwpo_mFoot"></div>' +
                    '</div>' +
                    '</div>');

                $('body').append($modalHost);

                $modalHost.find('#vas_rwpo_mClose').on('click', function () {
                    closeModal();
                });

                $modalHost.find('#vas_rwpo_mBack').on('click', function () {
                    popModal();
                });

                $modalHost.on('click', function (e) {
                    if (e.target === $modalHost[0]) {
                        closeModal();
                    }
                });

                $(document).off('keydown.vas_rwpo_esc').on('keydown.vas_rwpo_esc', function (e) {
                    if (e.key === 'Escape' && $modalHost.hasClass('vas-rwpo-open')) {
                        closeModal();
                    }
                });
            }
            return $modalHost;
        }

        function openModal(cfg, isBack) {
            var $host = getModalHost();
            if (!isBack) {
                if (cfg.isChild && currentModalCfg) {
                    modalStack.push(currentModalCfg);
                } else if (!cfg.isChild) {
                    modalStack = [];
                }
                currentModalCfg = cfg;
            } else {
                currentModalCfg = cfg;
            }

            var $backBtn = $host.find('#vas_rwpo_mBack');
            if (modalStack.length > 0) {
                $backBtn.removeAttr('hidden').show();
            } else {
                $backBtn.attr('hidden', 'hidden').hide();
            }

            var $box = $host.find('#vas_rwpo_modal_box');
            $box.removeClass('vas-rwpo-modal-sm vas-rwpo-modal-md');
            if (cfg.size === 'sm') $box.addClass('vas-rwpo-modal-sm');
            if (cfg.size === 'md') $box.addClass('vas-rwpo-modal-md');

            $host.find('#vas_rwpo_mTitle').text(cfg.title || '');
            $host.find('#vas_rwpo_mSub').text(cfg.subtitle || '');

            var $mBody = $host.find('#vas_rwpo_mBody');
            $mBody.empty();
            if (typeof cfg.body === 'function') {
                cfg.body($mBody);
            } else if (cfg.body) {
                $mBody.html(cfg.body);
            }

            var $mFoot = $host.find('#vas_rwpo_mFoot');
            $mFoot.empty();
            if (typeof cfg.foot === 'function') {
                cfg.foot($mFoot);
            } else if (cfg.foot) {
                $mFoot.html(cfg.foot);
            } else {
                var $defFoot = $('<span class="vas-rwpo-foot-note"></span>' +
                    '<button type="button" class="vas-rwpo-btn vas-rwpo-btn-close">' + esc(lbl('VAS_Close', 'Close')) + '</button>');
                $defFoot.filter('.vas-rwpo-btn-close').on('click', closeModal);
                $mFoot.append($defFoot);
            }

            $host.addClass('vas-rwpo-open');

            if (cfg.afterRender) {
                cfg.afterRender($host);
            }
        }

        function popModal() {
            var prevCfg = modalStack.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            openModal(prevCfg, true);
        }

        function closeModal() {
            if ($modalHost) {
                $modalHost.removeClass('vas-rwpo-open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        function openRepresentativeDetailModal(repId, repName) {
            var rObj = null;
            for (var i = 0; i < representativesData.length; i++) {
                if (representativesData[i].representativeId === repId) {
                    rObj = representativesData[i];
                    break;
                }
            }

            var periodStr = getPeriodLabel();
            var title = repName || (rObj ? rObj.name : lbl('VAS_Representative', 'Representative'));
            var subtitle = lbl('VAS_PurchaseOrdersRaisedByRep', 'Purchase orders raised by this representative') + ' · ' + periodStr;

            var rSpend = rObj ? rObj.value : 0;
            var rPOs = rObj ? rObj.pos : 0;
            var rShare = rObj ? (rObj.share * 100).toFixed(1) + '%' : '0.0%';
            var rAvgCycle = rObj ? formatCycleText(rObj.avgCycle, rObj.avgCycleText) : '—';

            var statStripHtml = '<div class="vas-rwpo-mstats">' +
                '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_POValue', 'PO value')) + '</div><div class="v" id="vas_rwpo_stat_val" title="' + esc(fmtMoney(rSpend)) + '">' + esc(fmtMoney(rSpend)) + '</div></div>' +
                '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_POsRaised', 'POs raised')) + '</div><div class="v" id="vas_rwpo_stat_pos">' + num(rPOs) + '</div></div>' +
                '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_ShareOfMonth', 'Share of month')) + '</div><div class="v" id="vas_rwpo_stat_share">' + esc(rShare) + '</div></div>' +
                '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_AvgCycle', 'Avg cycle')) + '</div><div class="v" id="vas_rwpo_stat_cycle">' + esc(rAvgCycle) + '</div></div>' +
                '</div>' +
                '<div class="vas-rwpo-msec">' + esc(lbl('VAS_PurchaseOrders', 'Purchase orders')) + '</div>' +
                '<div class="vas-rwpo-mtbl-wrap" id="vas_rwpo_po_table_wrap"></div>';

            openModal({
                isChild: false,
                title: title,
                subtitle: subtitle,
                body: statStripHtml,
                foot: function ($foot) {
                    $foot.html('<span class="vas-rwpo-foot-note">' + esc(lbl('VAS_SelectPOToOpen', 'select a PO number to open the record')) + '</span>' +
                        '<button type="button" class="vas-rwpo-btn vas-rwpo-close-btn">' + esc(lbl('VAS_Close', 'Close')) + '</button>');
                    $foot.find('.vas-rwpo-close-btn').on('click', closeModal);
                },
                afterRender: function ($host) {
                    loadRepresentativePurchaseOrders(repId, title, $host);
                }
            });
        }

        function loadRepresentativePurchaseOrders(repId, repName, $host) {
            var $container = $host.find('#vas_rwpo_po_table_wrap');
            $container.html('<div class="vas-rwpo-loading-row">' + esc(lbl('VAS_Loading', 'Loading...')) + '</div>');

            var url = VIS.Application.contextUrl + 'VAS_215_RepresentativeWisePOWidget/GetRepresentativePurchaseOrders';
            $.ajax({
                url: url,
                type: 'GET',
                data: { representativeId: repId, month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var orders = data.orders || [];

                    // Reconcile stats from real order response
                    if (data.posRaised != null) {
                        $host.find('#vas_rwpo_stat_pos').text(num(data.posRaised));
                    }
                    if (data.repTotalValue != null) {
                        $host.find('#vas_rwpo_stat_val').text(fmtMoney(data.repTotalValue)).attr('title', fmtMoney(data.repTotalValue));
                        var sharePct = totalMonthValue > 0 ? ((data.repTotalValue / totalMonthValue) * 100).toFixed(1) + '%' : '0.0%';
                        $host.find('#vas_rwpo_stat_share').text(sharePct);
                    }
                    if (data.avgCycleText != null) {
                        $host.find('#vas_rwpo_stat_cycle').text(formatCycleText(null, data.avgCycleText));
                    }

                    renderPagedPOTable($container, orders, repName);
                },
                error: function () {
                    $container.html('<div class="vas-rwpo-empty-box"><p class="vas-rwpo-empty-msg">' + esc(lbl('VAS_FailedToLoadRepresentativeData', 'Failed to load representative data')) + '</p></div>');
                }
            });
        }

        function renderPagedPOTable($container, orders, repName) {
            $container.empty();
            var mPage = 0;
            var mPageSize = 10;
            var mTotalPages = Math.max(1, Math.ceil(orders.length / mPageSize));

            var $tableWrap = $('<div class="vas-rwpo-paged-table-wrap"></div>');
            var $tableHead = $('<div class="vas-rwpo-mrow vas-rwpo-mhead" style="grid-template-columns: minmax(0, 0.35fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.6fr) minmax(0, 1.2fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.1fr) minmax(0, 1.1fr);">' +
                '<span class="vas-rwpo-cell"></span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_PONo', 'PO No')) + '">' + esc(lbl('VAS_PONo', 'PO No')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_PODate', 'PO date')) + '">' + esc(lbl('VAS_PODate', 'PO date')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Vendor', 'Vendor')) + '">' + esc(lbl('VAS_Vendor', 'Vendor')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Warehouse', 'Warehouse')) + '">' + esc(lbl('VAS_Warehouse', 'Warehouse')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Representative', 'Representative')) + '">' + esc(lbl('VAS_Representative', 'Representative')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Value', 'Value')) + '">' + esc(lbl('VAS_Value', 'Value')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Delivery', 'Delivery')) + '">' + esc(lbl('VAS_Delivery', 'Delivery')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Status', 'Status')) + '">' + esc(lbl('VAS_Status', 'Status')) + '</span>' +
                '</div>');

            var $tableBody = $('<div class="vas-rwpo-mbody"></div>');
            var $tableFoot = $('<div class="vas-rwpo-mtfoot"></div>');

            $tableWrap.append($tableHead).append($tableBody).append($tableFoot);
            $container.append($tableWrap);

            function drawPage() {
                $tableBody.empty();
                if (orders.length === 0) {
                    $tableBody.html('<div class="vas-rwpo-empty-box"><p class="vas-rwpo-empty-msg">' + esc(lbl('VAS_NoPOsFound', 'No purchase orders found')) + '</p></div>');
                    $tableFoot.html('<span class="vas-rwpo-helper">' + esc(lbl('VAS_Showing', 'Showing') + ' 0 ' + lbl('VAS_Of', 'of') + ' 0') + '</span>');
                    return;
                }

                mTotalPages = Math.max(1, Math.ceil(orders.length / mPageSize));
                if (mPage > mTotalPages - 1) mPage = mTotalPages - 1;
                if (mPage < 0) mPage = 0;

                var sIdx = mPage * mPageSize;
                var eIdx = Math.min(orders.length, sIdx + mPageSize);
                var slice = orders.slice(sIdx, eIdx);

                var iconLinesSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';

                for (var i = 0; i < slice.length; i++) {
                    var p = slice[i];
                    var formattedVal = fmtMoney(p.valueNum);
                    var delivLabel = getStatusLabel(p.deliveryText);
                    var statusLabel = getStatusLabel(p.statusText);

                    var $row = $('<div class="vas-rwpo-mrow" style="grid-template-columns: minmax(0, 0.35fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.6fr) minmax(0, 1.2fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.1fr) minmax(0, 1.1fr);">' +
                        '<span class="vas-rwpo-cell vas-rwpo-center"><button type="button" class="vas-rwpo-iconbtn" data-order-id="' + p.orderId + '" title="' + esc(lbl('VAS_LinesOf', 'Lines of') + ' ' + p.po) + '">' + iconLinesSvg + '</button></span>' +
                        '<span class="vas-rwpo-cell"><button type="button" class="vas-rwpo-lnk" data-order-id="' + p.orderId + '" title="' + esc(p.po) + '">' + esc(p.po) + '</button></span>' +
                        '<span class="vas-rwpo-cell" title="' + esc(p.dateFull) + '">' + esc(p.dateFull) + '</span>' +
                        '<span class="vas-rwpo-cell" title="' + esc(p.vendor) + '">' + esc(p.vendor) + '</span>' +
                        '<span class="vas-rwpo-cell" title="' + esc(p.wh) + '">' + esc(p.wh) + '</span>' +
                        '<span class="vas-rwpo-cell" title="' + esc(p.rep) + '">' + esc(p.rep) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right vas-rwpo-c-emph" title="' + esc(formattedVal) + '">' + esc(formattedVal) + '</span>' +
                        '<span class="vas-rwpo-cell"><span class="vas-rwpo-chip ' + esc(p.deliveryChip) + '" title="' + esc(delivLabel) + '">' + esc(delivLabel) + '</span></span>' +
                        '<span class="vas-rwpo-cell"><span class="vas-rwpo-chip ' + esc(p.statusChip) + '" title="' + esc(statusLabel) + '">' + esc(statusLabel) + '</span></span>' +
                        '</div>');

                    $tableBody.append($row);
                }

                var footHelper = lbl('VAS_Showing', 'Showing') + ' ' + (sIdx + 1) + '–' + eIdx + ' ' +
                    lbl('VAS_Of', 'of') + ' ' + orders.length + ' · ' + lbl('VAS_SelectPOToOpen', 'select a PO number to open the record');

                var pagerHtml = '<span class="vas-rwpo-helper">' + esc(footHelper) + '</span>';
                if (mTotalPages > 1) {
                    pagerHtml += '<span class="vas-rwpo-pager">' +
                        '<button type="button" class="vas-rwpo-pbtn vas-rwpo-m-prev"' + (mPage === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                        '<span class="vas-rwpo-ptxt">' + (mPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + mTotalPages + '</span>' +
                        '<button type="button" class="vas-rwpo-pbtn vas-rwpo-m-next"' + (mPage >= mTotalPages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>';
                }

                $tableFoot.html(pagerHtml);

                $tableFoot.find('.vas-rwpo-m-prev').on('click', function (e) {
                    e.stopPropagation();
                    if (mPage > 0) { mPage--; drawPage(); }
                });

                $tableFoot.find('.vas-rwpo-m-next').on('click', function (e) {
                    e.stopPropagation();
                    if (mPage < mTotalPages - 1) { mPage++; drawPage(); }
                });
            }

            drawPage();

            // Link click: open PO record
            $tableWrap.on('click', '.vas-rwpo-lnk', function (e) {
                e.stopPropagation();
                var orderId = parseInt($(this).data('order-id'), 10);
                if (orderId > 0) {
                    openPurchaseOrderRecord(orderId);
                }
            });

            // Lines icon click: open PO lines modal
            $tableWrap.on('click', '.vas-rwpo-iconbtn', function (e) {
                e.stopPropagation();
                var orderId = parseInt($(this).data('order-id'), 10);
                if (orderId > 0) {
                    openPurchaseOrderLinesModal(orderId);
                }
            });
        }

        function openPurchaseOrderLinesModal(orderId) {
            var url = VIS.Application.contextUrl + 'VAS_215_RepresentativeWisePOWidget/GetPurchaseOrderLines';
            $.ajax({
                url: url,
                type: 'GET',
                data: { orderId: orderId },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var header = data.header || {};
                    var lines = data.lines || [];

                    var poNo = header.po || (lbl('VAS_PONo', 'PO No') + ' #' + orderId);
                    var poVendor = header.vendor || '—';
                    var poDate = header.dateFull || '—';

                    var totalOrdered = 0;
                    var totalPending = 0;
                    var totalLinesAmt = 0;
                    for (var k = 0; k < lines.length; k++) {
                        totalOrdered += (lines[k].qty || 0);
                        totalPending += (lines[k].pend || 0);
                        totalLinesAmt += (lines[k].amount || 0);
                    }

                    var linesBodyHtml = '<div class="vas-rwpo-polink">' +
                        '  <span>' + esc(lbl('VAS_PurchaseOrder', 'Purchase order')) + ' </span>' +
                        '  <button type="button" class="vas-rwpo-lnk vas-rwpo-po-direct-lnk" data-order-id="' + orderId + '">' + esc(poNo) + '</button>' +
                        '  <span> · ' + esc(poDate) + ' · ' + esc(poVendor) + '</span>' +
                        '</div>' +
                        '<div class="vas-rwpo-mstats">' +
                        '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_Lines', 'Lines')) + '</div><div class="v">' + lines.length + '</div></div>' +
                        '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_POValue', 'PO value')) + '</div><div class="v" title="' + esc(fmtMoney(totalLinesAmt)) + '">' + esc(fmtMoney(totalLinesAmt)) + '</div></div>' +
                        '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_QtyOrdered', 'Qty ordered')) + '</div><div class="v">' + num(totalOrdered) + '</div></div>' +
                        '  <div class="vas-rwpo-mstat"><div class="l">' + esc(lbl('VAS_QtyPending', 'Qty pending')) + '</div><div class="v">' + num(totalPending) + '</div></div>' +
                        '</div>' +
                        '<div class="vas-rwpo-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                        '<div class="vas-rwpo-mtbl-wrap" id="vas_rwpo_lines_table_wrap"></div>';

                    openModal({
                        isChild: true,
                        size: 'md',
                        title: lbl('VAS_Lines', 'Lines') + ' · ' + poNo,
                        subtitle: poVendor + ' · ' + poDate,
                        body: linesBodyHtml,
                        foot: function ($foot) {
                            $foot.html('<span class="vas-rwpo-foot-note">' + esc(poNo) + ' · ' + esc(poVendor) + '</span>' +
                                '<span>' +
                                '<button type="button" class="vas-rwpo-btn vas-rwpo-back-btn">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                                '<button type="button" class="vas-rwpo-btn vas-rwpo-close-btn">' + esc(lbl('VAS_Close', 'Close')) + '</button>' +
                                '</span>');
                            $foot.find('.vas-rwpo-back-btn').on('click', popModal);
                            $foot.find('.vas-rwpo-close-btn').on('click', closeModal);
                        },
                        afterRender: function ($host) {
                            $host.find('.vas-rwpo-po-direct-lnk').on('click', function () {
                                openPurchaseOrderRecord(orderId);
                            });
                            renderPagedLinesTable($host.find('#vas_rwpo_lines_table_wrap'), lines, poNo);
                        }
                    });
                }
            });
        }

        function renderPagedLinesTable($container, lines, poNo) {
            $container.empty();
            var lPage = 0;
            var lPageSize = 10;
            var lTotalPages = Math.max(1, Math.ceil(lines.length / lPageSize));

            var $tableWrap = $('<div class="vas-rwpo-paged-table-wrap"></div>');
            var $tableHead = $('<div class="vas-rwpo-mrow vas-rwpo-mhead" style="grid-template-columns: minmax(0, 0.4fr) minmax(0, 1.6fr) minmax(0, 1.2fr) minmax(0, 0.6fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 1fr) minmax(0, 1.1fr);">' +
                '<span class="vas-rwpo-cell vas-rwpo-right">#</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Product', 'Product')) + '">' + esc(lbl('VAS_Product', 'Product')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_Attribute', 'Attribute')) + '">' + esc(lbl('VAS_Attribute', 'Attribute')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_UoM', 'UoM')) + '">' + esc(lbl('VAS_UoM', 'UoM')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Ordered', 'Ordered')) + '">' + esc(lbl('VAS_Ordered', 'Ordered')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Received', 'Received')) + '">' + esc(lbl('VAS_Received', 'Received')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Pending', 'Pending')) + '">' + esc(lbl('VAS_Pending', 'Pending')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Rate', 'Rate')) + '">' + esc(lbl('VAS_Rate', 'Rate')) + '</span>' +
                '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(lbl('VAS_Amount', 'Amount')) + '">' + esc(lbl('VAS_Amount', 'Amount')) + '</span>' +
                '<span class="vas-rwpo-cell" title="' + esc(lbl('VAS_LineStatus', 'Line status')) + '">' + esc(lbl('VAS_LineStatus', 'Line status')) + '</span>' +
                '</div>');

            var $tableBody = $('<div class="vas-rwpo-mbody"></div>');
            var $tableFoot = $('<div class="vas-rwpo-mtfoot"></div>');

            $tableWrap.append($tableHead).append($tableBody).append($tableFoot);
            $container.append($tableWrap);

            function drawLinesPage() {
                $tableBody.empty();
                if (lines.length === 0) {
                    $tableBody.html('<div class="vas-rwpo-empty-box"><p class="vas-rwpo-empty-msg">' + esc(lbl('VAS_NoLinesFound', 'No lines found')) + '</p></div>');
                    $tableFoot.html('<span class="vas-rwpo-helper">' + esc(lbl('VAS_Showing', 'Showing') + ' 0 ' + lbl('VAS_Of', 'of') + ' 0') + '</span>');
                    return;
                }

                lTotalPages = Math.max(1, Math.ceil(lines.length / lPageSize));
                if (lPage > lTotalPages - 1) lPage = lTotalPages - 1;
                if (lPage < 0) lPage = 0;

                var sIdx = lPage * lPageSize;
                var eIdx = Math.min(lines.length, sIdx + lPageSize);
                var slice = lines.slice(sIdx, eIdx);

                for (var i = 0; i < slice.length; i++) {
                    var l = slice[i];
                    var rateFormatted = fmtMoney(l.rate);
                    var amtFormatted = fmtMoney(l.amount);
                    var lineStatusLabel = getStatusLabel(l.statusText);

                    var $row = $('<div class="vas-rwpo-mrow" style="grid-template-columns: minmax(0, 0.4fr) minmax(0, 1.6fr) minmax(0, 1.2fr) minmax(0, 0.6fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 1fr) minmax(0, 1.1fr);">' +
                        '<span class="vas-rwpo-cell vas-rwpo-right vas-rwpo-c-std">' + (sIdx + i + 1) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-c-prim" title="' + esc(l.name) + '">' + esc(l.name) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-c-std" title="' + esc(l.attr || '—') + '">' + esc(l.attr || '—') + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-c-std" title="' + esc(l.uom || '') + '">' + esc(l.uom || '') + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right" title="' + num(l.qty) + '">' + num(l.qty) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right" title="' + num(l.recd) + '">' + num(l.recd) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right vas-rwpo-c-prim" title="' + num(l.pend) + '">' + num(l.pend) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right" title="' + esc(rateFormatted) + '">' + esc(rateFormatted) + '</span>' +
                        '<span class="vas-rwpo-cell vas-rwpo-right vas-rwpo-c-emph" title="' + esc(amtFormatted) + '">' + esc(amtFormatted) + '</span>' +
                        '<span class="vas-rwpo-cell"><span class="vas-rwpo-chip ' + esc(l.statusChip) + '" title="' + esc(lineStatusLabel) + '">' + esc(lineStatusLabel) + '</span></span>' +
                        '</div>');

                    $tableBody.append($row);
                }

                var footHelper = lbl('VAS_Showing', 'Showing') + ' ' + (sIdx + 1) + '–' + eIdx + ' ' +
                    lbl('VAS_Of', 'of') + ' ' + lines.length + ' · ' + lbl('VAS_LinesOf', 'lines of') + ' ' + poNo;

                var pagerHtml = '<span class="vas-rwpo-helper">' + esc(footHelper) + '</span>';
                if (lTotalPages > 1) {
                    pagerHtml += '<span class="vas-rwpo-pager">' +
                        '<button type="button" class="vas-rwpo-pbtn vas-rwpo-l-prev"' + (lPage === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                        '<span class="vas-rwpo-ptxt">' + (lPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + lTotalPages + '</span>' +
                        '<button type="button" class="vas-rwpo-pbtn vas-rwpo-l-next"' + (lPage >= lTotalPages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>';
                }

                $tableFoot.html(pagerHtml);

                $tableFoot.find('.vas-rwpo-l-prev').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage > 0) { lPage--; drawLinesPage(); }
                });

                $tableFoot.find('.vas-rwpo-l-next').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage < lTotalPages - 1) { lPage++; drawLinesPage(); }
                });
            }

            drawLinesPage();
        }

        function openPurchaseOrderRecord(orderId) {
            if (!orderId) return;
            closeModal();

            var ZOOM_WINDOW_NAME = 'VAS_PurchaseOrder';
            var ZOOM_WINDOW_FALLBACK = 'Purchase Order';
            var ZOOM_TABLE = 'C_Order';

            var navigated = false;
            try {
                if ($self.listener && typeof $self.widgetFirevalueChanged === 'function') {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + orderId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "AD_Tab_ID": 1002398,
                        "ActionName": ZOOM_WINDOW_NAME,
                        "ActionType": "W"
                    });
                    navigated = true;
                }
            } catch (e) { }

            if (!navigated) {
                try {
                    if (window.VAS && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === 'function') {
                        VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", orderId, 0, ZOOM_WINDOW_NAME, ZOOM_WINDOW_FALLBACK);
                    } else if (window.VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(259, orderId);
                    } else if (window.VIS && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        var windowId = (VIS.context && VIS.context.getWindowId)
                            ? (VIS.context.getWindowId(ZOOM_WINDOW_NAME) || VIS.context.getWindowId(ZOOM_TABLE) || 181)
                            : 181;
                        var query = new VIS.Query();
                        query.addRestriction("C_Order_ID", VIS.Query.prototype.EQUAL, orderId);
                        VIS.viewManager.startWindow(windowId, query);
                    }
                } catch (e2) { }
            }
        }

        /* ============================================================
           WIDGET LIFECYCLE & CONTRACT METHODS
           ============================================================ */

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshWidget = function () {
            loadRepresentativeWisePO();
        };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            $(document).off('keydown.vas_rwpo_esc');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_215_RepresentativeWisePOWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

})(VAS, jQuery);
