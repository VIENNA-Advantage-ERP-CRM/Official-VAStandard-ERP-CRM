/**
 * Module Name : VAS
 * Purpose     : Widget 05 - Drafted / In-Process POs (Purchase Order Dashboard)
 *               Read-only 3x1 glass KPI tile with drill-down modal work queue for
 *               Drafted ('DR') and In Progress ('IP') Purchase Orders.
 * Prefix      : VAS_195_
 *
 * Summary Message Table:
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+-----------------------------------
 *  1  | Drafted / In-Process POs             | VAS_DraftedInProcessPOs
 *  2  | Drafted                              | VAS_Drafted
 *  3  | In Progress                          | VAS_InProgress
 *  4  | Total documents                      | VAS_TotalDocuments
 *  5  | Value                                | VAS_Value
 *  6  | Documents                            | VAS_Documents
 *  7  | PO No                                | VAS_PONo
 *  8  | PO date                              | VAS_PODate
 *  9  | Vendor                               | VAS_Vendor
 * 10  | Representative                       | VAS_Representative
 * 11  | Lines                                | VAS_Lines
 * 12  | Stage                                | VAS_Stage
 * 13  | drafted ·<br/>in process             | VAS_DraftedInProcessLabel
 * 14  | Purchase order lines                 | VAS_POLines
 * 15  | Product                              | VAS_Product
 * 16  | Attribute                            | VAS_Attribute
 * 17  | UoM                                  | VAS_UOM
 * 18  | Ordered                              | VAS_Ordered
 * 19  | Received                             | VAS_Received
 * 20  | Pending                              | VAS_Pending
 * 21  | Rate                                 | VAS_Rate
 * 22  | Amount                               | VAS_Amount
 * 23  | Line status                          | VAS_LineStatus
 * 24  | Qty ordered                          | VAS_QtyOrdered
 * 25  | Qty pending                          | VAS_QtyPending
 * 26  | Showing                              | VAS_Showing
 * 27  | of                                   | VAS_Of
 * 28  | newest first                         | VAS_NewestFirst
 * 29  | No drafted or in-process POs         | VAS_NoDraftedInProcessPOs
 * 30  | Couldn't load data                   | VAS_CouldntLoad
 * 31  | Close                                | VAS_Close
 * 32  | Back                                 | VAS_Back
 * 33  | View lines                           | VAS_ViewLines
 * 34  | Open record                          | VAS_OpenRecord
 * 35  | Purchase order                       | VAS_PurchaseOrder
 * 36  | No line items found.                 | VAS_NoRecordsFound
 * 37  | lines of                             | VAS_LinesOf
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .vis-widget-body, body')[0];
        if (!container) { return; }

        var write = function () {
            var w = container.clientWidth || window.innerWidth;
            if (w > 0) {
                document.documentElement.style.setProperty('--dash-inline-size', w + 'px');
            }
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        window.addEventListener('resize', write);
        write();
    }

    VAS.VAS_195_DraftedInProcessPOsWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-195-widget-container"></div>');
        var $card = null;
        var $kpiVal = null;
        var $kpiMeta = null;
        var $kpiSplit = null;
        var $kpiSplitLabel = null;
        var $busy = null;

        var widgetObserver = null;
        var cachedData = null;
        var $activeModalOverlay = null;

        var modalStack = [];
        var modalCurrentPage = 0;
        var modalPageSize = 10;
        var linesCurrentPage = 0;
        var linesPageSize = 10;

        function lbl(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        function esc(s) {
            return String(s == null ? '' : s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function formatNumber(num) {
            return Number(num || 0).toLocaleString(window.navigator.language || 'en-IN');
        }

        function formatCurrency(val, symbol, iso) {
            var v = Number(val || 0);
            var sym = symbol || '₹';
            if (iso === 'INR' || sym === '₹') {
                if (v >= 1e7) { return sym + ' ' + (v / 1e7).toFixed(2) + ' Cr'; }
                if (v >= 1e5) { return sym + ' ' + (v / 1e5).toFixed(2) + ' L'; }
                return sym + ' ' + Math.round(v).toLocaleString('en-IN');
            }
            if (v >= 1e6) { return sym + ' ' + (v / 1e6).toFixed(2) + ' M'; }
            if (v >= 1e3) { return sym + ' ' + (v / 1e3).toFixed(2) + ' K'; }
            return sym + ' ' + v.toLocaleString(window.navigator.language || 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        function showBusy(show) {
            if ($busy && $busy[0]) {
                $busy.toggleClass('vas-195-hidden', !show);
            }
        }

        this.Initalize = function () {
            createWidgetDOM();
            setupResizeObserver();
            loadWidgetData();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $wrapper[0]) {
                            $wrapper[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($wrapper[0]);
            } catch (e) { }
        }

        function createWidgetDOM() {
            var titleText = lbl("VAS_DraftedInProcessPOs", "Drafted / In-Process POs");

            $card = $(
                '<button type="button" class="vas-195-card vas-195-border-info" aria-label="' + esc(titleText) + '">' +
                    '<svg class="vas-195-opencue" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                        '<path d="M7 17 17 7M9 7h8v8"/>' +
                    '</svg>' +
                    '<p class="vas-195-title">' + esc(titleText) + '</p>' +
                    '<div class="vas-195-kpi-row">' +
                        '<div class="vas-195-kpi-left">' +
                            '<p class="vas-195-kpi-val vas-195-val-info">—</p>' +
                            '<p class="vas-195-kpi-meta"></p>' +
                        '</div>' +
                        '<div class="vas-195-kpi-side">' +
                            '<div class="vas-195-side-v">— · —</div>' +
                            '<div class="vas-195-side-l">' +
                                esc(lbl("VAS_Drafted", "drafted")) + ' ·<br/>' +
                                esc(lbl("VAS_InProgress", "in process")) +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );

            $kpiVal = $card.find('.vas-195-kpi-val');
            $kpiMeta = $card.find('.vas-195-kpi-meta');
            $kpiSplit = $card.find('.vas-195-side-v');
            $kpiSplitLabel = $card.find('.vas-195-side-l');

            $card.on('click', function (e) {
                e.preventDefault();
                openDrilldownModal();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDrilldownModal();
                }
            });

            $wrapper.append($card);

            $busy = $('<div class="vas-195-busy vas-195-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $wrapper.append($busy);
        }

        function loadWidgetData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_195_DraftedInProcessPOsWidget/GetDraftedInProcessPOsData',
                type: 'GET',
                cache: false,
                dataType: 'json',
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    if (!data || data.error) {
                        setError();
                        return;
                    }

                    cachedData = data;
                    renderMetric(data);
                },
                error: function () {
                    setError();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderMetric(data) {
            var totalDocs = Number(data.totalDocuments || 0);
            var drafted = Number(data.draftedCount || 0);
            var inProgress = Number(data.inProgressCount || 0);
            var totalVal = Number(data.totalValue || 0);
            var sym = data.currencySymbol || '₹';
            var iso = data.currencyIso || 'INR';

            if ($kpiVal) {
                $kpiVal.text(formatNumber(totalDocs));
                $kpiVal.attr('title', formatNumber(totalDocs));
            }

            if ($kpiMeta) {
                var metaFormatted = formatCurrency(totalVal, sym, iso);
                $kpiMeta.text(metaFormatted);
                $kpiMeta.attr('title', metaFormatted);
            }

            if ($kpiSplit) {
                var splitText = formatNumber(drafted) + ' · ' + formatNumber(inProgress);
                $kpiSplit.text(splitText);
                $kpiSplit.attr('title', splitText);
            }

            if ($card) {
                $card.prop('disabled', false);
            }
        }

        function setError() {
            cachedData = null;
            if ($kpiVal) {
                $kpiVal.text('—');
                $kpiVal.removeAttr('title');
            }
            if ($kpiMeta) {
                var errText = lbl("VAS_CouldntLoad", "Couldn't load data");
                $kpiMeta.text(errText);
                $kpiMeta.attr('title', errText);
            }
            if ($kpiSplit) {
                $kpiSplit.text('— · —');
                $kpiSplit.removeAttr('title');
            }
        }

        /* ============================================================
           DRILLDOWN MODAL ENGINE
           ============================================================ */

        function openDrilldownModal() {
            if (!cachedData) {
                loadWidgetData();
                return;
            }

            if ($activeModalOverlay) {
                $activeModalOverlay.remove();
                $activeModalOverlay = null;
            }

            modalStack = [];
            modalCurrentPage = 0;

            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';
            var formattedTotal = formatCurrency(cachedData.totalValue, sym, iso);

            var $overlay = $('<div class="vas-195-mask" role="dialog" aria-modal="true"></div>');
            var $dialog = $('<div class="vas-195-modal"></div>');

            // Header
            var $header = $(
                '<div class="vas-195-modal-header">' +
                    '<div class="vas-195-head-left">' +
                        '<button type="button" class="vas-195-xbtn vas-195-mback" aria-label="' + esc(lbl("VAS_Back", "Back")) + '" style="display:none;">' +
                            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                        '</button>' +
                        '<div class="vas-195-htxt">' +
                            '<h2 class="vas-195-mtitle">' + esc(lbl("VAS_DraftedInProcessPOs", "Drafted / In-Process POs")) + '</h2>' +
                            '<div class="vas-195-msub">' + esc(formattedTotal) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-195-hact">' +
                        '<button type="button" class="vas-195-xbtn vas-195-mclose" aria-label="' + esc(lbl("VAS_Close", "Close")) + '">' +
                            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                        '</button>' +
                    '</div>' +
                '</div>'
            );

            var $body = $('<div class="vas-195-modal-body"></div>');
            var $foot = $(
                '<div class="vas-195-modal-foot">' +
                    '<span class="vas-195-foot-note"></span>' +
                    '<button type="button" class="vas-195-btn vas-195-close-btn">' + esc(lbl("VAS_Close", "Close")) + '</button>' +
                '</div>'
            );

            $dialog.append($header).append($body).append($foot);
            $overlay.append($dialog);
            $('body').append($overlay);
            $activeModalOverlay = $overlay;

            // Events
            var closeModal = function () {
                $overlay.remove();
                $activeModalOverlay = null;
                modalStack = [];
                if ($card) { $card.focus(); }
            };

            $header.find('.vas-195-mclose').on('click', closeModal);
            $foot.find('.vas-195-close-btn').on('click', closeModal);

            $overlay.on('click', function (e) {
                if ($(e.target).hasClass('vas-195-mask')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas195modal').on('keydown.vas195modal', function (e) {
                if (e.key === 'Escape' && $activeModalOverlay) {
                    closeModal();
                }
            });

            $header.find('.vas-195-mback').on('click', function () {
                if (modalStack.length > 0) {
                    var prevView = modalStack.pop();
                    if (prevView && prevView.render) {
                        prevView.render();
                    }
                }
            });

            renderMainTableView($dialog, $header, $body, $foot);

            // Dynamic Row Fitting
            fitModalTableRows($dialog, $body);
            $(window).off('resize.vas195modal').on('resize.vas195modal', function () {
                if ($activeModalOverlay) {
                    fitModalTableRows($dialog, $body);
                }
            });
        }

        function renderMainTableView($dialog, $header, $body, $foot) {
            $header.find('.vas-195-mback').hide();
            $dialog.removeClass('vas-195-modal-md');

            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';
            var formattedTotal = formatCurrency(cachedData.totalValue, sym, iso);

            $header.find('.vas-195-mtitle').text(lbl("VAS_DraftedInProcessPOs", "Drafted / In-Process POs"));
            $header.find('.vas-195-msub').text(formattedTotal);

            $body.empty();

            // Stat Strip (4 items)
            var $statStrip = $(
                '<div class="vas-195-mstats">' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_TotalDocuments", "Total documents")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(cachedData.totalDocuments) + '">' + formatNumber(cachedData.totalDocuments) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_Drafted", "Drafted")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(cachedData.draftedCount) + '">' + formatNumber(cachedData.draftedCount) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_InProgress", "In Progress")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(cachedData.inProgressCount) + '">' + formatNumber(cachedData.inProgressCount) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_Value", "Value")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + esc(formattedTotal) + '">' + esc(formattedTotal) + '</div>' +
                    '</div>' +
                '</div>'
            );
            $body.append($statStrip);

            // Section Header
            $body.append('<div class="vas-195-msec">' + esc(lbl("VAS_Documents", "Documents")) + '</div>');

            // Table Wrapper
            var $tableWrap = $('<div class="vas-195-mtbl-wrap"></div>');
            $body.append($tableWrap);

            renderMainTableRows($tableWrap, $dialog, $header, $body, $foot);
        }

        function renderMainTableRows($tableWrap, $dialog, $header, $body, $foot) {
            $tableWrap.empty();

            var records = cachedData.records || [];
            if (records.length === 0) {
                $tableWrap.html('<div class="vas-195-empty-msg">' + esc(lbl("VAS_NoDraftedInProcessPOs", "No drafted or in-process purchase orders found.")) + '</div>');
                return;
            }

            var totalCount = records.length;
            var totalPages = Math.max(1, Math.ceil(totalCount / modalPageSize));
            if (modalCurrentPage >= totalPages) { modalCurrentPage = totalPages - 1; }
            if (modalCurrentPage < 0) { modalCurrentPage = 0; }

            var startIdx = modalCurrentPage * modalPageSize;
            var endIdx = Math.min(startIdx + modalPageSize, totalCount);
            var pageRows = records.slice(startIdx, endIdx);

            var $table = $('<div class="vas-195-mtbl"></div>');

            // Table Header
            var $thead = $(
                '<div class="vas-195-mrow vas-195-mhead">' +
                    '<span class="vas-195-cell vas-195-w-icon"></span>' +
                    '<span class="vas-195-cell vas-195-w-pono" title="' + esc(lbl("VAS_PONo", "PO No")) + '">' + esc(lbl("VAS_PONo", "PO No")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-date" title="' + esc(lbl("VAS_PODate", "PO date")) + '">' + esc(lbl("VAS_PODate", "PO date")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-vendor" title="' + esc(lbl("VAS_Vendor", "Vendor")) + '">' + esc(lbl("VAS_Vendor", "Vendor")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-rep" title="' + esc(lbl("VAS_Representative", "Representative")) + '">' + esc(lbl("VAS_Representative", "Representative")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-lines vas-195-right" title="' + esc(lbl("VAS_Lines", "Lines")) + '">' + esc(lbl("VAS_Lines", "Lines")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-val vas-195-right" title="' + esc(lbl("VAS_Value", "Value")) + '">' + esc(lbl("VAS_Value", "Value")) + '</span>' +
                    '<span class="vas-195-cell vas-195-w-stage" title="' + esc(lbl("VAS_Stage", "Stage")) + '">' + esc(lbl("VAS_Stage", "Stage")) + '</span>' +
                '</div>'
            );
            $table.append($thead);

            // Body Rows
            var $mbody = $('<div class="vas-195-mbody"></div>');
            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';

            for (var i = 0; i < pageRows.length; i++) {
                var r = pageRows[i];
                var stageChipClass = r.DocStatus === 'DR' ? 'vas-195-chip-neutral' : 'vas-195-chip-prop';
                var stageText = r.DocStatus === 'DR' ? lbl("VAS_Drafted", "Drafted") : lbl("VAS_InProgress", "In Progress");
                var formattedVal = formatCurrency(r.ConvertedValue, sym, iso);

                var $row = $(
                    '<div class="vas-195-mrow vas-195-data-row">' +
                        '<span class="vas-195-cell vas-195-w-icon vas-195-center">' +
                            '<button type="button" class="vas-195-iconbtn vas-195-lines-btn" data-id="' + r.PurchaseOrderId + '" title="' + esc(lbl("VAS_ViewLines", "View lines")) + '" aria-label="' + esc(lbl("VAS_ViewLines", "View lines")) + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>' +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-195-cell vas-195-w-pono">' +
                            '<button type="button" class="vas-195-lnk vas-195-pono-btn" data-id="' + r.PurchaseOrderId + '" title="' + esc(lbl("VAS_OpenRecord", "Open record") + ' ' + r.PurchaseOrderNumber) + '">' +
                                esc(r.PurchaseOrderNumber) +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-195-cell vas-195-w-date vas-195-c-std" title="' + esc(r.OrderDateFormatted) + '">' + esc(r.OrderDateFormatted) + '</span>' +
                        '<span class="vas-195-cell vas-195-w-vendor vas-195-c-std" title="' + esc(r.VendorName) + '">' + esc(r.VendorName) + '</span>' +
                        '<span class="vas-195-cell vas-195-w-rep vas-195-c-std" title="' + esc(r.RepresentativeName) + '">' + esc(r.RepresentativeName) + '</span>' +
                        '<span class="vas-195-cell vas-195-w-lines vas-195-right vas-195-c-std" title="' + formatNumber(r.LineCount) + '">' + formatNumber(r.LineCount) + '</span>' +
                        '<span class="vas-195-cell vas-195-w-val vas-195-right vas-195-c-emph" title="' + esc(formattedVal) + '">' + esc(formattedVal) + '</span>' +
                        '<span class="vas-195-cell vas-195-w-stage" title="' + esc(stageText) + '">' +
                            '<span class="vas-195-chip ' + stageChipClass + '">' + esc(stageText) + '</span>' +
                        '</span>' +
                    '</div>'
                );

                (function (poRec) {
                    $row.find('.vas-195-pono-btn').on('click', function (e) {
                        e.stopPropagation();
                        openPurchaseOrderRecord(poRec.PurchaseOrderId);
                    });

                    $row.find('.vas-195-lines-btn').on('click', function (e) {
                        e.stopPropagation();
                        openLinesDrilldown(poRec, $dialog, $header, $body, $foot);
                    });
                })(r);

                $mbody.append($row);
            }
            $table.append($mbody);
            $tableWrap.append($table);

            // Table Footer / Pager
            var helperText = lbl("VAS_Showing", "Showing") + ' ' + (startIdx + 1) + '–' + endIdx + ' ' +
                             lbl("VAS_Of", "of") + ' ' + totalCount + ' · ' +
                             lbl("VAS_NewestFirst", "newest first");

            var $tfoot = $(
                '<div class="vas-195-mtfoot">' +
                    '<span class="vas-195-helper">' + esc(helperText) + '</span>' +
                    '<span class="vas-195-pager">' +
                        '<button type="button" class="vas-195-pbtn vas-195-prev-btn" ' + (modalCurrentPage === 0 ? 'disabled' : '') + ' aria-label="Previous">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                        '</button>' +
                        '<span class="vas-195-ptxt">' + (modalCurrentPage + 1) + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages + '</span>' +
                        '<button type="button" class="vas-195-pbtn vas-195-next-btn" ' + (modalCurrentPage >= totalPages - 1 ? 'disabled' : '') + ' aria-label="Next">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                        '</button>' +
                    '</span>' +
                '</div>'
            );

            $tfoot.find('.vas-195-prev-btn').on('click', function () {
                if (modalCurrentPage > 0) {
                    modalCurrentPage--;
                    renderMainTableRows($tableWrap, $dialog, $header, $body, $foot);
                }
            });

            $tfoot.find('.vas-195-next-btn').on('click', function () {
                if (modalCurrentPage < totalPages - 1) {
                    modalCurrentPage++;
                    renderMainTableRows($tableWrap, $dialog, $header, $body, $foot);
                }
            });

            if (totalPages <= 1) {
                $tfoot.find('.vas-195-pager').hide();
            }

            $tableWrap.append($tfoot);
        }

        /* ============================================================
           LINES DRILLDOWN MODAL
           ============================================================ */

        function openLinesDrilldown(poRec, $dialog, $header, $body, $foot) {
            modalStack.push({
                render: function () {
                    renderMainTableView($dialog, $header, $body, $foot);
                }
            });

            $header.find('.vas-195-mback').show();
            $dialog.addClass('vas-195-modal-md');

            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';
            var formattedVal = formatCurrency(poRec.ConvertedValue, sym, iso);

            $header.find('.vas-195-mtitle').text(lbl("VAS_Lines", "Lines") + ' · ' + poRec.PurchaseOrderNumber);
            $header.find('.vas-195-msub').text(poRec.VendorName + ' · ' + poRec.OrderDateFormatted + ' · ' + poRec.DocStatusName);

            $body.empty();
            $body.html('<div class="vas-195-empty-msg"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_195_DraftedInProcessPOsWidget/GetOrderLines',
                type: 'GET',
                data: { C_Order_ID: poRec.PurchaseOrderId },
                dataType: 'json',
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    if (!data || data.error) {
                        $body.html('<div class="vas-195-empty-msg">' + esc(lbl("VAS_CouldntLoad", "Couldn't load data")) + '</div>');
                        return;
                    }

                    renderLinesContent(poRec, data.lines || [], $dialog, $header, $body, $foot);
                },
                error: function () {
                    $body.html('<div class="vas-195-empty-msg">' + esc(lbl("VAS_CouldntLoad", "Couldn't load data")) + '</div>');
                }
            });
        }

        function renderLinesContent(poRec, lines, $dialog, $header, $body, $foot) {
            $body.empty();

            var totalOrderedQty = 0;
            var totalPendingQty = 0;
            for (var i = 0; i < lines.length; i++) {
                totalOrderedQty += Number(lines[i].QtyOrdered || 0);
                totalPendingQty += Number(lines[i].QtyPending || 0);
            }

            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';
            var formattedVal = formatCurrency(poRec.ConvertedValue, sym, iso);

            // Top Link Row
            var $poLinkRow = $(
                '<div class="vas-195-polink">' +
                    '<span>' + esc(lbl("VAS_PurchaseOrder", "Purchase order")) + ' </span>' +
                    '<button type="button" class="vas-195-lnk vas-195-pono-btn" title="' + esc(lbl("VAS_OpenRecord", "Open record")) + '">' +
                        esc(poRec.PurchaseOrderNumber) +
                    '</button>' +
                    '<span> · ' + esc(poRec.OrderDateFormatted) + ' · ' + esc(poRec.DocStatusName) + '</span>' +
                '</div>'
            );

            $poLinkRow.find('.vas-195-pono-btn').on('click', function () {
                openPurchaseOrderRecord(poRec.PurchaseOrderId);
            });
            $body.append($poLinkRow);

            // Stat Strip
            var $statStrip = $(
                '<div class="vas-195-mstats">' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_Lines", "Lines")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(lines.length) + '">' + formatNumber(lines.length) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_Value", "PO value")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + esc(formattedVal) + '">' + esc(formattedVal) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_QtyOrdered", "Qty ordered")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(totalOrderedQty) + '">' + formatNumber(totalOrderedQty) + '</div>' +
                    '</div>' +
                    '<div class="vas-195-mstat">' +
                        '<div class="vas-195-stat-l">' + esc(lbl("VAS_QtyPending", "Qty pending")) + '</div>' +
                        '<div class="vas-195-stat-v" title="' + formatNumber(totalPendingQty) + '">' + formatNumber(totalPendingQty) + '</div>' +
                    '</div>' +
                '</div>'
            );
            $body.append($statStrip);

            // Section
            $body.append('<div class="vas-195-msec">' + esc(lbl("VAS_POLines", "Purchase order lines")) + '</div>');

            // Table
            var $tableWrap = $('<div class="vas-195-mtbl-wrap"></div>');
            $body.append($tableWrap);

            linesCurrentPage = 0;
            renderLinesTableRows(poRec, lines, $tableWrap);
        }

        function renderLinesTableRows(poRec, lines, $tableWrap) {
            $tableWrap.empty();

            if (!lines || lines.length === 0) {
                $tableWrap.html('<div class="vas-195-empty-msg">' + esc(lbl("VAS_NoRecordsFound", "No line items found.")) + '</div>');
                return;
            }

            var totalCount = lines.length;
            var totalPages = Math.max(1, Math.ceil(totalCount / linesPageSize));
            if (linesCurrentPage >= totalPages) { linesCurrentPage = totalPages - 1; }
            if (linesCurrentPage < 0) { linesCurrentPage = 0; }

            var startIdx = linesCurrentPage * linesPageSize;
            var endIdx = Math.min(startIdx + linesPageSize, totalCount);
            var pageRows = lines.slice(startIdx, endIdx);

            var $table = $('<div class="vas-195-mtbl vas-195-lines-grid"></div>');

            // Table Header
            var $thead = $(
                '<div class="vas-195-mrow vas-195-mhead vas-195-lines-head">' +
                    '<span class="vas-195-cell vas-195-lh-num vas-195-right" title="#">#</span>' +
                    '<span class="vas-195-cell vas-195-lh-prod" title="' + esc(lbl("VAS_Product", "Product")) + '">' + esc(lbl("VAS_Product", "Product")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-attr" title="' + esc(lbl("VAS_Attribute", "Attribute")) + '">' + esc(lbl("VAS_Attribute", "Attribute")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-uom" title="' + esc(lbl("VAS_UOM", "UoM")) + '">' + esc(lbl("VAS_UOM", "UoM")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-ord vas-195-right" title="' + esc(lbl("VAS_Ordered", "Ordered")) + '">' + esc(lbl("VAS_Ordered", "Ordered")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-rec vas-195-right" title="' + esc(lbl("VAS_Received", "Received")) + '">' + esc(lbl("VAS_Received", "Received")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-pend vas-195-right" title="' + esc(lbl("VAS_Pending", "Pending")) + '">' + esc(lbl("VAS_Pending", "Pending")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-rate vas-195-right" title="' + esc(lbl("VAS_Rate", "Rate")) + '">' + esc(lbl("VAS_Rate", "Rate")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-amt vas-195-right" title="' + esc(lbl("VAS_Amount", "Amount")) + '">' + esc(lbl("VAS_Amount", "Amount")) + '</span>' +
                    '<span class="vas-195-cell vas-195-lh-stat" title="' + esc(lbl("VAS_LineStatus", "Line status")) + '">' + esc(lbl("VAS_LineStatus", "Line status")) + '</span>' +
                '</div>'
            );
            $table.append($thead);

            var $mbody = $('<div class="vas-195-mbody vas-195-lines-body"></div>');
            var sym = cachedData.currencySymbol || '₹';
            var iso = cachedData.currencyIso || 'INR';

            for (var i = 0; i < pageRows.length; i++) {
                var l = pageRows[i];
                var chipClass = 'vas-195-chip-neutral';
                if (l.LineStatus === 'Received') { chipClass = 'vas-195-chip-ok'; }
                else if (l.LineStatus === 'Partial received') { chipClass = 'vas-195-chip-warn'; }

                var formattedRate = sym + ' ' + formatNumber(l.PriceActual);
                var formattedAmount = formatCurrency(l.LineNetAmt, sym, iso);

                var $row = $(
                    '<div class="vas-195-mrow vas-195-lines-row">' +
                        '<span class="vas-195-cell vas-195-lh-num vas-195-right vas-195-c-std" title="' + (startIdx + i + 1) + '">' + (startIdx + i + 1) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-prod vas-195-c-prim" title="' + esc(l.ProductName) + '">' + esc(l.ProductName) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-attr vas-195-c-std" title="' + esc(l.AttributeDesc || '—') + '">' + esc(l.AttributeDesc || '—') + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-uom vas-195-c-std" title="' + esc(l.UOM) + '">' + esc(l.UOM) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-ord vas-195-right vas-195-c-std" title="' + formatNumber(l.QtyOrdered) + '">' + formatNumber(l.QtyOrdered) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-rec vas-195-right vas-195-c-std" title="' + formatNumber(l.QtyDelivered) + '">' + formatNumber(l.QtyDelivered) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-pend vas-195-right vas-195-c-prim" title="' + formatNumber(l.QtyPending) + '">' + formatNumber(l.QtyPending) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-rate vas-195-right vas-195-c-std" title="' + esc(formattedRate) + '">' + esc(formattedRate) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-amt vas-195-right vas-195-c-emph" title="' + esc(formattedAmount) + '">' + esc(formattedAmount) + '</span>' +
                        '<span class="vas-195-cell vas-195-lh-stat" title="' + esc(l.LineStatus) + '">' +
                            '<span class="vas-195-chip ' + chipClass + '">' + esc(l.LineStatus) + '</span>' +
                        '</span>' +
                    '</div>'
                );
                $mbody.append($row);
            }
            $table.append($mbody);
            $tableWrap.append($table);

            // Footer
            var helperText = lbl("VAS_Showing", "Showing") + ' ' + (startIdx + 1) + '–' + endIdx + ' ' +
                             lbl("VAS_Of", "of") + ' ' + totalCount + ' ' +
                             lbl("VAS_LinesOf", "lines of") + ' ' + poRec.PurchaseOrderNumber;

            var $tfoot = $(
                '<div class="vas-195-mtfoot">' +
                    '<span class="vas-195-helper">' + esc(helperText) + '</span>' +
                    '<span class="vas-195-pager">' +
                        '<button type="button" class="vas-195-pbtn vas-195-prev-btn" ' + (linesCurrentPage === 0 ? 'disabled' : '') + ' aria-label="Previous">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                        '</button>' +
                        '<span class="vas-195-ptxt">' + (linesCurrentPage + 1) + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages + '</span>' +
                        '<button type="button" class="vas-195-pbtn vas-195-next-btn" ' + (linesCurrentPage >= totalPages - 1 ? 'disabled' : '') + ' aria-label="Next">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                        '</button>' +
                    '</span>' +
                '</div>'
            );

            $tfoot.find('.vas-195-prev-btn').on('click', function () {
                if (linesCurrentPage > 0) {
                    linesCurrentPage--;
                    renderLinesTableRows(poRec, lines, $tableWrap);
                }
            });

            $tfoot.find('.vas-195-next-btn').on('click', function () {
                if (linesCurrentPage < totalPages - 1) {
                    linesCurrentPage++;
                    renderLinesTableRows(poRec, lines, $tableWrap);
                }
            });

            if (totalPages <= 1) {
                $tfoot.find('.vas-195-pager').hide();
            }

            $tableWrap.append($tfoot);
        }

        /* ============================================================
           RECORD NAVIGATION (C_Order_ID -> VAS_PurchaseOrder)
           ============================================================ */

        function openPurchaseOrderRecord(orderId) {
            if (!orderId) { return; }

            try {
                // Table 259 is C_Order
                var query = new VIS.Query();
                query.addRestriction("C_Order_ID", VIS.Query.prototype.EQUAL, orderId);

                // Check window resolution from context or default Purchase Order window
                var windowId = 0;
                if (VIS.context && VIS.context.getWindowId) {
                    windowId = VIS.context.getWindowId("VAS_PurchaseOrder") || VIS.context.getWindowId("C_Order") || 181;
                } else {
                    windowId = 181;
                }

                if (VIS.viewManager) {
                    VIS.viewManager.startWindow(windowId, query);
                } else if (AEnv && AEnv.zoom) {
                    AEnv.zoom(259, orderId);
                }
            } catch (ex) {
                console.error("VAS_195: Error navigating to Purchase Order record:", ex);
            }
        }

        /* ============================================================
           DYNAMIC TABLE ROW FITTER
           ============================================================ */

        function fitModalTableRows($dialog, $body) {
            if (!$body || !$body[0]) { return; }
            var $mtwrap = $body.find('.vas-195-mtbl-wrap');
            if (!$mtwrap[0]) { return; }

            var availHeight = $mtwrap.innerHeight() || ($body.innerHeight() - 140);
            if (availHeight < 60) { return; }

            var $head = $mtwrap.find('.vas-195-mhead');
            var $foot = $mtwrap.find('.vas-195-mtfoot');
            var $row = $mtwrap.find('.vas-195-data-row, .vas-195-lines-row').first();

            var headH = $head[0] ? $head.outerHeight() : 34;
            var footH = $foot[0] ? $foot.outerHeight() : 40;
            var rowH = $row[0] ? $row.outerHeight() : 36;

            var calculated = Math.floor((availHeight - headH - footH) / rowH);
            var clamped = Math.max(2, Math.min(10, calculated));

            if ($mtwrap.find('.vas-195-lines-grid')[0]) {
                if (clamped !== linesPageSize && clamped >= 2) {
                    linesPageSize = clamped;
                }
            } else {
                if (clamped !== modalPageSize && clamped >= 2) {
                    modalPageSize = clamped;
                }
            }
        }

        /* ============================================================
           PUBLIC WIDGET INTERFACES
           ============================================================ */

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshWidget = function () {
            loadWidgetData();
        };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            if ($activeModalOverlay) {
                $activeModalOverlay.remove();
                $activeModalOverlay = null;
            }
            $(window).off('resize.vas195modal');
            $(document).off('keydown.vas195modal');
            if ($card) {
                $card.off('click keydown');
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_195_DraftedInProcessPOsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_195_DraftedInProcessPOsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_195_DraftedInProcessPOsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_195_DraftedInProcessPOsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
