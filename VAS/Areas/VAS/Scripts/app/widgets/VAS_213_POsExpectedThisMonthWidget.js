/**
 * VAS_213_POsExpectedThisMonthWidget
 * Purchase Order Dashboard — Widget 11: POs Expected This Month
 * Widget size: 2 columns x 2 rows (2x2 Glass Widget).
 *
 * Summary:
 *   Shows open purchase orders whose promised delivery date falls in the selected/current month,
 *   the total value of those orders, how many are due within seven days, and the three nearest
 *   promised orders. The headline block opens the full expected-PO drill-down list; each PO row
 *   opens that exact Purchase Order record and lines.
 *
 * Summary Message Table
 *  # | Fallback Text                                      | Message Key
 * ---+----------------------------------------------------+-----------------------------------
 *  1 | POs Expected This Month                            | VAS_POsExpectedThisMonth
 *  2 | Delivery due in                                    | VAS_DeliveryDueIn
 *  3 | due in the next 7 days                             | VAS_DueInNext7Days
 *  4 | Expected POs                                       | VAS_ExpectedPOs
 *  5 | Value                                              | VAS_Value
 *  6 | Due in 7 days                                      | VAS_DueIn7Days
 *  7 | Of open POs                                        | VAS_OfOpenPOs
 *  8 | pending                                            | VAS_PendingLabel
 *  9 | Expected deliveries                                | VAS_ExpectedDeliveries
 * 10 | earliest expected first                            | VAS_EarliestExpectedFirst
 * 11 | PO No                                              | VAS_PONo
 * 12 | PO date                                            | VAS_PODate
 * 13 | Vendor                                             | VAS_Vendor
 * 14 | Warehouse                                          | VAS_Warehouse
 * 15 | Representative                                     | VAS_Representative
 * 16 | Delivery                                           | VAS_Delivery
 * 17 | Status                                             | VAS_Status
 * 18 | Product                                            | VAS_Product
 * 19 | Attribute                                          | VAS_Attribute
 * 20 | UoM                                                | VAS_UOM
 * 21 | Ordered                                            | VAS_Ordered
 * 22 | Received                                           | VAS_Received
 * 23 | Pending                                            | VAS_Pending
 * 24 | Rate                                               | VAS_Rate
 * 25 | Amount                                             | VAS_Amount
 * 26 | Line status                                        | VAS_LineStatus
 * 27 | Lines                                              | VAS_Lines
 * 28 | Qty ordered                                        | VAS_QtyOrdered
 * 29 | Qty pending                                        | VAS_QtyPending
 * 30 | PO value                                           | VAS_POValue
 * 31 | Expected on                                        | VAS_ExpectedOn
 * 32 | Created by                                         | VAS_CreatedBy
 * 33 | Document status                                    | VAS_DocumentStatus
 * 34 | Delivery status                                    | VAS_DeliveryStatus
 * 35 | Purchase order lines                               | VAS_PurchaseOrderLines
 * 36 | Purchase order                                     | VAS_PurchaseOrder
 * 37 | Back                                               | VAS_Back
 * 38 | Close                                              | VAS_Close
 * 39 | Showing                                            | VAS_Showing
 * 40 | of                                                 | VAS_Of
 * 41 | No expected POs found for this month               | VAS_NoExpectedPOsFound
 * 42 | No lines found                                     | VAS_NoLinesFound
 * 43 | Loading...                                         | VAS_Loading
 * 44 | Couldn't load data                                 | VAS_CouldntLoad
 * 45 | select a PO number to open the record              | VAS_SelectPOToOpen
 * 46 | lines of                                           | VAS_LinesOf
 * 47 | Partial                                            | VAS_Partial
 * 48 | Partial received                                   | VAS_PartialReceived
 * 49 | Completed                                          | VAS_Completed
 * 50 | In process                                         | VAS_InProcess
 * 51 | Drafted                                            | VAS_Drafted
 * 52 | Open in Window                                     | VAS_OpenInWindow
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

    function lbl(key, fallback) {
        if (window.VIS && VIS.Msg && VIS.Msg.getMsg) {
            var msg = VIS.Msg.getMsg(key);
            // VIS.Msg.getMsg returns "[KEY]" when the AD_Message row is missing;
            // that must fall through to the English fallback, not render as-is.
            if (msg && msg !== key && msg !== '[' + key + ']' && msg.charAt(0) !== '['
                && msg.indexOf('**') === -1) {
                return msg;
            }
        }
        return fallback;
    }

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function parseResponse(res) {
        var data = res;
        if (typeof data === 'string' && data.length) {
            try { data = JSON.parse(data); } catch (e) { }
        }
        if (typeof data === 'string' && data.length) {
            try { data = JSON.parse(data); } catch (e2) { }
        }
        return data || {};
    }

    function formatNumber(v) {
        var n = Number(v || 0);
        if (!isFinite(n)) { n = 0; }
        return Math.round(n).toLocaleString(window.navigator.language);
    }

    function formatDecimal(v, decimals) {
        var n = Number(v || 0);
        if (!isFinite(n)) { n = 0; }
        return n.toLocaleString(window.navigator.language, {
            minimumFractionDigits: decimals != null ? decimals : 0,
            maximumFractionDigits: decimals != null ? decimals : 2
        });
    }

    function formatMoney(amount, curSymbol, curIso, precision) {
        var val = Number(amount || 0);
        if (!isFinite(val)) { val = 0; }

        var p = precision != null ? precision : 2;
        var sym = curSymbol || curIso || '₹';

        if (curIso === 'INR' || sym === '₹') {
            if (Math.abs(val) >= 1e7) {
                return sym + ' ' + (val / 1e7).toFixed(2) + ' Cr';
            }
            if (Math.abs(val) >= 1e5) {
                return sym + ' ' + (val / 1e5).toFixed(2) + ' L';
            }
        }

        var formatted = val.toLocaleString(window.navigator.language, {
            minimumFractionDigits: p,
            maximumFractionDigits: p
        });

        return sym ? (sym + ' ' + formatted) : formatted;
    }

    var MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MONTH_FULL = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

    var ICON_LINES = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';
    var ICON_BACK = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
    var ICON_CLOSE = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    var ICON_PREV = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_NEXT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
    var ICON_OPEN_EXT = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6M15 3h6v6M10 14L21 3"/></svg>';

    VAS.VAS_213_POsExpectedThisMonthWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;
        this.listener = null;

        var $self = this;
        var $wrapper = $('<div class="vas-213-container"></div>');
        var $root = $('<div class="vas-213-root"></div>');
        var $card = null;
        var $kpiCount = null;
        var $kpiMeta = null;
        var $subTitle = null;
        var $listContainer = null;
        var $busy = null;
        var widgetObserver = null;

        var now = new Date();
        var currentMonth = now.getMonth() + 1; // 1-12
        var currentYear = now.getFullYear();

        var widgetData = {
            expectedPOs: 0,
            expectedValue: 0,
            dueIn7Days: 0,
            totalOpenPendingPOs: 0,
            targetMonth: currentMonth,
            targetYear: currentYear,
            baseCurrency: { CurrencyID: 0, CurSymbol: '₹', ISO_Code: 'INR', StdPrecision: 2 },
            records: []
        };

        // Modal engine state
        var $modalHost = null;
        var modalStack = [];
        var currentModalCfg = null;
        var MT = {};
        var MT_SEQ = 0;
        // Rows per page in the modal table. The stylesheet sizes the table body to
        // exactly this many rows (--vas-213-rows), so the two must stay in step.
        var MAX_ROWS = 6;

        function showBusy(show) {
            if (!$busy) { return; }
            $busy.toggleClass('vas-213-hidden', !show);
        }

        this.Initalize = function () {
            createWidgetHtml();
            setupResizeObserver();
            loadWidgetData();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($root[0]);
            } catch (e) { }
        }

        function getPeriodLabel(m, y) {
            var mIdx = (m >= 1 && m <= 12) ? (m - 1) : (currentMonth - 1);
            var yr = y || currentYear;
            return MONTH_SHORT[mIdx] + ' ' + yr;
        }

        function createWidgetHtml() {
            var title = lbl('VAS_POsExpectedThisMonth', 'POs Expected This Month');
            var initialSub = lbl('VAS_DeliveryDueIn', 'Delivery due in') + ' ' + getPeriodLabel(currentMonth, currentYear);

            $card = $(
                '<section class="vas-213-card vas-213-glass">' +
                    '<div class="vas-213-head">' +
                        '<div class="vas-213-head-txt">' +
                            '<p class="vas-213-title">' + esc(title) + '</p>' +
                            '<p class="vas-213-sub">' + esc(initialSub) + '</p>' +
                        '</div>' +
                    '</div>' +
                    '<button type="button" class="vas-213-headline" id="vas201_headline" aria-label="' + esc(title) + '">' +
                        '<span class="vas-213-kpi-val vas-213-info">—</span>' +
                        '<span class="vas-213-kpi-meta">—</span>' +
                    '</button>' +
                    '<div class="vas-213-mini" id="vas201_mini_list"></div>' +
                '</section>'
            );

            $kpiCount = $card.find('.vas-213-kpi-val');
            $kpiMeta = $card.find('.vas-213-kpi-meta');
            $subTitle = $card.find('.vas-213-sub');
            $listContainer = $card.find('.vas-213-mini');

            // Headline click opens the full expected-PO list modal
            $card.find('.vas-213-headline').on('click', function () {
                openExpectedPOsModal();
            });

            // List row click delegates to open exact Purchase Order record
            $listContainer.on('click', '.vas-213-hrow', function () {
                var poId = parseInt($(this).attr('data-po-id'), 10);
                var poNo = $(this).attr('data-po-no');
                if (poId) {
                    openRecordModal(poId, poNo);
                }
            });

            $root.append($card);

            $busy = $(
                '<div class="vas-213-busy vas-213-hidden">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $root.append($busy);
            $wrapper.append($root);
        }

        function loadWidgetData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_213_POsExpectedThisMonthWidget/GetPOsExpectedThisMonth',
                type: 'GET',
                cache: false,
                data: {
                    year: currentYear,
                    month: currentMonth
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error || data.success === false) {
                        setError();
                        return;
                    }
                    widgetData = data;
                    renderWidget(data);
                },
                error: function () {
                    setError();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderWidget(data) {
            var expectedCount = Number(data.expectedPOs || 0);
            var expectedVal = Number(data.expectedValue || 0);
            var dueIn7 = Number(data.dueIn7Days || 0);
            var cur = data.baseCurrency || {};
            var records = data.records || [];

            var formattedValue = formatMoney(expectedVal, cur.CurSymbol, cur.ISO_Code, cur.StdPrecision);
            var subText = lbl('VAS_DeliveryDueIn', 'Delivery due in') + ' ' + getPeriodLabel(data.targetMonth, data.targetYear);

            if ($subTitle) {
                $subTitle.text(subText);
            }

            if ($kpiCount) {
                $kpiCount.text(formatNumber(expectedCount));
                $kpiCount.attr('title', formatNumber(expectedCount));
            }

            if ($kpiMeta) {
                var metaStr = formattedValue + ' · ' + dueIn7 + ' ' + lbl('VAS_DueInNext7Days', 'due in the next 7 days');
                $kpiMeta.text(metaStr);
                $kpiMeta.attr('title', metaStr);
            }

            // Render top 3 nearest promised orders
            if ($listContainer) {
                $listContainer.empty();

                if (records.length === 0) {
                    $listContainer.html(
                        '<div class="vas-213-empty-state">' +
                            esc(lbl('VAS_NoExpectedPOsFound', 'No expected POs found for this month')) +
                        '</div>'
                    );
                } else {
                    var top3 = records.slice(0, 3);
                    var html = '';
                    for (var i = 0; i < top3.length; i++) {
                        var p = top3[i];
                        var pVal = formatMoney(p.POValue, p.CurrencySymbol, p.CurrencyISO);
                        var vendorShort = p.ShortVendorName || p.VendorName || '—';
                        var expDateDisplay = p.PromisedDateShort || p.PromisedDateDisplay || p.PromisedDate || '—';

                        html +=
                            '<button type="button" class="vas-213-hrow" data-po-id="' + p.PurchaseOrderID + '" data-po-no="' + esc(p.PurchaseOrderNo) + '" title="' + esc(lbl('VAS_PurchaseOrder', 'Purchase order') + ' ' + p.PurchaseOrderNo) + '">' +
                                '<span class="vas-213-row-left">' +
                                    '<span class="vas-213-mname vas-213-c-link">' + esc(p.PurchaseOrderNo) + '</span>' +
                                    '<span class="vas-213-mmeta">' + esc(vendorShort) + ' · ' + esc(expDateDisplay) + '</span>' +
                                '</span>' +
                                '<span class="vas-213-mval">' + esc(pVal) + '</span>' +
                            '</button>';
                    }
                    $listContainer.html(html);
                }
            }
        }

        function setError() {
            if ($kpiCount) { $kpiCount.text('—'); }
            if ($kpiMeta) { $kpiMeta.text(lbl('VAS_CouldntLoad', "Couldn't load data")); }
            if ($listContainer) {
                $listContainer.html(
                    '<div class="vas-213-empty-state">' +
                        esc(lbl('VAS_CouldntLoad', "Couldn't load data")) +
                    '</div>'
                );
            }
        }

        /* ============================================================
           RECORD NAVIGATION TO PURCHASE ORDER WINDOW
           ============================================================ */
        function openPurchaseOrderRecord(orderId) {
            if (!orderId) { return; }
            // Navigating away must dismiss the popup: the record opens behind it
            // otherwise, leaving the dialog stranded over the window it just opened.
            closeModal();

            var ZOOM_TABLE = "C_Order";
            var ZOOM_WINDOW_NAME = "VAS_PurchaseOrder";
            var ZOOM_WINDOW_FALLBACK = "Purchase Order";

            var navigated = false;

            try {
                if ($self.listener && typeof $self.listener.widgetFirevalueChanged === 'function') {
                    $self.listener.widgetFirevalueChanged({
                        "Record_ID": orderId,
                        "C_Order_ID": orderId,
                        "AD_Table_ID": 259,
                        "WindowName": ZOOM_WINDOW_NAME,
                        "AD_Tab_ID": 1002398,
                        "TabWhereClause": "C_Order.C_Order_ID = " + orderId,
                        "TabLayout": "N",
                        "TabIndex": "0"
                    });
                    navigated = true;
                }
            } catch (e) { }

            if (!navigated) {
                try {
                    if (window.VAS && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === 'function') {
                        VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", orderId, 0, ZOOM_WINDOW_NAME, ZOOM_WINDOW_FALLBACK);
                    } else if (window.VIS && VIS.ZoomManager && typeof VIS.ZoomManager.zoom === 'function') {
                        VIS.ZoomManager.zoom(259, orderId);
                    } else if (window.VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(259, orderId);
                    } else if (window.VIS && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        var action = new VIS.AActionItem();
                        action.setAD_Table_ID(259);
                        action.setRecord_ID(orderId);
                        action.setWindowName(ZOOM_WINDOW_NAME);
                        action.setAD_Tab_ID(1002398);
                        VIS.viewManager.startWindow(0, action);
                    }
                } catch (e2) { }
            }
        }

        /* ============================================================
           MODAL ENGINE & DRILL-DOWN STACK
           ============================================================ */
        function ensureModalHost() {
            if ($modalHost && $modalHost[0]) { return; }

            var html =
                '<div class="vas-213-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-213-modal">' +
                        '<div class="vas-213-modal-header">' +
                            '<div class="vas-213-htxt-wrap">' +
                                '<button type="button" class="vas-213-xbtn vas-213-back-btn" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" style="display:none;">' +
                                    ICON_BACK +
                                '</button>' +
                                '<div class="vas-213-htxt">' +
                                    '<h2 class="vas-213-mtitle"></h2>' +
                                    '<div class="vas-213-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-213-hact">' +
                                '<button type="button" class="vas-213-xbtn vas-213-close-btn" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                                    ICON_CLOSE +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-213-modal-body"></div>' +
                        '<div class="vas-213-modal-foot"></div>' +
                    '</div>' +
                '</div>';

            $modalHost = $(html);
            $('body').append($modalHost);

            $modalHost.find('.vas-213-close-btn').on('click', closeModal);
            $modalHost.find('.vas-213-back-btn').on('click', popModal);

            $modalHost.on('click', function (e) {
                if (e.target === this) { closeModal(); }
                if ($(e.target).closest('[data-vas-close]').length) { closeModal(); }
            });

            $(document).on('keydown.vas201', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            $(window).on('resize.vas201', function () {
                if ($modalHost && $modalHost.hasClass('vas-213-open')) {
                    fitAllTables();
                }
            });

            // Delegate table click events
            $modalHost.find('.vas-213-modal-body').on('click', function (e) {
                var $pg = $(e.target).closest('[data-mt]');
                if ($pg.length) {
                    var mtId = $pg.attr('data-mt');
                    var dir = parseInt($pg.attr('data-dir'), 10);
                    var t = MT[mtId];
                    if (t) {
                        var pages = Math.ceil(t.rows.length / t.size);
                        t.page = Math.min(pages - 1, Math.max(0, t.page + dir));
                        drawTable(mtId);
                    }
                    return;
                }

                var $lk = $(e.target).closest('.vas-213-lnk');
                if ($lk.length) {
                    var poId = parseInt($lk.attr('data-po-id'), 10);
                    var poNo = $lk.attr('data-po-no');
                    openRecordModal(poId, poNo);
                    return;
                }

                var $ln = $(e.target).closest('[data-lines-po-id]');
                if ($ln.length) {
                    var lpoId = parseInt($ln.attr('data-lines-po-id'), 10);
                    var lpoNo = $ln.attr('data-lines-po-no');
                    openLinesModal(lpoId, lpoNo);
                    return;
                }
            });
        }

        function openModal(cfg, isBack) {
            ensureModalHost();

            if (!isBack) {
                if (cfg.child && currentModalCfg) {
                    modalStack.push(currentModalCfg);
                } else if (!cfg.child) {
                    modalStack = [];
                }
                currentModalCfg = cfg;
            } else {
                currentModalCfg = cfg;
            }

            var $backBtn = $modalHost.find('.vas-213-back-btn');
            if (modalStack.length > 0) {
                $backBtn.show();
            } else {
                $backBtn.hide();
            }

            var $modal = $modalHost.find('.vas-213-modal');
            $modal.removeClass('vas-213-modal-sm vas-213-modal-md');
            if (cfg.size === 'sm') { $modal.addClass('vas-213-modal-sm'); }
            if (cfg.size === 'md') { $modal.addClass('vas-213-modal-md'); }

            $modalHost.find('.vas-213-mtitle').text(cfg.title || '');
            $modalHost.find('.vas-213-msub').text(cfg.subtitle || '');
            $modalHost.find('.vas-213-modal-body').html(cfg.body || '');
            $modalHost.find('.vas-213-modal-foot').html(cfg.foot || '<span class="vas-213-foot-note"></span><button type="button" class="vas-213-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button>');

            $modalHost.addClass('vas-213-open');
            drawAllTables();

            requestAnimationFrame(function () {
                fitAllTables();
                requestAnimationFrame(fitAllTables);
            });

            if (cfg.after) { cfg.after(); }
        }

        function popModal() {
            var prevCfg = modalStack.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            if (prevCfg.reopen) {
                prevCfg.reopen();
            } else {
                openModal(prevCfg, true);
            }
        }

        function closeModal() {
            if ($modalHost) {
                $modalHost.removeClass('vas-213-open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        /* ============================================================
           PAGED TABLE BUILDER (DYNAMIC CLIENT HEIGHT FIT)
           ============================================================ */
        function pagedTable(cols, rows, opts) {
            opts = opts || {};
            var id = 'vas201_mt_' + (++MT_SEQ);
            var initialSize = Math.min(MAX_ROWS, opts.size || MAX_ROWS);
            MT[id] = {
                cols: cols,
                rows: rows,
                size: initialSize,
                max: initialSize,
                page: 0,
                label: opts.label || '',
                fixed: !!opts.fixed
            };
            return '<div class="vas-213-mtwrap' + (opts.fixed ? ' vas-213-fixed' : '') + '" id="' + id + '"></div>';
        }

        function cellHTML(cell, col) {
            if (cell && typeof cell === 'object') {
                if (cell.link) {
                    return '<span class="vas-213-cell"><button type="button" class="vas-213-lnk" data-po-id="' + esc(cell.id) + '" data-po-no="' + esc(cell.link) + '" title="' + esc(cell.link) + '">' + esc(cell.link) + '</button></span>';
                }
                if (cell.icon) {
                    return '<span class="vas-213-cell vas-213-center"><button type="button" class="vas-213-iconbtn" data-lines-po-id="' + esc(cell.id) + '" data-lines-po-no="' + esc(cell.icon) + '" title="' + esc(lbl('VAS_Lines', 'Lines')) + '">' + ICON_LINES + '</button></span>';
                }
                if (cell.chip) {
                    return '<span class="vas-213-cell" title="' + esc(cell.text) + '"><span class="vas-213-chip ' + esc(cell.chip) + '">' + esc(cell.text) + '</span></span>';
                }
            }
            var alignClass = (col && col.align === 'right') ? ' vas-213-right' : '';
            var textClass = (col && col.cls) ? (' ' + col.cls) : ' vas-213-c-std';
            return '<span class="vas-213-cell' + alignClass + textClass + '" title="' + esc(cell) + '">' + esc(cell) + '</span>';
        }

        function drawTable(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el) { return; }

            var tpl = t.cols.map(function (c) { return 'minmax(0,' + (c.w || 1) + 'fr)'; }).join(' ');
            var totalRows = t.rows.length;
            var pages = Math.max(1, Math.ceil(totalRows / t.size));
            if (t.page > pages - 1) { t.page = pages - 1; }

            var s = t.page * t.size;
            var slice = t.rows.slice(s, s + t.size);

            var h = '<div class="vas-213-mtbl">' +
                        '<div class="vas-213-mrow vas-213-mhead" style="grid-template-columns:' + tpl + '">';
            for (var ci = 0; ci < t.cols.length; ci++) {
                var c = t.cols[ci];
                var cAlign = c.align === 'right' ? ' vas-213-right' : '';
                h += '<span class="vas-213-cell' + cAlign + '" title="' + esc(c.label || '') + '">' + esc(c.label || '') + '</span>';
            }
            h += '</div>' +
                 '<div class="vas-213-mbody">';

            if (slice.length === 0) {
                h += '<div class="vas-213-empty-row">' + esc(lbl('VAS_NoExpectedPOsFound', 'No expected POs found for this month')) + '</div>';
            } else {
                for (var ri = 0; ri < slice.length; ri++) {
                    var r = slice[ri];
                    h += '<div class="vas-213-mrow" style="grid-template-columns:' + tpl + '">';
                    for (var cj = 0; cj < t.cols.length; cj++) {
                        h += cellHTML(r[cj], t.cols[cj]);
                    }
                    h += '</div>';
                }
            }
            h += '</div></div>';

            // Pager & helper text
            var showingFrom = totalRows > 0 ? (s + 1) : 0;
            var showingTo = totalRows > 0 ? (s + slice.length) : 0;
            var showingText = lbl('VAS_Showing', 'Showing') + ' ' + showingFrom + '–' + showingTo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalRows;
            if (t.label) { showingText += ' · ' + t.label; }

            h += '<div class="vas-213-mtfoot">' +
                    '<span class="vas-213-helper">' + esc(showingText) + '</span>';

            if (pages > 1) {
                h += '<span class="vas-213-pager">' +
                        '<button type="button" class="vas-213-pbtn" data-mt="' + id + '" data-dir="-1"' + (t.page === 0 ? ' disabled' : '') + ' aria-label="Previous">' + ICON_PREV + '</button>' +
                        '<span class="vas-213-ptxt">' + (t.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pages + '</span>' +
                        '<button type="button" class="vas-213-pbtn" data-mt="' + id + '" data-dir="1"' + (t.page >= pages - 1 ? ' disabled' : '') + ' aria-label="Next">' + ICON_NEXT + '</button>' +
                     '</span>';
            } else {
                h += '<span></span>';
            }
            h += '</div>';

            el.innerHTML = h;
        }

        function drawAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { drawTable(id); }
            });
        }

        /* Rows per page are fixed and the stylesheet sizes the table body to exactly
           that many rows, so there is nothing to measure or re-fit. The old body
           derived the count from a rendered row height, but the rows are sized by
           the stylesheet, so it was measuring its own output. */
        function fitTable(id) { return; }

        function fitAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { fitTable(id); }
            });
        }

        function mstatsHtml(items) {
            var h = '<div class="vas-213-mstats">';
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                h += '<div class="vas-213-mstat">' +
                        '<div class="vas-213-mstat-l">' + esc(it.l) + '</div>' +
                        '<div class="vas-213-mstat-v" title="' + esc(it.v) + '">' + esc(it.v) + '</div>' +
                     '</div>';
            }
            h += '</div>';
            return h;
        }

        /* ============================================================
           DRILL-DOWN 1: HEADLINE POs EXPECTED THIS MONTH MODAL
           ============================================================ */
        function openExpectedPOsModal() {
            var all = widgetData.records || [];
            var cur = widgetData.baseCurrency || {};

            var formattedValue = formatMoney(widgetData.expectedValue, cur.CurSymbol, cur.ISO_Code, cur.StdPrecision);
            var periodStr = getPeriodLabel(widgetData.targetMonth, widgetData.targetYear);

            var statStrip = mstatsHtml([
                { l: lbl('VAS_ExpectedPOs', 'Expected POs'), v: formatNumber(widgetData.expectedPOs) },
                { l: lbl('VAS_Value', 'Value'), v: formattedValue },
                { l: lbl('VAS_DueIn7Days', 'Due in 7 days'), v: formatNumber(widgetData.dueIn7Days) },
                { l: lbl('VAS_OfOpenPOs', 'Of open POs'), v: formatNumber(widgetData.totalOpenPendingPOs) + ' ' + lbl('VAS_PendingLabel', 'pending') }
            ]);

            var cols = [
                { label: '', w: 0.32 },
                { label: lbl('VAS_PONo', 'PO No'), w: 1.2, cls: 'vas-213-c-link' },
                { label: lbl('VAS_PODate', 'PO date'), w: 1.0 },
                { label: lbl('VAS_Vendor', 'Vendor'), w: 1.7 },
                { label: lbl('VAS_Warehouse', 'Warehouse'), w: 1.2 },
                { label: lbl('VAS_Representative', 'Representative'), w: 1.2 },
                { label: lbl('VAS_Value', 'Value'), w: 0.9, align: 'right', cls: 'vas-213-c-emph' },
                { label: lbl('VAS_Delivery', 'Delivery'), w: 1.05 },
                { label: lbl('VAS_Status', 'Status'), w: 1.1 }
            ];

            var rows = all.map(function (p) {
                var pValueFormatted = formatMoney(p.POValue, p.CurrencySymbol, p.CurrencyISO);
                var delivText = p.DeliveryStatus || lbl('VAS_Pending', 'Pending');
                var delivChip = (p.DeliveryStatusChip === 'chip-warn') ? 'vas-213-chip-warn' : 'vas-213-chip-neutral';
                var statusText = p.DocStatusText || p.DocumentStatus;
                var statusChip = (p.DocStatusChip === 'chip-prop') ? 'vas-213-chip-prop' : 'vas-213-chip-neutral';

                return [
                    { icon: p.PurchaseOrderNo, id: p.PurchaseOrderID },
                    { link: p.PurchaseOrderNo, id: p.PurchaseOrderID },
                    p.OrderDateDisplay || p.OrderDate,
                    p.VendorName,
                    p.WarehouseName || '—',
                    p.RepresentativeName || '—',
                    pValueFormatted,
                    { chip: delivChip, text: delivText },
                    { chip: statusChip, text: statusText }
                ];
            });

            var bodyHtml =
                statStrip +
                '<div class="vas-213-msec">' + esc(lbl('VAS_ExpectedDeliveries', 'Expected deliveries')) + '</div>' +
                pagedTable(cols, rows, { label: lbl('VAS_EarliestExpectedFirst', 'earliest expected first') });

            openModal({
                title: lbl('VAS_POsExpectedThisMonth', 'POs Expected This Month'),
                subtitle: lbl('VAS_DeliveryDueIn', 'Delivery due in') + ' ' + periodStr,
                body: bodyHtml,
                foot: '<span class="vas-213-foot-note">' + all.length + ' ' + esc(lbl('VAS_ExpectedPOs', 'Expected POs')) + ' · ' + esc(formattedValue) + '</span>' +
                      '<span><button type="button" class="vas-213-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>'
            });
        }

        /* ============================================================
           DRILL-DOWN 2: PO LINES MODAL (FROM LINES BUTTON)
           ============================================================ */
        function openLinesModal(poId, poNo) {
            var po = (widgetData.records || []).find(function (r) { return r.PurchaseOrderID === poId; });
            var vendor = po ? po.VendorName : '';
            var dateDisplay = po ? (po.OrderDateDisplay || po.OrderDate) : '';
            var docStatusDisplay = po ? po.DocStatusText : '';

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_213_POsExpectedThisMonthWidget/GetPOLines',
                type: 'GET',
                cache: false,
                data: { C_Order_ID: poId },
                success: function (res) {
                    var data = parseResponse(res);
                    var lines = data.lines || [];

                    var totalOrderedQty = Number(data.totalOrderedQty || 0);
                    var totalPendingQty = Number(data.totalPendingQty || 0);
                    var totalAmt = Number(data.totalAmount || 0);

                    var pValFormatted = formatMoney(totalAmt, data.currencySymbol, data.currencyIso, data.stdPrecision);

                    var statStrip = mstatsHtml([
                        { l: lbl('VAS_Lines', 'Lines'), v: formatNumber(lines.length) },
                        { l: lbl('VAS_POValue', 'PO value'), v: pValFormatted },
                        { l: lbl('VAS_QtyOrdered', 'Qty ordered'), v: formatNumber(totalOrderedQty) },
                        { l: lbl('VAS_QtyPending', 'Qty pending'), v: formatNumber(totalPendingQty) }
                    ]);

                    var lineCols = [
                        { label: '#', w: 0.3, align: 'right' },
                        { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-213-c-prim' },
                        { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                        { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                        { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_Pending', 'Pending'), w: 0.7, align: 'right', cls: 'vas-213-c-prim' },
                        { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-213-c-emph' },
                        { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                    ];

                    var lineRows = lines.map(function (l, idx) {
                        var rateFmt = formatMoney(l.PriceActual, data.currencySymbol, data.currencyIso, data.stdPrecision);
                        var amtFmt = formatMoney(l.LineNetAmt, data.currencySymbol, data.currencyIso, data.stdPrecision);

                        var chipClass = 'vas-213-chip-neutral';
                        var statusTxt = l.LineStatus;
                        if (l.LineStatusKey === 'VAS_LineStatusReceived') {
                            chipClass = 'vas-213-chip-ok';
                            statusTxt = lbl('VAS_Received', 'Received');
                        } else if (l.LineStatusKey === 'VAS_LineStatusPartialReceived') {
                            chipClass = 'vas-213-chip-warn';
                            statusTxt = lbl('VAS_PartialReceived', 'Partial received');
                        } else if (l.LineStatusKey === 'Drafted') {
                            chipClass = 'vas-213-chip-neutral';
                            statusTxt = lbl('VAS_Drafted', 'Drafted');
                        } else {
                            chipClass = 'vas-213-chip-neutral';
                            statusTxt = lbl('VAS_Pending', 'Pending');
                        }

                        return [
                            String(l.LineNo || (idx + 1)),
                            l.ProductName || l.ProductSKU || '—',
                            l.Attribute || '',
                            l.UOM || '—',
                            formatDecimal(l.OrderedQty),
                            nsDash(l, formatDecimal(l.DeliveredQty)),
                            nsDash(l, formatDecimal(l.PendingQty)),
                            rateFmt,
                            amtFmt,
                            nsDash(l, null) ? '–' : { chip: chipClass, text: statusTxt }
                        ];
                    });

                    var topPolink =
                        '<div class="vas-213-polink">' +
                            esc(lbl('VAS_PurchaseOrder', 'Purchase order')) + ' ' +
                            '<button type="button" class="vas-213-lnk" data-po-id="' + poId + '" data-po-no="' + esc(poNo) + '">' + esc(poNo) + '</button>' +
                            ' · ' + esc(dateDisplay || data.orderDateFormatted) + ' · ' + esc(docStatusDisplay || data.documentStatus) +
                        '</div>';

                    var bodyHtml =
                        topPolink +
                        statStrip +
                        '<div class="vas-213-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                        pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + poNo });

                    openModal({
                        child: true,
                        size: 'md',
                        title: lbl('VAS_Lines', 'Lines') + ' · ' + poNo,
                        subtitle: (vendor || data.vendorName) + ' · ' + (dateDisplay || data.orderDateFormatted) + ' · ' + (data.deliveryStatus || lbl('VAS_Pending', 'Pending')),
                        body: bodyHtml,
                        foot: '<span class="vas-213-foot-note">' + esc(poNo) + ' · ' + esc(vendor || data.vendorName) + '</span>' +
                              '<span><button type="button" class="vas-213-btn" onclick="VAS.VAS_213_POsExpectedThisMonthWidget.back()">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                              '<button type="button" class="vas-213-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>'
                    });
                },
                error: function () {
                    showBusy(false);
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        /* ============================================================
           DRILL-DOWN 3: PO RECORD DETAIL MODAL (FROM ROW OR LINK CLICK)
           ============================================================ */
        function openRecordModal(poId, poNo) {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_213_POsExpectedThisMonthWidget/GetPODetail',
                type: 'GET',
                cache: false,
                data: { C_Order_ID: poId },
                success: function (resHdr) {
                    var hdrData = parseResponse(resHdr);
                    var header = hdrData.header || {};

                    // Also fetch lines
                    $.ajax({
                        url: VIS.Application.contextUrl + 'VAS_213_POsExpectedThisMonthWidget/GetPOLines',
                        type: 'GET',
                        cache: false,
                        data: { C_Order_ID: poId },
                        success: function (resLines) {
                            var lData = parseResponse(resLines);
                            var lines = lData.lines || [];

                            var curSym = header.CurrencySymbol || lData.currencySymbol || '₹';
                            var curIso = header.CurrencyISO || lData.currencyIso || 'INR';
                            var precision = header.StdPrecision != null ? header.StdPrecision : (lData.stdPrecision || 2);
                            var valFormatted = formatMoney(header.GrandTotal || lData.totalAmount, curSym, curIso, precision);

                            var statStrip = mstatsHtml([
                                { l: lbl('VAS_Vendor', 'Vendor'), v: header.VendorName || lData.vendorName || '—' },
                                { l: lbl('VAS_PODate', 'PO date'), v: header.OrderDateDisplay || lData.orderDateFormatted || '—' },
                                { l: lbl('VAS_ExpectedOn', 'Expected on'), v: header.PromisedDateDisplay || lData.promisedDateFormatted || '—' },
                                { l: lbl('VAS_POValue', 'PO value'), v: valFormatted },
                                { l: lbl('VAS_Warehouse', 'Warehouse'), v: header.WarehouseName || lData.warehouseName || '—' },
                                { l: lbl('VAS_CreatedBy', 'Created by'), v: (header.CreatedBy || '—') + (header.CreatedOn ? (' · ' + header.CreatedOn) : '') },
                                { l: lbl('VAS_DocumentStatus', 'Document status'), v: header.DocStatusDisplay || lData.documentStatus || '—' },
                                { l: lbl('VAS_DeliveryStatus', 'Delivery status'), v: lData.deliveryStatus || lbl('VAS_Pending', 'Pending') }
                            ]);

                            var lineCols = [
                                { label: '#', w: 0.3, align: 'right' },
                                { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-213-c-prim' },
                                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                                { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                                { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Pending', 'Pending'), w: 0.7, align: 'right', cls: 'vas-213-c-prim' },
                                { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-213-c-emph' },
                                { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                            ];

                            var lineRows = lines.map(function (l, idx) {
                                var rateFmt = formatMoney(l.PriceActual, curSym, curIso, precision);
                                var amtFmt = formatMoney(l.LineNetAmt, curSym, curIso, precision);

                                var chipClass = 'vas-213-chip-neutral';
                                var statusTxt = l.LineStatus;
                                if (l.LineStatusKey === 'VAS_LineStatusReceived') {
                                    chipClass = 'vas-213-chip-ok';
                                    statusTxt = lbl('VAS_Received', 'Received');
                                } else if (l.LineStatusKey === 'VAS_LineStatusPartialReceived') {
                                    chipClass = 'vas-213-chip-warn';
                                    statusTxt = lbl('VAS_PartialReceived', 'Partial received');
                                } else {
                                    chipClass = 'vas-213-chip-neutral';
                                    statusTxt = lbl('VAS_Pending', 'Pending');
                                }

                                return [
                                    String(l.LineNo || (idx + 1)),
                                    l.ProductName || l.ProductSKU || '—',
                                    l.Attribute || '',
                                    l.UOM || '—',
                                    formatDecimal(l.OrderedQty),
                                    nsDash(l, formatDecimal(l.DeliveredQty)),
                                    nsDash(l, formatDecimal(l.PendingQty)),
                                    rateFmt,
                                    amtFmt,
                                    nsDash(l, null) ? '–' : { chip: chipClass, text: statusTxt }
                                ];
                            });

                            var bodyHtml =
                                statStrip +
                                '<div class="vas-213-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                                pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + (header.PurchaseOrderNo || poNo) });

                            var totalOrd = Number(lData.totalOrderedQty || 0);

                            openModal({
                                child: true,
                                title: header.PurchaseOrderNo || poNo,
                                subtitle: (header.VendorName || lData.vendorName || '') + ' · ' + (header.OrderDateDisplay || lData.orderDateFormatted || '') + ' · ' + (header.DocStatusDisplay || lData.documentStatus || ''),
                                body: bodyHtml,
                                foot: '<span class="vas-213-foot-note">' + lines.length + ' ' + esc(lbl('VAS_Lines', 'lines')) + ' · ' + formatNumber(totalOrd) + ' ' + esc(lbl('VAS_QtyOrdered', 'qty ordered')) + ' · ' + esc(lData.deliveryStatus || lbl('VAS_Pending', 'Pending')) + '</span>' +
                                      '<span><button type="button" class="vas-213-btn vas-213-btn-primary" id="vas201_open_window_btn">' + ICON_OPEN_EXT + ' ' + esc(lbl('VAS_OpenInWindow', 'Open in Window')) + '</button> ' +
                                      '<button type="button" class="vas-213-btn" onclick="VAS.VAS_213_POsExpectedThisMonthWidget.back()">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                                      '<button type="button" class="vas-213-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>',
                                after: function () {
                                    $('#vas201_open_window_btn').on('click', function () {
                                        openPurchaseOrderRecord(poId);
                                    });
                                }
                            });
                        },
                        error: function () {
                            showBusy(false);
                        },
                        complete: function () {
                            showBusy(false);
                        }
                    });
                },
                error: function () {
                    showBusy(false);
                }
            });
        }

        /* ============================================================
           WIDGET LIFECYCLE & CONTRACT METHODS
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
            $(document).off('keydown.vas201');
            $(window).off('resize.vas201');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $wrapper.remove();
        };
    };

    // Global back helper for modal history stack
    VAS.VAS_213_POsExpectedThisMonthWidget.back = function () {
        var host = $('.vas-213-mask');
        if (host.length) {
            host.find('.vas-213-back-btn').trigger('click');
        }
    };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_213_POsExpectedThisMonthWidget.prototype.dispose = function () {
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
