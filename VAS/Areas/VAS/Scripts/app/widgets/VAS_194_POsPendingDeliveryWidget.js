/**
 * VAS_194_POsPendingDeliveryWidget
 * Purchase Order Dashboard — Widget 04: POs Pending Delivery
 * Widget size: 3 columns x 1 row (3x1 Warning KPI card).
 * Operational PO chase list: Completed Purchase Orders (DocStatus = 'CO')
 * with remaining undelivered quantity (QtyOrdered > QtyDelivered).
 *
 * Summary Message Table
 *  # | Current Text                                       | Message Key
 * ---+----------------------------------------------------+-----------------------------------
 *  1 | POs Pending Delivery                               | VAS_POsPendingDelivery
 *  2 | Open till date                                     | VAS_OpenTillDate
 *  3 | items pending                                      | VAS_ItemsPendingLabel
 *  4 | delivery                                           | VAS_DeliveryLabel
 *  5 | undelivered                                        | VAS_Undelivered
 *  6 | past due                                           | VAS_PastDueLabel
 *  7 | All POs till date that are not fully delivered     | VAS_PendingDeliverySubtitle
 *  8 | Open POs                                           | VAS_OpenPOs
 *  9 | Items pending                                      | VAS_ItemsPending
 * 10 | Undelivered value                                  | VAS_UndeliveredValue
 * 11 | Past due                                           | VAS_PastDue
 * 12 | Purchase orders awaiting delivery                  | VAS_POsAwaitingDelivery
 * 13 | earliest expected first                            | VAS_EarliestExpectedFirst
 * 14 | PO No                                              | VAS_PONumber
 * 15 | PO date                                            | VAS_PODate
 * 16 | Vendor                                             | VAS_Vendor
 * 17 | Warehouse                                          | VAS_Warehouse
 * 18 | Ordered                                            | VAS_Ordered
 * 19 | Pending items                                      | VAS_PendingItems
 * 20 | Pending value                                      | VAS_PendingValue
 * 21 | Expected                                           | VAS_Expected
 * 22 | Delivery                                           | VAS_Delivery
 * 23 | Status                                             | VAS_Status
 * 24 | Pending                                            | VAS_Pending
 * 25 | Partial                                            | VAS_Partial
 * 26 | Received                                           | VAS_Received
 * 27 | Partial received                                   | VAS_PartialReceived
 * 28 | Completed                                          | VAS_Completed
 * 29 | Lines                                              | VAS_Lines
 * 30 | Product                                            | VAS_Product
 * 31 | Attribute                                          | VAS_Attribute
 * 32 | UoM                                                | VAS_UOM
 * 33 | Rate                                               | VAS_Rate
 * 34 | Amount                                             | VAS_Amount
 * 35 | Line status                                        | VAS_LineStatus
 * 36 | Purchase order lines                               | VAS_PurchaseOrderLines
 * 37 | Purchase order                                     | VAS_PurchaseOrder
 * 38 | Expected on                                        | VAS_ExpectedOn
 * 39 | PO value                                           | VAS_POValue
 * 40 | Created by                                         | VAS_CreatedBy
 * 41 | Document status                                    | VAS_DocumentStatus
 * 42 | Delivery status                                    | VAS_DeliveryStatus
 * 43 | Qty ordered                                        | VAS_QtyOrdered
 * 44 | Qty pending                                        | VAS_QtyPending
 * 45 | Back                                               | VAS_Back
 * 46 | Close                                              | VAS_Close
 * 47 | Open Record                                        | VAS_OpenRecord
 * 48 | Showing                                            | VAS_Showing
 * 49 | of                                                 | VAS_Of
 * 50 | No pending delivery POs found                      | VAS_NoPendingDeliveryPOs
 * 51 | Loading...                                         | VAS_Loading
 * 52 | Couldn't load data                                 | VAS_CouldntLoad
 * 53 | lines of                                           | VAS_LinesOf
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

    function parseResponse(res) {
        var data = res;
        if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
        if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
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
        var sym = curSymbol || curIso || '';

        // Format compact if very large
        if (Math.abs(val) >= 10000000) {
            return sym + ' ' + (val / 10000000).toFixed(2) + ' Cr';
        }
        if (Math.abs(val) >= 100000) {
            return sym + ' ' + (val / 100000).toFixed(2) + ' L';
        }

        var formatted = val.toLocaleString(window.navigator.language, {
            minimumFractionDigits: p,
            maximumFractionDigits: p
        });

        return sym ? (sym + ' ' + formatted) : formatted;
    }

    var ICON_OPEN_CUE = '<svg class="vas-194-opencue" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M9 7h8v8"/></svg>';
    var ICON_LINES = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';
    var ICON_BACK = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
    var ICON_CLOSE = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    var ICON_PREV = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_NEXT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';

    VAS.VAS_194_POsPendingDeliveryWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-194-root">');
        var $card = null;
        var $kpiVal = null;
        var $kpiMeta = null;
        var $kpiItems = null;
        var $busy = null;

        var kpiData = {
            openPOs: 0,
            itemsPending: 0,
            undeliveredValue: 0,
            pastDue: 0,
            baseCurrency: { CurrencyID: 0, CurSymbol: '', ISO_Code: '', StdPrecision: 2 },
            records: []
        };

        // Modal state management
        var $modalHost = null;
        var modalStack = [];
        var currentModalCfg = null;
        var MT = {};
        var MT_SEQ = 0;
        var MAX_ROWS = 10;

        function showBusy(show) {
            if (!$busy) { return; }
            $busy.toggleClass('vas-194-hidden', !show);
        }

        this.Initalize = function () {
            createWidgetHtml();
            setupResizeObserver();
            loadKpiData();
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

        function createWidgetHtml() {
            var title = lbl('VAS_POsPendingDelivery', 'POs Pending Delivery');
            var itemsPendingLbl = lbl('VAS_ItemsPendingLabel', 'items pending');
            var deliveryLbl = lbl('VAS_DeliveryLabel', 'delivery');

            $card = $(
                '<button type="button" class="vas-194-card vas-194-border-warn" aria-label="' + esc(title) + '">' +
                    ICON_OPEN_CUE +
                    '<div class="vas-194-head">' +
                        '<div class="vas-194-head-txt">' +
                            '<p class="vas-194-title">' + esc(title) + '</p>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-194-body-row">' +
                        '<div class="vas-194-left-block">' +
                            '<p class="vas-194-kpi-val vas-194-warn">—</p>' +
                            '<p class="vas-194-kpi-meta">' + esc(lbl('VAS_OpenTillDate', 'Open till date')) + '</p>' +
                        '</div>' +
                        '<div class="vas-194-kpi-side">' +
                            '<div class="vas-194-side-v">—</div>' +
                            '<div class="vas-194-side-l">' + esc(itemsPendingLbl) + '<br/>' + esc(deliveryLbl) + '</div>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );

            $kpiVal = $card.find('.vas-194-kpi-val');
            $kpiMeta = $card.find('.vas-194-kpi-meta');
            $kpiItems = $card.find('.vas-194-side-v');

            $card.on('click', function () {
                openPendingDeliveryModal();
            });

            $root.append($card);

            $busy = $(
                '<div class="vas-194-busy vas-194-hidden">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $root.append($busy);
        }

        function loadKpiData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_194_POsPendingDeliveryWidget/GetPOsPendingDelivery',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error || data.success === false) {
                        setError();
                        return;
                    }
                    kpiData = data;
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
            var openPOs = Number(data.openPOs || 0);
            var itemsPending = Number(data.itemsPending || 0);
            var undeliveredVal = Number(data.undeliveredValue || 0);
            var pastDue = Number(data.pastDue || 0);
            var cur = data.baseCurrency || {};

            var formattedUndelivered = formatMoney(undeliveredVal, cur.CurSymbol, cur.ISO_Code, cur.StdPrecision);

            if ($kpiVal) {
                $kpiVal.text(formatNumber(openPOs));
                $kpiVal.attr('title', formatNumber(openPOs));
            }
            if ($kpiItems) {
                $kpiItems.text(formatNumber(itemsPending));
                $kpiItems.attr('title', formatNumber(itemsPending));
            }
            if ($kpiMeta) {
                var metaText = lbl('VAS_OpenTillDate', 'Open till date') + ' · ' +
                               formattedUndelivered + ' ' + lbl('VAS_Undelivered', 'undelivered') + ' · ' +
                               pastDue + ' ' + lbl('VAS_PastDueLabel', 'past due');
                $kpiMeta.text(metaText);
                $kpiMeta.attr('title', metaText);
            }
            if ($card) {
                $card.prop('disabled', false);
            }
        }

        function setError() {
            if ($kpiVal) { $kpiVal.text('—'); $kpiVal.removeAttr('title'); }
            if ($kpiItems) { $kpiItems.text('—'); $kpiItems.removeAttr('title'); }
            if ($kpiMeta) { $kpiMeta.text(lbl('VAS_CouldntLoad', "Couldn't load data")); }
            if ($card) { $card.prop('disabled', false); }
        }

        /* ============================================================
           RECORD NAVIGATION TO PURCHASE ORDER WINDOW
           ============================================================ */
        function openPurchaseOrderRecord(orderId) {
            if (!orderId) { return; }

            // 1. Fire value changed if tab panel / dashboard router is listening
            try {
                if ($self.widgetFirevalueChanged) {
                    var windowParam = {
                        "Record_ID": orderId,
                        "C_Order_ID": orderId,
                        "AD_Table_ID": 259,
                        "WindowName": "VAS_PurchaseOrder",
                        "AD_Tab_ID": 1002398,
                        "TabWhereClause": "C_Order.C_Order_ID = " + orderId,
                        "TabLayout": "N",
                        "TabIndex": "0"
                    };
                    $self.widgetFirevalueChanged(windowParam);
                }
            } catch (e) { }

            // 2. Standard VIS Zoom / Window Open
            try {
                if (VIS && VIS.ZoomManager && VIS.ZoomManager.zoom) {
                    VIS.ZoomManager.zoom(259, orderId);
                    return;
                }
            } catch (e) { }

            try {
                if (VIS && VIS.viewManager && VIS.viewManager.startWindow) {
                    var action = new VIS.AActionItem();
                    action.setAD_Table_ID(259);
                    action.setRecord_ID(orderId);
                    action.setWindowName("VAS_PurchaseOrder");
                    action.setAD_Tab_ID(1002398);
                    VIS.viewManager.startWindow(0, action);
                }
            } catch (e) { }
        }

        /* ============================================================
           MODAL ENGINE & DRILL-DOWN STACK
           ============================================================ */
        function ensureModalHost() {
            if ($modalHost && $modalHost[0]) { return; }

            var html =
                '<div class="vas-194-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-194-modal">' +
                        '<div class="vas-194-modal-header">' +
                            '<div class="vas-194-htxt-wrap">' +
                                '<button type="button" class="vas-194-xbtn vas-194-back-btn" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" style="display:none;">' +
                                    ICON_BACK +
                                '</button>' +
                                '<div class="vas-194-htxt">' +
                                    '<h2 class="vas-194-mtitle"></h2>' +
                                    '<div class="vas-194-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-194-hact">' +
                                '<button type="button" class="vas-194-xbtn vas-194-close-btn" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                                    ICON_CLOSE +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-194-modal-body"></div>' +
                        '<div class="vas-194-modal-foot"></div>' +
                    '</div>' +
                '</div>';

            $modalHost = $(html);
            $('body').append($modalHost);

            $modalHost.find('.vas-194-close-btn').on('click', closeModal);
            $modalHost.find('.vas-194-back-btn').on('click', popModal);

            $modalHost.on('click', function (e) {
                if (e.target === this) { closeModal(); }
                if ($(e.target).closest('[data-vas-close]').length) { closeModal(); }
            });

            $(document).on('keydown.vas194', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            $(window).on('resize.vas194', function () {
                if ($modalHost && $modalHost.hasClass('vas-194-open')) {
                    fitAllTables();
                }
            });

            // Delegate table events
            $modalHost.find('.vas-194-modal-body').on('click', function (e) {
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

                var $lk = $(e.target).closest('.vas-194-lnk');
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

            var $backBtn = $modalHost.find('.vas-194-back-btn');
            if (modalStack.length > 0) {
                $backBtn.show();
            } else {
                $backBtn.hide();
            }

            var $modal = $modalHost.find('.vas-194-modal');
            $modal.removeClass('vas-194-modal-sm vas-194-modal-md');
            if (cfg.size === 'sm') { $modal.addClass('vas-194-modal-sm'); }
            if (cfg.size === 'md') { $modal.addClass('vas-194-modal-md'); }

            $modalHost.find('.vas-194-mtitle').text(cfg.title || '');
            $modalHost.find('.vas-194-msub').text(cfg.subtitle || '');
            $modalHost.find('.vas-194-modal-body').html(cfg.body || '');
            $modalHost.find('.vas-194-modal-foot').html(cfg.foot || '<span class="vas-194-foot-note"></span><button type="button" class="vas-194-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button>');

            $modalHost.addClass('vas-194-open');
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
                $modalHost.removeClass('vas-194-open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        /* ============================================================
           PAGED TABLE BUILDER (DYNAMIC CLIENT HEIGHT FIT)
           ============================================================ */
        function pagedTable(cols, rows, opts) {
            opts = opts || {};
            var id = 'vas194_mt_' + (++MT_SEQ);
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
            return '<div class="vas-194-mtwrap' + (opts.fixed ? ' vas-194-fixed' : '') + '" id="' + id + '"></div>';
        }

        function cellHTML(cell, col) {
            if (cell && typeof cell === 'object') {
                if (cell.link) {
                    return '<span class="vas-194-cell"><button type="button" class="vas-194-lnk" data-po-id="' + esc(cell.id) + '" data-po-no="' + esc(cell.link) + '" title="' + esc(cell.link) + '">' + esc(cell.link) + '</button></span>';
                }
                if (cell.icon) {
                    return '<span class="vas-194-cell vas-194-center"><button type="button" class="vas-194-iconbtn" data-lines-po-id="' + esc(cell.id) + '" data-lines-po-no="' + esc(cell.icon) + '" title="' + esc(lbl('VAS_Lines', 'Lines')) + '">' + ICON_LINES + '</button></span>';
                }
                if (cell.chip) {
                    return '<span class="vas-194-cell" title="' + esc(cell.text) + '"><span class="vas-194-chip ' + esc(cell.chip) + '">' + esc(cell.text) + '</span></span>';
                }
            }
            var alignClass = (col && col.align === 'right') ? ' vas-194-right' : '';
            var textClass = (col && col.cls) ? (' ' + col.cls) : ' vas-194-c-std';
            return '<span class="vas-194-cell' + alignClass + textClass + '" title="' + esc(cell) + '">' + esc(cell) + '</span>';
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

            var h = '<div class="vas-194-mtbl">' +
                        '<div class="vas-194-mrow vas-194-mhead" style="grid-template-columns:' + tpl + '">';
            for (var ci = 0; ci < t.cols.length; ci++) {
                var c = t.cols[ci];
                var cAlign = c.align === 'right' ? ' vas-194-right' : '';
                h += '<span class="vas-194-cell' + cAlign + '" title="' + esc(c.label || '') + '">' + esc(c.label || '') + '</span>';
            }
            h += '</div>' +
                 '<div class="vas-194-mbody">';

            if (slice.length === 0) {
                h += '<div class="vas-194-empty-row">' + esc(lbl('VAS_NoPendingDeliveryPOs', 'No pending delivery POs found')) + '</div>';
            } else {
                for (var ri = 0; ri < slice.length; ri++) {
                    var r = slice[ri];
                    h += '<div class="vas-194-mrow" style="grid-template-columns:' + tpl + '">';
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

            h += '<div class="vas-194-mtfoot">' +
                    '<span class="vas-194-helper">' + esc(showingText) + '</span>';

            if (pages > 1) {
                h += '<span class="vas-194-pager">' +
                        '<button type="button" class="vas-194-pbtn" data-mt="' + id + '" data-dir="-1"' + (t.page === 0 ? ' disabled' : '') + ' aria-label="Previous">' + ICON_PREV + '</button>' +
                        '<span class="vas-194-ptxt">' + (t.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pages + '</span>' +
                        '<button type="button" class="vas-194-pbtn" data-mt="' + id + '" data-dir="1"' + (t.page >= pages - 1 ? ' disabled' : '') + ' aria-label="Next">' + ICON_NEXT + '</button>' +
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

        function fitTable(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el || t.fixed) { return; }

            var avail = el.clientHeight;
            if (avail < 40) { return; }

            var head = el.querySelector('.vas-194-mhead');
            var foot = el.querySelector('.vas-194-mtfoot');
            var row = el.querySelector('.vas-194-mbody .vas-194-mrow');
            if (!head || !row) { return; }

            var rowH = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowH);
            n = Math.max(2, Math.min(t.max, n));

            if (n !== t.size) {
                t.size = n;
                drawTable(id);
            }
        }

        function fitAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { fitTable(id); }
            });
        }

        function mstatsHtml(items) {
            var h = '<div class="vas-194-mstats">';
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                h += '<div class="vas-194-mstat">' +
                        '<div class="vas-194-mstat-l">' + esc(it.l) + '</div>' +
                        '<div class="vas-194-mstat-v" title="' + esc(it.v) + '">' + esc(it.v) + '</div>' +
                     '</div>';
            }
            h += '</div>';
            return h;
        }

        /* ============================================================
           MODAL 1: MAIN POs PENDING DELIVERY MODAL
           ============================================================ */
        function openPendingDeliveryModal() {
            var all = kpiData.records || [];
            var cur = kpiData.baseCurrency || {};

            var formattedUndelivered = formatMoney(kpiData.undeliveredValue, cur.CurSymbol, cur.ISO_Code, cur.StdPrecision);

            var statStrip = mstatsHtml([
                { l: lbl('VAS_OpenPOs', 'Open POs'), v: formatNumber(kpiData.openPOs) },
                { l: lbl('VAS_ItemsPending', 'Items pending'), v: formatNumber(kpiData.itemsPending) },
                { l: lbl('VAS_UndeliveredValue', 'Undelivered value'), v: formattedUndelivered },
                { l: lbl('VAS_PastDue', 'Past due'), v: formatNumber(kpiData.pastDue) }
            ]);

            var cols = [
                { label: '', w: 0.32 },
                { label: lbl('VAS_PONumber', 'PO No'), w: 1.15, cls: 'vas-194-c-link' },
                { label: lbl('VAS_PODate', 'PO date'), w: 1.0 },
                { label: lbl('VAS_Vendor', 'Vendor'), w: 1.6 },
                { label: lbl('VAS_Warehouse', 'Warehouse'), w: 1.2 },
                { label: lbl('VAS_Ordered', 'Ordered'), w: 0.75, align: 'right' },
                { label: lbl('VAS_PendingItems', 'Pending items'), w: 0.9, align: 'right', cls: 'vas-194-c-prim' },
                { label: lbl('VAS_PendingValue', 'Pending value'), w: 0.95, align: 'right', cls: 'vas-194-c-emph' },
                { label: lbl('VAS_Expected', 'Expected'), w: 1.0 },
                { label: lbl('VAS_Delivery', 'Delivery'), w: 1.05 }
            ];

            var rows = all.map(function (p) {
                var pValueFormatted = formatMoney(p.PendingValue, p.CurrencySymbol, p.CurrencyISO);
                var delivText = p.DeliveryStatus === 'Partial' ? lbl('VAS_Partial', 'Partial') : lbl('VAS_Pending', 'Pending');
                var delivChip = p.DeliveryStatus === 'Partial' ? 'vas-194-chip-warn' : 'vas-194-chip-neutral';

                return [
                    { icon: p.PurchaseOrderNo, id: p.PurchaseOrderID },
                    { link: p.PurchaseOrderNo, id: p.PurchaseOrderID },
                    p.OrderDateDisplay || p.OrderDate,
                    p.VendorName,
                    p.WarehouseName || '—',
                    formatNumber(p.OrderedQty),
                    formatNumber(p.PendingQty),
                    pValueFormatted,
                    p.PromisedDateDisplay || p.PromisedDate || '—',
                    { chip: delivChip, text: delivText }
                ];
            });

            var bodyHtml =
                statStrip +
                '<div class="vas-194-msec">' + esc(lbl('VAS_POsAwaitingDelivery', 'Purchase orders awaiting delivery')) + '</div>' +
                pagedTable(cols, rows, { label: lbl('VAS_EarliestExpectedFirst', 'earliest expected first') });

            openModal({
                title: lbl('VAS_POsPendingDelivery', 'POs Pending Delivery'),
                subtitle: lbl('VAS_PendingDeliverySubtitle', 'All POs till date that are not fully delivered'),
                body: bodyHtml,
                foot: '<span class="vas-194-foot-note">' + all.length + ' ' + esc(lbl('VAS_OpenPOs', 'Open POs')) + ' · ' + esc(formattedUndelivered) + '</span>' +
                      '<span><button type="button" class="vas-194-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>'
            });
        }

        /* ============================================================
           MODAL 2: CHILD PO LINES MODAL (FROM LINES BUTTON)
           ============================================================ */
        function openLinesModal(poId, poNo) {
            var po = (kpiData.records || []).find(function (r) { return r.PurchaseOrderID === poId; });
            var vendor = po ? po.VendorName : '';
            var dateDisplay = po ? (po.OrderDateDisplay || po.OrderDate) : '';
            var delivStatus = po ? (po.DeliveryStatus === 'Partial' ? lbl('VAS_Partial', 'Partial') : lbl('VAS_Pending', 'Pending')) : '';

            // Load lines via AJAX
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_194_POsPendingDeliveryWidget/GetPOLines?C_Order_ID=' + poId,
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var lines = data.lines || [];

                    var totalOrderedQty = 0;
                    var totalPendingQty = 0;
                    var totalOrderValue = 0;

                    lines.forEach(function (l) {
                        totalOrderedQty += Number(l.OrderedQty || 0);
                        totalPendingQty += Number(l.PendingQty || 0);
                        totalOrderValue += Number(l.LineNetAmt || 0);
                    });

                    var curSym = (lines.length > 0 && lines[0].CurrencySymbol) ? lines[0].CurrencySymbol : '';
                    var curIso = (lines.length > 0 && lines[0].CurrencyISO) ? lines[0].CurrencyISO : '';
                    var pValFormatted = formatMoney(totalOrderValue, curSym, curIso);

                    var statStrip = mstatsHtml([
                        { l: lbl('VAS_Lines', 'Lines'), v: formatNumber(lines.length) },
                        { l: lbl('VAS_POValue', 'PO value'), v: pValFormatted },
                        { l: lbl('VAS_QtyOrdered', 'Qty ordered'), v: formatNumber(totalOrderedQty) },
                        { l: lbl('VAS_QtyPending', 'Qty pending'), v: formatNumber(totalPendingQty) }
                    ]);

                    var lineCols = [
                        { label: '#', w: 0.3, align: 'right' },
                        { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-194-c-prim' },
                        { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                        { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                        { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_PendingItems', 'Pending'), w: 0.7, align: 'right', cls: 'vas-194-c-prim' },
                        { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                        { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-194-c-emph' },
                        { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                    ];

                    var lineRows = lines.map(function (l, idx) {
                        var rateFmt = formatMoney(l.PriceActual, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);
                        var amtFmt = formatMoney(l.LineNetAmt, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);

                        var chipClass = 'vas-194-chip-neutral';
                        var statusTxt = l.LineStatus;
                        if (l.LineStatus === 'Received') {
                            chipClass = 'vas-194-chip-ok';
                            statusTxt = lbl('VAS_Received', 'Received');
                        } else if (l.LineStatus === 'Partial received') {
                            chipClass = 'vas-194-chip-warn';
                            statusTxt = lbl('VAS_PartialReceived', 'Partial received');
                        } else {
                            statusTxt = lbl('VAS_Pending', 'Pending');
                        }

                        return [
                            String(l.LineNo || (idx + 1)),
                            l.ProductName || l.ProductSKU || '—',
                            l.Attribute || '—',
                            l.UOM || '—',
                            formatDecimal(l.OrderedQty),
                            formatDecimal(l.DeliveredQty),
                            formatDecimal(l.PendingQty),
                            rateFmt,
                            amtFmt,
                            { chip: chipClass, text: statusTxt }
                        ];
                    });

                    var topPolink =
                        '<div class="vas-194-polink">' +
                            esc(lbl('VAS_PurchaseOrder', 'Purchase order')) + ' ' +
                            '<button type="button" class="vas-194-lnk" data-po-id="' + poId + '" data-po-no="' + esc(poNo) + '">' + esc(poNo) + '</button>' +
                            ' · ' + esc(dateDisplay) + ' · ' + esc(lbl('VAS_Completed', 'Completed')) +
                        '</div>';

                    var bodyHtml =
                        topPolink +
                        statStrip +
                        '<div class="vas-194-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                        pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + poNo });

                    openModal({
                        child: true,
                        size: 'md',
                        title: lbl('VAS_Lines', 'Lines') + ' · ' + poNo,
                        subtitle: vendor + ' · ' + dateDisplay + ' · ' + delivStatus,
                        body: bodyHtml,
                        foot: '<span class="vas-194-foot-note">' + esc(poNo) + ' · ' + esc(vendor) + '</span>' +
                              '<span><button type="button" class="vas-194-btn vas-194-btn-back">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                              '<button type="button" class="vas-194-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>',
                        after: function () {
                            $modalHost.find('.vas-194-btn-back').on('click', popModal);
                        }
                    });
                }
            });
        }

        /* ============================================================
           MODAL 3: CHILD PO RECORD DETAIL MODAL (FROM PO NUMBER LINK)
           ============================================================ */
        function openRecordModal(poId, poNo) {
            var po = (kpiData.records || []).find(function (r) { return r.PurchaseOrderID === poId; });

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_194_POsPendingDeliveryWidget/GetPODetail?C_Order_ID=' + poId,
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var hdr = data.header || {};

                    var vendor = hdr.VendorName || (po ? po.VendorName : '');
                    var dateDisplay = hdr.OrderDateDisplay || (po ? po.OrderDateDisplay : '');
                    var expDateDisplay = hdr.PromisedDateDisplay || (po ? po.PromisedDateDisplay : '—');
                    var whName = hdr.WarehouseName || (po ? po.WarehouseName : '—');
                    var createdBy = (hdr.CreatedBy || '') + (hdr.CreatedOn ? (' · ' + hdr.CreatedOn) : '');
                    var docStatusTxt = lbl('VAS_Completed', 'Completed');
                    var delivStatusTxt = (po && po.DeliveryStatus === 'Partial') ? lbl('VAS_Partial', 'Partial') : lbl('VAS_Pending', 'Pending');

                    var pValFormatted = po ? formatMoney(po.TotalOrderValue, po.CurrencySymbol, po.CurrencyISO) : '—';

                    var headerStatsHtml = mstatsHtml([
                        { l: lbl('VAS_Vendor', 'Vendor'), v: vendor },
                        { l: lbl('VAS_PODate', 'PO date'), v: dateDisplay },
                        { l: lbl('VAS_ExpectedOn', 'Expected on'), v: expDateDisplay },
                        { l: lbl('VAS_POValue', 'PO value'), v: pValFormatted },
                        { l: lbl('VAS_Warehouse', 'Warehouse'), v: whName },
                        { l: lbl('VAS_CreatedBy', 'Created by'), v: createdBy || '—' },
                        { l: lbl('VAS_DocumentStatus', 'Document status'), v: docStatusTxt },
                        { l: lbl('VAS_DeliveryStatus', 'Delivery status'), v: delivStatusTxt }
                    ]);

                    // Now load lines for this PO
                    $.ajax({
                        url: VIS.Application.contextUrl + 'VAS_194_POsPendingDeliveryWidget/GetPOLines?C_Order_ID=' + poId,
                        type: 'GET',
                        cache: false,
                        success: function (lineRes) {
                            var lineData = parseResponse(lineRes);
                            var lines = lineData.lines || [];

                            var totalOrderedQty = 0;
                            var totalPendingQty = 0;
                            lines.forEach(function (l) {
                                totalOrderedQty += Number(l.OrderedQty || 0);
                                totalPendingQty += Number(l.PendingQty || 0);
                            });

                            var lineCols = [
                                { label: '#', w: 0.3, align: 'right' },
                                { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-194-c-prim' },
                                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                                { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                                { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_PendingItems', 'Pending'), w: 0.7, align: 'right', cls: 'vas-194-c-prim' },
                                { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-194-c-emph' },
                                { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                            ];

                            var lineRows = lines.map(function (l, idx) {
                                var rateFmt = formatMoney(l.PriceActual, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);
                                var amtFmt = formatMoney(l.LineNetAmt, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);

                                var chipClass = 'vas-194-chip-neutral';
                                var statusTxt = l.LineStatus;
                                if (l.LineStatus === 'Received') {
                                    chipClass = 'vas-194-chip-ok';
                                    statusTxt = lbl('VAS_Received', 'Received');
                                } else if (l.LineStatus === 'Partial received') {
                                    chipClass = 'vas-194-chip-warn';
                                    statusTxt = lbl('VAS_PartialReceived', 'Partial received');
                                } else {
                                    statusTxt = lbl('VAS_Pending', 'Pending');
                                }

                                return [
                                    String(l.LineNo || (idx + 1)),
                                    l.ProductName || l.ProductSKU || '—',
                                    l.Attribute || '—',
                                    l.UOM || '—',
                                    formatDecimal(l.OrderedQty),
                                    formatDecimal(l.DeliveredQty),
                                    formatDecimal(l.PendingQty),
                                    rateFmt,
                                    amtFmt,
                                    { chip: chipClass, text: statusTxt }
                                ];
                            });

                            var bodyHtml =
                                headerStatsHtml +
                                '<div class="vas-194-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                                pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + poNo });

                            var footNote = lines.length + ' ' + lbl('VAS_Lines', 'lines') + ' · ' +
                                           formatNumber(totalOrderedQty) + ' ' + lbl('VAS_QtyOrdered', 'qty ordered') + ' · ' +
                                           delivStatusTxt;

                            openModal({
                                child: true,
                                title: poNo,
                                subtitle: vendor + ' · ' + dateDisplay + ' · ' + docStatusTxt,
                                body: bodyHtml,
                                foot: '<span class="vas-194-foot-note">' + esc(footNote) + '</span>' +
                                      '<span><button type="button" class="vas-194-btn vas-194-btn-primary vas-194-btn-open-record">' + esc(lbl('VAS_OpenRecord', 'Open Record')) + '</button> ' +
                                      '<button type="button" class="vas-194-btn vas-194-btn-back">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                                      '<button type="button" class="vas-194-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>',
                                after: function () {
                                    $modalHost.find('.vas-194-btn-back').on('click', popModal);
                                    $modalHost.find('.vas-194-btn-open-record').on('click', function () {
                                        openPurchaseOrderRecord(poId);
                                    });
                                }
                            });
                        }
                    });
                }
            });
        }

        this.refreshWidget = function () {
            loadKpiData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $(document).off('keydown.vas194');
            $(window).off('resize.vas194');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $root.remove();
        };
    };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_194_POsPendingDeliveryWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
