/**
 * VAS_205_POCompletedMTDWidget
 * 2x1 KPI Widget & Drill-down modal for Purchase Orders dashboard.
 * Displays count of purchase orders reaching the Completed state (DocStatus CO) Month-to-Date.
 *
 * =========================================================================
 * Summary Message Table
 *  # | Fallback Text                                    | Message Key
 * ---+--------------------------------------------------+-----------------------------------
 *  1 | Purchase Orders Completed MTD                   | VAS_205_POCompletedMTD
 *  2 | received in full                                 | VAS_205_ReceivedInFull
 *  3 | closed short                                     | VAS_205_ClosedShort
 *  4 | Completed documents                              | VAS_205_CompletedDocs
 *  5 | Completed                                        | VAS_205_Completed
 *  6 | Closed                                           | VAS_205_Closed
 *  7 | Received value                                   | VAS_205_ReceivedValue
 *  8 | On-time closure                                  | VAS_205_OnTimeClosure
 *  9 | Closed documents carry no delivery — the balance quantity is written off at closure. | VAS_205_ClosedDocNote
 * 10 | Documents                                        | VAS_205_Documents
 * 11 | Lines                                            | VAS_205_Lines
 * 12 | Purchase order lines                             | VAS_205_POLines
 * 13 | Showing                                          | VAS_205_Showing
 * 14 | of                                               | VAS_205_Of
 * 15 | select a PO number to open the record            | VAS_205_SelectToOpen
 * 16 | Close                                            | VAS_205_Close
 * 17 | Back                                             | VAS_205_Back
 * 18 | Open in Window                                   | VAS_205_OpenInWindow
 * 19 | PO No                                            | VAS_205_PONo
 * 20 | PO date                                          | VAS_205_PODate
 * 21 | Vendor                                           | VAS_205_Vendor
 * 22 | Warehouse                                        | VAS_205_Warehouse
 * 23 | Representative                                   | VAS_205_Representative
 * 24 | Value                                            | VAS_205_Value
 * 25 | Delivery                                         | VAS_205_Delivery
 * 26 | Status                                           | VAS_205_Status
 * 27 | Product                                          | VAS_205_Product
 * 28 | Attribute                                        | VAS_205_Attribute
 * 29 | UoM                                              | VAS_205_UOM
 * 30 | Ordered                                          | VAS_205_Ordered
 * 31 | Received                                         | VAS_205_Received
 * 32 | Pending                                          | VAS_205_Pending
 * 33 | Rate                                             | VAS_205_Rate
 * 34 | Amount                                           | VAS_205_Amount
 * 35 | Line status                                      | VAS_205_LineStatus
 * 36 | Couldn't load                                    | VAS_205_CouldntLoad
 * 37 | No records found                                 | VAS_205_NoRecordsFound
 * 38 | Expected on                                      | VAS_205_ExpectedOn
 * 39 | PO value                                         | VAS_205_POValue
 * 40 | Qty ordered                                      | VAS_205_QtyOrdered
 * 41 | Qty pending                                      | VAS_205_QtyPending
 * 42 | Document status                                  | VAS_205_DocumentStatus
 * 43 | Delivery status                                  | VAS_205_DeliveryStatus
 * 44 | lines of                                         | VAS_205_LinesOf
 * 45 | Purchase order                                   | VAS_205_PurchaseOrder
 * 46 | Previous                                         | VAS_205_Previous
 * 47 | Next                                             | VAS_205_Next
 * =========================================================================
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

    VAS.VAS_205_POCompletedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        // Single widget root, matching VAS_206 / VAS_207: this element is the container
        // query context, the type-scale anchor and the ResizeObserver target all at once.
        // It used to be a container div wrapping a second "root" div, with the anchor on
        // the card and the observed width written to the inner div - so the measurement
        // and the element that consumed it were never the same node.
        var $wrapper = $('<div class="vas-w205-container">');
        var $card = null;
        var $valueEl = null;
        var $metaEl = null;
        var $busy = null;

        var widgetObserver = null;
        var cachedData = null;
        var poRegistry = {};

        // Modal State Stack
        var CFGSTACK = [];
        var CURCFG = null;
        var $activeModal = null;

        // 8 rows per page - the popup tables are paged and never scroll
        var PAGE_SIZE = 8;
        var currentTablePage = 0;
        var currentLinesPage = 0;

        // VAS163 2026-11-12: unused English month-name array removed (dates go through Intl).

        var ZOOM_WINDOW_NAME = 'VAS_PurchaseOrder';
        var ZOOM_WINDOW_FALLBACK = 'Purchase Order';
        var ZOOM_TABLE = 'C_Order';
        var poZoomWindowId = 0;

        function lbl(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-w205-hidden', !show);
        }

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatMoney(value, symbol, iso, precision) {
            // VAS163 2026-11-12: server ISO + precision through the shared helper; the browser culture
            // places the symbol and picks the compact form (was symbol + ' ' + number).
            var v = Number(value || 0);
            return VAS.formatAmountByBrowser(v, iso || '', precision, { compact: Math.abs(v) >= 100000, symbol: symbol || '' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatIsoDate(iso) {
            var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ''));
            if (!m) { return ''; }
            return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
                .toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2). ISO / precision paired with the symbol in use.
        function curIsoOf(src) {
            return (src && src.currencySymbol) ? (src.currencyIso || '') : ((cachedData && cachedData.currencyIso) || '');
        }
        function curPrecOf(src) {
            var p = (src && src.currencySymbol) ? src.stdPrecision : (cachedData && cachedData.stdPrecision);
            return (typeof p === 'number') ? p : undefined;
        }

        function setupWidgetSizeObserver() {
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

        this.Initalize = function () {
            createWidget();
            setupWidgetSizeObserver();
            loadKpi();
        };

        function createWidget() {
            var title = lbl("VAS_205_POCompletedMTD", "Purchase Orders Completed MTD");

            // Card scaffold shared with VAS_206 / VAS_207: head > head-txt > title, then a
            // body row holding the left value/meta block. The 3-wide siblings add a
            // right-hand kpi-side block to the same row; this 2-wide tile has no secondary
            // stat to show, so the left block takes the whole row.
            // Every class is scoped to this widget - the previous markup leaned on the
            // unscoped mock class names (w / c2 / r1 / kpi / border-ok / clickable /
            // w-title / kpi-val / kpi-meta) plus the shared .vas-widget-bg, whose white
            // 2px border and 0.875em radius fought the card's own success border.
            $card = $(
                '<button type="button" class="vas-w205-card vas-w205-border-ok" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-w205-head">' +
                        '<div class="vas-w205-head-txt">' +
                            '<p class="vas-w205-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</p>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-w205-body-row">' +
                        '<div class="vas-w205-left-block">' +
                            '<p class="vas-w205-kpi-val vas-w205-ok">—</p>' +
                            '<p class="vas-w205-kpi-meta"></p>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-w205-kpi-val');
            $metaEl = $card.find('.vas-w205-kpi-meta');

            $card.on('click', function (e) {
                e.preventDefault();
                openCompletedModal();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openCompletedModal();
                }
            });

            $wrapper.append($card);

            $busy = $('<div class="vas-w205-busy vas-w205-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $wrapper.append($busy);
        }

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_205_POCompletedMTDWidget/GetPOCompletedMTDData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    if (data && data.error) {
                        setError();
                        return;
                    }

                    cachedData = data || {};
                    poRegistry = {};
                    if (cachedData.records && Array.isArray(cachedData.records)) {
                        for (var i = 0; i < cachedData.records.length; i++) {
                            var r = cachedData.records[i];
                            poRegistry[r.PurchaseOrderId] = r;
                            poRegistry[r.PurchaseOrderNumber] = r;
                        }
                    }

                    renderMetric(cachedData);
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
            var total = Number(data.totalCount || 0);
            var receivedVal = Number(data.receivedValue || 0);
            var curSym = data.currencySymbol || data.currencyIso || ''; // VAS163 2026-11-10: browser locale (6-Nov retest2).

            if ($valueEl) {
                $valueEl.text(formatCount(total));
                $valueEl.attr('title', formatCount(total));
            }

            if ($metaEl) {
                var recText = formatMoney(receivedVal, curSym, data.currencyIso, curPrecOf(data)) + ' ' + lbl("VAS_205_ReceivedInFull", "received in full");
                var metaString = recText;
                $metaEl.text(metaString);
                $metaEl.attr('title', metaString);
            }

            if ($card) {
                $card.prop('disabled', false);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl("VAS_205_CouldntLoad", "Couldn't load");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
            if ($card) {
                $card.prop('disabled', false);
            }
        }

        /* =========================================================================
           MODAL ENGINE & DRILL-DOWNS
           ========================================================================= */

        // Canonical Home/Landing-page zoom handling (2026-09-30, per user-supplied reference
        // pattern): $self.windowNo >= 0 means this widget is hosted inside an actual window tab,
        // so the host's own tab-panel router relays the value change; otherwise (Home dashboard
        // placement, windowNo < 0) VAS.ZoomUtil.zoomToRecord resolves and opens the window
        // directly, caching the resolved window id so repeat clicks skip re-resolving it by name.
        function zoomToPurchaseOrder(cOrderId) {
            if (!cOrderId) { return; }
            closeModal();
            if ($self.windowNo >= 0) {
                var windowParam = {
                    "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + cOrderId,
                    "TabLayout": "Y",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
            } else {
                // VAS163 2026-11-12: pass the old window name too so the util can fall back.
                VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", cOrderId, poZoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_FALLBACK)
                    .done(function (id) {
                        if (id > 0) { poZoomWindowId = id; }
                    });
            }
        }

        function openModal(cfg, isBack) {
            if (!isBack) {
                if (cfg.child && CURCFG) {
                    CFGSTACK.push(CURCFG);
                } else if (!cfg.child) {
                    CFGSTACK = [];
                }
                CURCFG = cfg;
            } else {
                CURCFG = cfg;
            }

            if (!$activeModal) {
                createModalShell();
            }

            var $mask = $activeModal;
            var $modal = $mask.find('.vas-w205-modal');
            var $mBack = $mask.find('#vas205-mBack');
            var $mTitle = $mask.find('#vas205-mTitle');
            var $mSub = $mask.find('#vas205-mSub');
            var $mBody = $mask.find('#vas205-mBody');
            var $mFoot = $mask.find('#vas205-mFoot');

            $mBack.toggle(CFGSTACK.length > 0);
            $modal.removeClass('sm md').addClass(cfg.size ? cfg.size : '');
            $mBody.attr('class', 'vas-w205-modal-body modal-body' + (cfg.bodyClass ? ' ' + cfg.bodyClass : ''));

            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
            $mBody.html(cfg.body || '');
            // No footer Close button: the header X closes the popup.
            $mFoot.html(cfg.foot || '<span class="vas-w205-foot-note foot-note"></span>');

            $mask.addClass('vas-w205-mask-open open');

            if (cfg.after) {
                cfg.after($mask);
            }
        }

        function backModal() {
            var prevCfg = CFGSTACK.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            openModal(prevCfg, true);
        }

        function closeModal() {
            if ($activeModal) {
                $activeModal.removeClass('vas-w205-mask-open open');
                $activeModal.remove();
                $activeModal = null;
            }
            CFGSTACK = [];
            CURCFG = null;
        }

        function createModalShell() {
            var shellHtml =
                '<div class="vas-w205-mask mask" id="vas205-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-w205-modal" id="vas205-modal">' +
                        '<div class="vas-w205-modal-header">' +
                            '<div style="display:flex;align-items:center;gap:8px;min-width:0">' +
                                '<button type="button" class="vas-w205-xbtn xbtn" id="vas205-mBack" aria-label="' + escapeHtml(lbl("VAS_205_Back", "Back")) + '" style="display:none;">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                        '<path d="M19 12H5M12 19l-7-7 7-7"/>' +
                                    '</svg>' +
                                '</button>' +
                                '<div class="vas-w205-htxt htxt">' +
                                    '<h2 id="vas205-mTitle">Title</h2>' +
                                    '<div class="vas-w205-msub msub" id="vas205-mSub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-w205-hact haact">' +
                                '<button type="button" class="vas-w205-xbtn xbtn" id="vas205-mClose" aria-label="' + escapeHtml(lbl("VAS_205_Close", "Close")) + '">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
                                        '<path d="M18 6 6 18M6 6l12 12"/>' +
                                    '</svg>' +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-w205-modal-body" id="vas205-mBody"></div>' +
                        '<div class="vas-w205-modal-foot modal-foot" id="vas205-mFoot"></div>' +
                    '</div>' +
                '</div>';

            $activeModal = $(shellHtml);
            $('body').append($activeModal);

            $activeModal.find('#vas205-mBack').on('click', function () { backModal(); });
            $activeModal.find('#vas205-mClose').on('click', function () { closeModal(); });
            $activeModal.on('click', function (e) {
                if ($(e.target).closest('[data-close]').length > 0) {
                    closeModal();
                }
            });
        }

        /* 1. Main Drill-down Modal (PO Completed MTD) */
        function openCompletedModal() {
            var data = cachedData || {};
            var records = data.records || [];
            currentTablePage = 0;

            // VAS163 2026-11-10: browser locale (6-Nov retest2). MTD range formatted with the browser locale.
            var now = new Date();
            var monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
            var rangeOpts = { day: '2-digit', month: 'short', year: 'numeric' };
            var rangeStr;
            try {
                var rangeFmt = new Intl.DateTimeFormat(window.navigator.language, rangeOpts);
                rangeStr = (typeof rangeFmt.formatRange === 'function')
                    ? rangeFmt.formatRange(monthStart, now)
                    : rangeFmt.format(monthStart) + ' – ' + rangeFmt.format(now);
            } catch (e) {
                rangeStr = monthStart.toLocaleDateString(window.navigator.language, rangeOpts) + ' – ' + now.toLocaleDateString(window.navigator.language, rangeOpts);
            }

            var title = lbl("VAS_205_POCompletedMTD", "Purchase Orders Completed MTD");
            var subtitle = lbl("VAS_205_CompletedDocs", "Completed documents") + ' · ' + rangeStr;

            var completedVal = formatCount(data.completedCount || 0);
            var receivedFormatted = formatMoney(data.receivedValue || 0, data.currencySymbol || data.currencyIso || '', data.currencyIso, curPrecOf(data));
            // VAS163 2026-11-12: percent as a browser-locale number.
            var onTimeStr = (data.onTimePercent != null) ? (Number(data.onTimePercent).toLocaleString(window.navigator.language, { maximumFractionDigits: 1 }) + '%') : '—';

            var statStripHtml =
                '<div class="vas-w205-mstats mstats">' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_Completed", "Completed")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(completedVal) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_ReceivedValue", "Received value")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(receivedFormatted) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_OnTimeClosure", "On-time closure")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(onTimeStr) + '</div>' +
                    '</div>' +
                '</div>';

            var secHtml = '<div class="vas-w205-msec msec">' + escapeHtml(lbl("VAS_205_Documents", "Documents")) + '</div>';

            var bodyHtml =
                statStripHtml +
                secHtml +
                '<div class="vas-w205-tbl-wrap" id="vas205-doc-tbl-wrap"></div>';

            var footHtml =
                '<span class="vas-w205-foot-note foot-note" id="vas205-doc-foot-note"></span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w205-pager" id="vas205-doc-pager"></span>' +
                '</span>';

            openModal({
                child: false,
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    renderMainDocTable($m, records, data.currencySymbol);
                }
            });
        }

        function renderMainDocTable($m, records, curSym) {
            var sym = curSym || ((cachedData && cachedData.currencyIso) || ''); // VAS163 2026-11-10: browser locale (6-Nov retest2).
            var iso = (cachedData && cachedData.currencyIso) || '';
            var prec = curPrecOf(cachedData);
            var totalRows = records.length;
            var totalPages = Math.max(1, Math.ceil(totalRows / PAGE_SIZE));
            currentTablePage = Math.min(currentTablePage, totalPages - 1);
            if (currentTablePage < 0) { currentTablePage = 0; }

            var s = currentTablePage * PAGE_SIZE;
            var slice = records.slice(s, s + PAGE_SIZE);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var p = slice[i];
                var valFormatted = formatMoney(p.PoValueConverted || p.PoValueDocumentCurrency, sym, iso, prec);

                rowsHtml +=
                    '<div class="vas-w205-trow vas-w205-doc-grid trow">' +
                        '<span class="vas-w205-cell cell center">' +
                            '<button type="button" class="vas-w205-iconbtn iconbtn" data-lines="' + p.PurchaseOrderId + '" title="' + escapeHtml(lbl("VAS_205_LinesOf", "lines of") + ' ' + p.PurchaseOrderNumber) + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
                                    '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>' +
                                '</svg>' +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-w205-cell cell">' +
                            '<button type="button" class="vas-w205-lnk lnk" data-poid="' + p.PurchaseOrderId + '" title="' + escapeHtml(p.PurchaseOrderNumber) + '">' +
                                escapeHtml(p.PurchaseOrderNumber) +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(formatIsoDate(p.OrderDate)) + '">' + escapeHtml(formatIsoDate(p.OrderDate)) + '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(p.VendorName) + '">' + escapeHtml(p.VendorName) + '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(p.WarehouseName) + '">' + escapeHtml(p.WarehouseName) + '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(p.RepresentativeName) + '">' + escapeHtml(p.RepresentativeName) + '</span>' +
                        '<span class="vas-w205-cell cell right c-emph" title="' + escapeHtml(valFormatted) + '">' + escapeHtml(valFormatted) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(p.DeliveryText) + '">' +
                            '<span class="vas-w205-chip chip ' + p.DeliveryChip + '">' + escapeHtml(p.DeliveryText) + '</span>' +
                        '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(p.DocStatusText) + '">' +
                            '<span class="vas-w205-chip chip ' + p.DocStatusChip + '">' + escapeHtml(p.DocStatusText) + '</span>' +
                        '</span>' +
                    '</div>';
            }

            if (slice.length === 0) {
                rowsHtml = '<div style="padding: 1.5em; text-align: center; color: #5F7283; font-size: 0.8125em;">' +
                    escapeHtml(lbl("VAS_205_NoRecordsFound", "No records found")) + '</div>';
            }

            var tableHtml =
                '<div class="vas-w205-tbl mtbl">' +
                    '<div class="vas-w205-trow vas-w205-doc-grid mhead thead">' +
                        '<span class="vas-w205-cell cell center"></span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_PONo", "PO No")) + '">' + escapeHtml(lbl("VAS_205_PONo", "PO No")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_PODate", "PO date")) + '">' + escapeHtml(lbl("VAS_205_PODate", "PO date")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Vendor", "Vendor")) + '">' + escapeHtml(lbl("VAS_205_Vendor", "Vendor")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Warehouse", "Warehouse")) + '">' + escapeHtml(lbl("VAS_205_Warehouse", "Warehouse")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Representative", "Representative")) + '">' + escapeHtml(lbl("VAS_205_Representative", "Representative")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Value", "Value")) + '">' + escapeHtml(lbl("VAS_205_Value", "Value")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Delivery", "Delivery")) + '">' + escapeHtml(lbl("VAS_205_Delivery", "Delivery")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Status", "Status")) + '">' + escapeHtml(lbl("VAS_205_Status", "Status")) + '</span>' +
                    '</div>' +
                    '<div class="vas-w205-tbody tbody mbody">' +
                        rowsHtml +
                    '</div>' +
                '</div>';

            $m.find('#vas205-doc-tbl-wrap').html(tableHtml);

            // Foot note & pager
            var noteTxt = totalRows === 0 ? '' :
                lbl("VAS_205_Showing", "Showing") + ' ' + (s + 1) + '–' + (s + slice.length) + ' ' + lbl("VAS_205_Of", "of") + ' ' + totalRows + ' · ' + lbl("VAS_205_SelectToOpen", "select a PO number to open the record");
            $m.find('#vas205-doc-foot-note').text(noteTxt);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-w205-pbtn pbtn" id="vas205-pg-prev"' + (currentTablePage === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_205_Previous", "Previous")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-w205-ptxt ptxt">' + (currentTablePage + 1) + ' ' + lbl("VAS_205_Of", "of") + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-w205-pbtn pbtn" id="vas205-pg-next"' + (currentTablePage >= totalPages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_205_Next", "Next")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }
            $m.find('#vas205-doc-pager').html(pagerHtml);

            // Wire events
            $m.find('#vas205-pg-prev').on('click', function () {
                if (currentTablePage > 0) {
                    currentTablePage--;
                    renderMainDocTable($m, records, sym);
                }
            });
            $m.find('#vas205-pg-next').on('click', function () {
                if (currentTablePage < totalPages - 1) {
                    currentTablePage++;
                    renderMainDocTable($m, records, sym);
                }
            });
            $m.find('.vas-w205-lnk[data-poid]').on('click', function () {
                var poid = Number($(this).attr('data-poid'));
                openRecordModal(poid);
            });
            $m.find('.vas-w205-iconbtn[data-lines]').on('click', function () {
                var poid = Number($(this).attr('data-lines'));
                openLinesModal(poid);
            });
        }

        /* 2. PO Record View Modal */
        function openRecordModal(cOrderId) {
            var po = poRegistry[cOrderId];
            if (!po) { return; }

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_205_POCompletedMTDWidget/GetPOLines',
                type: 'GET',
                data: { C_Order_ID: cOrderId },
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    renderRecordModalView(po, data || {});
                },
                error: function () {
                    renderRecordModalView(po, { lines: [] });
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderRecordModalView(po, lineData) {
            var lines = lineData.lines || [];
            currentLinesPage = 0;
            // VAS163 2026-11-10: browser locale (6-Nov retest2).
            var curIso = curIsoOf(lineData);
            var curPrec = curPrecOf(lineData);
            var curSym = lineData.currencySymbol || cachedData.currencySymbol || curIso || '';

            var title = po.PurchaseOrderNumber;
            var subtitle = po.VendorName + ' · ' + formatIsoDate(po.OrderDate) + ' · ' + po.DocStatusText;

            var statsHtml =
                '<div class="vas-w205-mstats mstats" style="grid-template-columns: repeat(4, minmax(0, 1fr));">' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_Vendor", "Vendor")) + '</div>' +
                        '<div class="vas-w205-mstat-v v" title="' + escapeHtml(po.VendorName) + '">' + escapeHtml(po.VendorName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_PODate", "PO date")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(formatIsoDate(po.OrderDate)) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_ExpectedOn", "Expected on")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(formatIsoDate(po.PromisedDate) || '—') + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_POValue", "PO value")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(formatMoney(po.PoValueConverted || po.PoValueDocumentCurrency, curSym, curIso, curPrec)) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_Warehouse", "Warehouse")) + '</div>' +
                        '<div class="vas-w205-mstat-v v" title="' + escapeHtml(po.WarehouseName) + '">' + escapeHtml(po.WarehouseName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_Representative", "Representative")) + '</div>' +
                        '<div class="vas-w205-mstat-v v" title="' + escapeHtml(po.RepresentativeName) + '">' + escapeHtml(po.RepresentativeName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_DocumentStatus", "Document status")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(po.DocStatusText) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_DeliveryStatus", "Delivery status")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(po.DeliveryText) + '</div>' +
                    '</div>' +
                '</div>';

            var secHtml = '<div class="vas-w205-msec msec">' + escapeHtml(lbl("VAS_205_POLines", "Purchase order lines")) + '</div>';

            var bodyHtml =
                statsHtml +
                secHtml +
                '<div class="vas-w205-tbl-wrap" id="vas205-lines-tbl-wrap"></div>';

            var totalOrderedQty = Number(lineData.totalOrderedQty || po.OrderedQty || 0);

            var footHtml =
                '<span class="vas-w205-foot-note foot-note" id="vas205-lines-foot-note">' +
                    lines.length + ' ' + lbl("VAS_205_Lines", "lines") + ' · ' +
                    formatCount(totalOrderedQty) + ' ' + lbl("VAS_205_Ordered", "ordered") + ' · ' +
                    po.DeliveryText +
                '</span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w205-pager" id="vas205-lines-pager"></span>' +
                    '<button type="button" class="vas-w205-btn vas-w205-btn-primary btn-primary" id="vas205-btn-zoom">' +
                        escapeHtml(lbl("VAS_205_OpenInWindow", "Open in Window")) +
                    '</button>' +
                    '<button type="button" class="vas-w205-btn" id="vas205-btn-back">' + escapeHtml(lbl("VAS_205_Back", "Back")) + '</button>' +
                '</span>';

            openModal({
                child: true,
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    $m.find('#vas205-btn-back').on('click', function () { backModal(); });
                    $m.find('#vas205-btn-zoom').on('click', function () { zoomToPurchaseOrder(po.PurchaseOrderId); });
                    renderLinesTable($m, lines, curSym, curIso, curPrec);
                }
            });
        }

        /* 3. Lines Quick View Modal */
        function openLinesModal(cOrderId) {
            var po = poRegistry[cOrderId];
            if (!po) { return; }

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_205_POCompletedMTDWidget/GetPOLines',
                type: 'GET',
                data: { C_Order_ID: cOrderId },
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    renderLinesModalView(po, data || {});
                },
                error: function () {
                    renderLinesModalView(po, { lines: [] });
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderLinesModalView(po, lineData) {
            var lines = lineData.lines || [];
            currentLinesPage = 0;
            // VAS163 2026-11-10: browser locale (6-Nov retest2).
            var curIso = curIsoOf(lineData);
            var curPrec = curPrecOf(lineData);
            var curSym = lineData.currencySymbol || cachedData.currencySymbol || curIso || '';

            var title = lbl("VAS_205_Lines", "Lines") + ' · ' + po.PurchaseOrderNumber;
            var subtitle = po.VendorName + ' · ' + formatIsoDate(po.OrderDate) + ' · ' + po.DeliveryText;

            var poLinkHtml =
                '<div class="vas-w205-polink polink" style="font-size: 0.75em; color: #5F7283; margin-bottom: 0.25em;">' +
                    escapeHtml(lbl("VAS_205_PurchaseOrder", "Purchase order")) + ' <button type="button" class="vas-w205-lnk lnk" id="vas205-lnk-po-rec" style="display:inline;padding:0 4px;">' +
                        escapeHtml(po.PurchaseOrderNumber) +
                    '</button>' +
                    ' · ' + escapeHtml(formatIsoDate(po.OrderDate)) + ' · ' + escapeHtml(po.DocStatusText) +
                '</div>';

            var totalOrderedQty = Number(lineData.totalOrderedQty || po.OrderedQty || 0);
            var totalPendingQty = Number(lineData.totalPendingQty || po.PendingQty || 0);
            var poValFormatted = formatMoney(po.PoValueConverted || po.PoValueDocumentCurrency, curSym, curIso, curPrec);

            var statsHtml =
                '<div class="vas-w205-mstats mstats" style="grid-template-columns: repeat(4, minmax(0, 1fr));">' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_Lines", "Lines")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + lines.length + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_POValue", "PO value")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(poValFormatted) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_QtyOrdered", "Qty ordered")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(formatCount(totalOrderedQty)) + '</div>' +
                    '</div>' +
                    '<div class="vas-w205-mstat mstat">' +
                        '<div class="vas-w205-mstat-l l">' + escapeHtml(lbl("VAS_205_QtyPending", "Qty pending")) + '</div>' +
                        '<div class="vas-w205-mstat-v v">' + escapeHtml(formatCount(totalPendingQty)) + '</div>' +
                    '</div>' +
                '</div>';

            var secHtml = '<div class="vas-w205-msec msec">' + escapeHtml(lbl("VAS_205_POLines", "Purchase order lines")) + '</div>';

            var bodyHtml =
                poLinkHtml +
                statsHtml +
                secHtml +
                '<div class="vas-w205-tbl-wrap" id="vas205-lines-tbl-wrap"></div>';

            var footHtml =
                '<span class="vas-w205-foot-note foot-note">' + escapeHtml(po.PurchaseOrderNumber + ' · ' + po.VendorName) + '</span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w205-pager" id="vas205-lines-pager"></span>' +
                    '<button type="button" class="vas-w205-btn" id="vas205-btn-back">' + escapeHtml(lbl("VAS_205_Back", "Back")) + '</button>' +
                '</span>';

            openModal({
                child: true,
                size: 'md',
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    $m.find('#vas205-btn-back').on('click', function () { backModal(); });
                    $m.find('#vas205-lnk-po-rec').on('click', function () { openRecordModal(po.PurchaseOrderId); });
                    renderLinesTable($m, lines, curSym, curIso, curPrec);
                }
            });
        }

        function renderLinesTable($m, lines, curSym, curIso, curPrec) {
            var sym = curSym || curIso || ''; // VAS163 2026-11-10: browser locale (6-Nov retest2).
            var totalRows = lines.length;
            var totalPages = Math.max(1, Math.ceil(totalRows / PAGE_SIZE));
            currentLinesPage = Math.min(currentLinesPage, totalPages - 1);
            if (currentLinesPage < 0) { currentLinesPage = 0; }

            var s = currentLinesPage * PAGE_SIZE;
            var slice = lines.slice(s, s + PAGE_SIZE);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var l = slice[i];
                var rateFormatted = formatMoney(l.PriceActual, sym, curIso, curPrec);
                var amtFormatted = formatMoney(l.LineNetAmt, sym, curIso, curPrec);

                rowsHtml +=
                    '<div class="vas-w205-trow vas-w205-line-grid trow">' +
                        '<span class="vas-w205-cell cell right c-std" title="' + l.LineNo + '">' + (s + i + 1) + '</span>' +
                        '<span class="vas-w205-cell cell c-prim" title="' + escapeHtml(l.ProductName) + '">' + escapeHtml(l.ProductName) + '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(l.Attribute) + '">' + escapeHtml(l.Attribute) + '</span>' +
                        '<span class="vas-w205-cell cell c-std" title="' + escapeHtml(l.UOM) + '">' + escapeHtml(l.UOM) + '</span>' +
                        '<span class="vas-w205-cell cell right c-std" title="' + formatCount(l.QtyOrdered) + '">' + formatCount(l.QtyOrdered) + '</span>' +
                        '<span class="vas-w205-cell cell right c-std" title="' + nsDash(l, formatCount(l.QtyDelivered)) + '">' + nsDash(l, formatCount(l.QtyDelivered)) + '</span>' +
                        '<span class="vas-w205-cell cell right c-prim" title="' + nsDash(l, formatCount(l.QtyPending)) + '">' + nsDash(l, formatCount(l.QtyPending)) + '</span>' +
                        '<span class="vas-w205-cell cell right c-std" title="' + escapeHtml(rateFormatted) + '">' + escapeHtml(rateFormatted) + '</span>' +
                        '<span class="vas-w205-cell cell right c-emph" title="' + escapeHtml(amtFormatted) + '">' + escapeHtml(amtFormatted) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(nsDash(l, l.LineStatus)) + '">' +
                            '<span class="vas-w205-chip chip ' + l.LineStatusChip + '">' + escapeHtml(nsDash(l, l.LineStatus)) + '</span>' +
                        '</span>' +
                    '</div>';
            }

            if (slice.length === 0) {
                rowsHtml = '<div style="padding: 1.5em; text-align: center; color: #5F7283; font-size: 0.8125em;">' +
                    escapeHtml(lbl("VAS_205_NoRecordsFound", "No records found")) + '</div>';
            }

            var tableHtml =
                '<div class="vas-w205-tbl mtbl">' +
                    '<div class="vas-w205-trow vas-w205-line-grid mhead thead">' +
                        '<span class="vas-w205-cell cell right" title="#">#</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Product", "Product")) + '">' + escapeHtml(lbl("VAS_205_Product", "Product")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_Attribute", "Attribute")) + '">' + escapeHtml(lbl("VAS_205_Attribute", "Attribute")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_UOM", "UoM")) + '">' + escapeHtml(lbl("VAS_205_UOM", "UoM")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Ordered", "Ordered")) + '">' + escapeHtml(lbl("VAS_205_Ordered", "Ordered")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Received", "Received")) + '">' + escapeHtml(lbl("VAS_205_Received", "Received")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Pending", "Pending")) + '">' + escapeHtml(lbl("VAS_205_Pending", "Pending")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Rate", "Rate")) + '">' + escapeHtml(lbl("VAS_205_Rate", "Rate")) + '</span>' +
                        '<span class="vas-w205-cell cell right" title="' + escapeHtml(lbl("VAS_205_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_205_Amount", "Amount")) + '</span>' +
                        '<span class="vas-w205-cell cell" title="' + escapeHtml(lbl("VAS_205_LineStatus", "Line status")) + '">' + escapeHtml(lbl("VAS_205_LineStatus", "Line status")) + '</span>' +
                    '</div>' +
                    '<div class="vas-w205-tbody tbody mbody">' +
                        rowsHtml +
                    '</div>' +
                '</div>';

            $m.find('#vas205-lines-tbl-wrap').html(tableHtml);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-w205-pbtn pbtn" id="vas205-lines-pg-prev"' + (currentLinesPage === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_205_Previous", "Previous")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-w205-ptxt ptxt">' + (currentLinesPage + 1) + ' ' + lbl("VAS_205_Of", "of") + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-w205-pbtn pbtn" id="vas205-lines-pg-next"' + (currentLinesPage >= totalPages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_205_Next", "Next")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }
            $m.find('#vas205-lines-pager').html(pagerHtml);

            $m.find('#vas205-lines-pg-prev').on('click', function () {
                if (currentLinesPage > 0) {
                    currentLinesPage--;
                    renderLinesTable($m, lines, sym, curIso, curPrec);
                }
            });
            $m.find('#vas205-lines-pg-next').on('click', function () {
                if (currentLinesPage < totalPages - 1) {
                    currentLinesPage++;
                    renderLinesTable($m, lines, sym, curIso, curPrec);
                }
            });
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () {
            return $wrapper;
        };

        this.disposeComponent = function () {
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            if ($card) {
                $card.off();
            }
            closeModal();
            $wrapper.remove();
        };
    };

    VAS.VAS_205_POCompletedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_205_POCompletedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_205_POCompletedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_205_POCompletedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_205_POCompletedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_205_POCompletedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };


    /* A charge line, or a product that is not of Item type, is never received:
       received, pending and line status render as a dash instead of a figure. */
    function nsDash(l, v) {
        return (l && (l.IsNonStock || l.isNonStock)) ? '–' : v;
    }

})(VAS, jQuery);
/* VAS163 2026-11-12: VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
   Amount in the BROWSER culture (window.navigator.language) with the currency the SERVER sent
   (ISO code + precision; the server symbol only as a fallback). Defined in each widget / panel
   that uses it - guarded, so the first file loaded defines it - so the shared util/CurrencyFormat.js
   stays as shipped. Compact amounts keep that file's default scale (VIS.Util.formatCompactAmount:
   L / Cr for Indian-numbering currencies, K / M / B otherwise, digits in the browser culture);
   separators, decimals and the position of the currency sign come from the browser culture. */
; (function () {
    var V = window.VAS = window.VAS || {};
    if (typeof V.formatAmountByBrowser === 'function') return;
    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }
    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }
    /* Currency sign and where the browser culture puts it for this ISO code. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }
    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        var minus = (num < 0 && /[1-9]/.test(body)) ? '-' : '';
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        if (!lay.sign) return minus + sign + ' ' + body;
        return lay.before ? (minus + sign + lay.gap + body) : (minus + body + lay.gap + sign);
    };
})();
