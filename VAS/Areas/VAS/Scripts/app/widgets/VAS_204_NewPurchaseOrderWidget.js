/**
 * VAS_204_NewPurchaseOrderWidget
 * 1x1 Quick Action Tile for Purchase Order Dashboard.
 * Opens the Open Requisitions picker to start the requisition-to-purchase-order flow,
 * selecting pending lines, filling order parameters, and generating a Purchase Order.
 *
 * Summary Message Table
 *  # | Current Text                                       | Message Key
 * ---+----------------------------------------------------+---------------------------------------
 *  1 | New Purchase Order                                 | VAS_NewPurchaseOrder
 *  2 | Open Requisitions                                  | VAS_OpenRequisitions
 *  3 | Select a requisition to raise a purchase order...  | VAS_SelectRequisitionSub
 *  4 | Open requisitions                                  | VAS_OpenRequisitionsStat
 *  5 | Ready to PO                                        | VAS_ReadyToPO
 *  6 | Partly ordered                                     | VAS_PartlyOrdered
 *  7 | Pending qty                                        | VAS_PendingQty
 *  8 | Requisition                                        | VAS_Requisition
 *  9 | Lines                                              | VAS_Lines
 * 10 | Req qty                                            | VAS_ReqQty
 * 11 | Already ordered                                    | VAS_AlreadyOrdered
 * 12 | Needed by                                          | VAS_NeededBy
 * 13 | Status                                             | VAS_Status
 * 14 | One requisition at a time · its lines can go into...| VAS_OneReqAtTime
 * 15 | Close                                              | VAS_Close
 * 16 | Back                                               | VAS_Back
 * 17 | Continue                                           | VAS_Continue
 * 18 | Cancel                                             | VAS_Cancel
 * 19 | Product                                            | VAS_Product
 * 20 | Attribute                                          | VAS_Attribute
 * 21 | UoM                                                | VAS_UoM
 * 22 | Qty to order                                       | VAS_QtyToOrder
 * 23 | Vendor                                             | VAS_Vendor
 * 24 | Rate                                               | VAS_Rate
 * 25 | Amount                                             | VAS_Amount
 * 26 | Tax                                                | VAS_Tax
 * 27 | Date promised                                      | VAS_DatePromised
 * 28 | PO date                                            | VAS_PODate
 * 29 | Warehouse                                          | VAS_Warehouse
 * 30 | Payment term                                       | VAS_PaymentTerm
 * 31 | Payment method                                     | VAS_PaymentMethod
 * 32 | Description                                        | VAS_Description
 * 33 | Print description                                  | VAS_PrintDescription
 * 34 | Target document type                               | VAS_TargetDocType
 * 35 | Order reference                                    | VAS_OrderReference
 * 36 | Priority                                           | VAS_Priority
 * 37 | Price list                                         | VAS_PriceList
 * 38 | Currency                                           | VAS_Currency
 * 39 | Currency rate type                                 | VAS_CurrencyRateType
 * 40 | Incoterm                                           | VAS_Incoterm
 * 41 | Create PO                                          | VAS_CreatePO
 * 42 | Continue to lines                                  | VAS_ContinueToLines
 * 43 | No lines selected                                  | VAS_NoLinesSelected
 * 44 | lines selected                                     | VAS_LinesSelected
 * 45 | different vendors — one PO needs a single vendor   | VAS_MultiVendorWarning
 * 46 | Direct New PO (Blank)                              | VAS_DirectNewPO
 * 47 | Purchase order created                             | VAS_POCreatedSuccess
 * 48 | Unable to open Purchase Order window               | VAS_CouldntOpenPOWindow
 * 49 | Subtotal                                           | VAS_Subtotal
 * 50 | Order total                                        | VAS_OrderTotal
 * 51 | Requisition lines · select one or many             | VAS_ReqLinesSelectHeader
 * 52 | New Purchase Order · Details                       | VAS_NewPODetailsHeader
 * 53 | New Purchase Order · Lines                         | VAS_NewPOLinesHeader
 * 54 | Normal                                             | VAS_PriorityNormal
 * 55 | High                                               | VAS_PriorityHigh
 * 56 | Urgent                                             | VAS_PriorityUrgent
 * 57 | Low                                                | VAS_PriorityLow
 * 58 | Page 1 of 2                                        | VAS_Page1Of2
 * 59 | Page 2 of 2                                        | VAS_Page2Of2
 * 60 | from                                               | VAS_From
 * 61 | Showing                                            | VAS_Showing
 * 62 | of                                                 | VAS_Of
 * 63 | Back to details                                    | VAS_BackToDetails
 * 64 | Standard Vendor                                    | VAS_StandardVendor
 * 65 | Purchase against                                   | VAS_PurchaseAgainst
 * 66 | Error creating Purchase Order                      | VAS_ErrorCreatingPO
 * 67 | Server error creating Purchase Order               | VAS_ServerTimeoutError
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

    VAS.VAS_204_NewPurchaseOrderWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-204-npo-root">');
        var $card;
        var poWindowId = 0;
        var isBusy = false;

        // Modal elements
        var $mask = null;
        var $modal = null;
        var $mTitle = null;
        var $mSub = null;
        var $mBack = null;
        var $mClose = null;
        var $mBody = null;
        var $mFoot = null;

        // Navigation state & lookup cache
        var CFGSTACK = [];
        var CURCFG = null;
        var BACKMODE = false;
        var MT = {};
        var MT_SEQ = 0;

        var cachedRequisitions = [];
        var activeReq = null;
        var selectedPoLines = [];
        var poHeadValues = {};
        var formLookups = null;

        function lbl(key, fallback) {
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
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            return data || {};
        }

        function fmtNum(v) {
            var n = Number(v || 0);
            return isNaN(n) ? "0" : n.toLocaleString(window.navigator.language || 'en-US', { maximumFractionDigits: 2 });
        }

        function fmtMoney(v) {
            var n = Number(v || 0);
            if (n >= 1e7) { return (n / 1e7).toFixed(2) + ' Cr'; }
            if (n >= 1e5) { return (n / 1e5).toFixed(2) + ' L'; }
            return n.toLocaleString(window.navigator.language || 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        function toast(msg) {
            var $t = $('#vas_204_toast');
            if (!$t.length) {
                $t = $('<div id="vas_204_toast" class="vas-204-toast"></div>');
                $('body').append($t);
            }
            $t.text(msg).addClass('show');
            clearTimeout($t.data('timer'));
            $t.data('timer', setTimeout(function () {
                $t.removeClass('show');
            }, 3200));
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadPOWindowId();
            loadLookups();
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

        function loadPOWindowId() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/GetPurchaseOrderWindowId',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var res = parseResponse(response);
                    poWindowId = Number((res && res.windowId) || 0);
                }
            });
        }

        function loadLookups() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/GetFormLookups',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var res = parseResponse(response);
                    formLookups = res || {};
                }
            });
        }

        function createWidget() {
            var title = lbl('VAS_NewPurchaseOrder', 'New Purchase Order');

            $card = $(
                '<button type="button" class="vas-204-npo-card" aria-label="' + escapeHtml(title) + '">' +
                '<span class="vas-204-npo-well" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M12 5v14M5 12h14"/>' +
                '</svg>' +
                '</span>' +
                '<span class="vas-204-npo-title">' + escapeHtml(title) + '</span>' +
                '</button>'
            );

            $card.on('click', function () {
                openRequisitionsModal();
            });

            $root.append($card);
        }

        /* ============================================================
           MODAL ENGINE & MULTI-STEP FLOW
           ============================================================ */

        function ensureModalHost() {
            if ($mask && $mask.length && document.body.contains($mask[0])) {
                return;
            }

            var modalHtml =
                '<div class="vas-204-mask" role="dialog" aria-modal="true">' +
                '  <div class="vas-204-modal">' +
                '    <div class="vas-204-modal-header">' +
                '      <div class="vas-204-htxt-wrap">' +
                '        <button type="button" class="vas-204-xbtn vas-204-mback" aria-label="' + escapeHtml(lbl('VAS_Back', 'Back')) + '" style="display:none;">' +
                '          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                '        </button>' +
                '        <div class="vas-204-htxt"><h2 class="vas-204-mtitle"></h2><div class="vas-204-msub"></div></div>' +
                '      </div>' +
                '      <div class="vas-204-hact">' +
                '        <button type="button" class="vas-204-xbtn vas-204-mclose" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">' +
                '          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                '        </button>' +
                '      </div>' +
                '    </div>' +
                '    <div class="vas-204-modal-body"></div>' +
                '    <div class="vas-204-modal-foot"></div>' +
                '  </div>' +
                '</div>';

            $mask = $(modalHtml);
            $modal = $mask.find('.vas-204-modal');
            $mTitle = $mask.find('.vas-204-mtitle');
            $mSub = $mask.find('.vas-204-msub');
            $mBack = $mask.find('.vas-204-mback');
            $mClose = $mask.find('.vas-204-mclose');
            $mBody = $mask.find('.vas-204-modal-body');
            $mFoot = $mask.find('.vas-204-modal-foot');

            $mBack.on('click', function () { backModal(); });
            $mClose.on('click', function () { closeModal(); });

            $mask.on('click', function (e) {
                if (e.target === $mask[0] || $(e.target).closest('[data-close]').length) {
                    closeModal();
                }
            });

            $(document).on('keydown.vas204', function (e) {
                if (e.key === 'Escape' && $mask.hasClass('open')) {
                    closeModal();
                }
            });

            $(window).on('resize.vas204', function () {
                if ($mask.hasClass('open')) {
                    fitAllTables();
                }
            });

            $('body').append($mask);
        }

        function openModal(cfg, isBack) {
            ensureModalHost();

            if (BACKMODE) {
                CURCFG = cfg;
            } else if (!isBack) {
                if (cfg.child && CURCFG) {
                    CFGSTACK.push(CURCFG);
                } else if (!cfg.child) {
                    CFGSTACK = [];
                }
                CURCFG = cfg;
            }

            if (CFGSTACK.length > 0) {
                $mBack.show();
            } else {
                $mBack.hide();
            }

            $mBody.attr('class', 'vas-204-modal-body' + (cfg.bodyClass ? ' ' + cfg.bodyClass : ''));
            $modal.attr('class', 'vas-204-modal' + (cfg.size ? ' ' + cfg.size : ''));

            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
            $mBody.html(cfg.body || '');
            $mFoot.html(cfg.foot || '<span class="vas-204-foot-note"></span><button type="button" class="vas-204-btn" data-close="1">' + escapeHtml(lbl('VAS_Close', 'Close')) + '</button>');

            $mask.addClass('open');
            drawAllTables();

            requestAnimationFrame(function () {
                fitAllTables();
                requestAnimationFrame(fitAllTables);
            });

            if (typeof cfg.after === 'function') {
                cfg.after();
            }
        }

        function closeModal() {
            if ($mask) {
                $mask.removeClass('open');
            }
            CFGSTACK = [];
            CURCFG = null;
            MT = {};
        }

        function backModal() {
            var prev = CFGSTACK.pop();
            if (!prev) {
                closeModal();
                return;
            }
            if (typeof prev.reopen === 'function') {
                BACKMODE = true;
                try {
                    prev.reopen();
                } finally {
                    BACKMODE = false;
                }
            } else {
                CURCFG = prev;
                openModal(prev, true);
            }
        }

        /* Paged Table Engine */
        function pagedTable(cols, rows, opts) {
            opts = opts || {};
            var id = 'vas204_mt_' + (++MT_SEQ);
            var maxRows = Math.min(10, opts.size || 10);
            MT[id] = {
                cols: cols,
                rows: rows,
                size: maxRows,
                max: maxRows,
                page: 0,
                label: opts.label || '',
                render: opts.render || null,
                fixed: !!opts.fixed,
                rowClass: opts.rowClass || ''
            };
            return '<div class="vas-204-mtwrap' + (opts.fixed ? ' fixed' : '') + '" id="' + id + '"></div>';
        }

        function cellHTML(cell, c) {
            if (cell && typeof cell === 'object') {
                if (cell.link) {
                    return '<span class="vas-204-cell"><button type="button" class="vas-204-lnk" data-po="' + escapeHtml(cell.link) + '" title="' + escapeHtml(cell.link) + '">' + escapeHtml(cell.link) + '</button></span>';
                }
                if (cell.chip) {
                    return '<span class="vas-204-cell" title="' + escapeHtml(cell.text) + '"><span class="vas-204-chip ' + escapeHtml(cell.chip) + '">' + escapeHtml(cell.text) + '</span></span>';
                }
            }
            var alignCls = (c && c.align === 'right') ? ' right' : '';
            var textCls = (c && c.cls) ? ' ' + c.cls : ' c-std';
            return '<span class="vas-204-cell' + alignCls + textCls + '" title="' + escapeHtml(cell) + '">' + escapeHtml(cell) + '</span>';
        }

        function mtDraw(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el) { return; }

            var tpl = t.cols.map(function (c) {
                return 'minmax(0,' + (c.w || 1) + 'fr)';
            }).join(' ');

            var pages = Math.max(1, Math.ceil(t.rows.length / t.size));
            if (t.page > pages - 1) { t.page = pages - 1; }

            var s = t.page * t.size;
            var slice = t.rows.slice(s, s + t.size);

            var h = '<div class="vas-204-mtbl"><div class="vas-204-mrow vas-204-mhead" style="grid-template-columns:' + tpl + '">';
            h += t.cols.map(function (c) {
                return '<span class="vas-204-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label || '') + '">' + escapeHtml(c.label || '') + '</span>';
            }).join('');
            h += '</div><div class="vas-204-mbody">';

            h += slice.map(function (r, i) {
                if (t.render) {
                    return '<div class="vas-204-mrow' + (t.rowClass ? ' ' + t.rowClass : '') + (r.sel ? ' sel' : '') + '" style="grid-template-columns:' + tpl + '" data-i="' + (s + i) + '">' + t.render(r, s + i) + '</div>';
                }
                return '<div class="vas-204-mrow" style="grid-template-columns:' + tpl + '">' +
                    r.map(function (cell, ci) {
                        return cellHTML(cell, t.cols[ci]);
                    }).join('') + '</div>';
            }).join('');

            h += '</div></div>';

            // Footer Pager
            var showStart = slice.length ? (s + 1) : 0;
            var showEnd = s + slice.length;
            h += '<div class="vas-204-mtfoot">';
            h += '<span class="vas-204-helper">' + escapeHtml(lbl('VAS_Showing', 'Showing') + ' ' + showStart + '–' + showEnd + ' ' + lbl('VAS_Of', 'of') + ' ' + t.rows.length + (t.label ? ' · ' + t.label : '')) + '</span>';

            if (pages > 1) {
                h += '<span class="vas-204-pager">' +
                    '<button type="button" class="vas-204-pbtn" data-mt="' + id + '" data-dir="-1"' + (t.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl('Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                    '<span class="vas-204-ptxt">' + (t.page + 1) + ' ' + escapeHtml(lbl('VAS_Of', 'of')) + ' ' + pages + '</span>' +
                    '<button type="button" class="vas-204-pbtn" data-mt="' + id + '" data-dir="1"' + (t.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl('Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                    '</span>';
            } else {
                h += '<span></span>';
            }
            h += '</div>';

            el.innerHTML = h;
        }

        function drawAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) {
                    mtDraw(id);
                }
            });
        }

        function mtFit(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el || t.fixed) { return; }

            var avail = el.getBoundingClientRect().height;
            if (avail < 40) { return; }

            var head = el.querySelector('.vas-204-mhead');
            var foot = el.querySelector('.vas-204-mtfoot');
            var row = el.querySelector('.vas-204-mbody .vas-204-mrow');
            if (!head || !row) { return; }

            var rowH = row.getBoundingClientRect().height || 32;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowH);
            n = Math.max(2, Math.min(t.max, n));

            if (n !== t.size) {
                t.size = n;
                mtDraw(id);
            }
        }

        function fitAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) {
                    mtFit(id);
                }
            });
            if ($mBody && $mBody[0]) {
                $mBody.removeClass('overflowing');
                if ($mBody[0].scrollHeight > $mBody[0].clientHeight + 2) {
                    $mBody.addClass('overflowing');
                }
            }
        }

        // Delegate paged table clicks
        $(document).on('click', '.vas-204-pbtn[data-mt]', function (e) {
            e.stopPropagation();
            var mtId = $(this).data('mt');
            var dir = +($(this).data('dir') || 0);
            var t = MT[mtId];
            if (!t) { return; }
            var pages = Math.ceil(t.rows.length / t.size);
            t.page = Math.min(pages - 1, Math.max(0, t.page + dir));
            mtDraw(mtId);
        });

        /* ============================================================
           STEP 1: OPEN REQUISITIONS LIST
           ============================================================ */

        function openRequisitionsModal() {
            openModal({
                title: lbl('VAS_OpenRequisitions', 'Open Requisitions'),
                subtitle: lbl('VAS_SelectRequisitionSub', 'Select a requisition to raise a purchase order against'),
                body: '<div class="vas-204-loading">' + escapeHtml(lbl('Loading', 'Loading...')) + '</div>',
                foot: '<span class="vas-204-foot-note">' + escapeHtml(lbl('VAS_OneReqAtTime', 'One requisition at a time · its lines can go into one PO')) + '</span>' +
                    '<span><button type="button" class="vas-204-btn vas-204-direct-po">' + escapeHtml(lbl('VAS_DirectNewPO', 'Direct New PO (Blank)')) + '</button> ' +
                    '<button type="button" class="vas-204-btn" data-close="1">' + escapeHtml(lbl('VAS_Close', 'Close')) + '</button></span>',
                after: function () {
                    $mFoot.find('.vas-204-direct-po').on('click', function () {
                        openDirectNewPORecord();
                    });
                    loadRequisitionsList();
                }
            });
        }

        function loadRequisitionsList() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/GetOpenRequisitions',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var res = parseResponse(response);
                    cachedRequisitions = (res && res.rows) || [];
                    var summary = (res && res.summary) || {
                        openRequisitions: cachedRequisitions.length,
                        readyToPO: 0,
                        partlyOrdered: 0,
                        pendingQty: 0
                    };

                    renderRequisitionsStep(cachedRequisitions, summary);
                },
                error: function () {
                    $mBody.html('<div class="vas-204-empty-msg">' + escapeHtml(lbl('VAS_ErrorLoadingData', 'Unable to load requisitions.')) + '</div>');
                }
            });
        }

        function renderRequisitionsStep(reqs, summary) {
            var reqCols = [
                { label: lbl('VAS_Requisition', 'Requisition'), w: 1.2, cls: 'c-prim' },
                { label: lbl('VAS_Lines', 'Lines'), w: 0.6, align: 'right' },
                { label: lbl('VAS_ReqQty', 'Req qty'), w: 0.9, align: 'right' },
                { label: lbl('VAS_AlreadyOrdered', 'Already ordered'), w: 1, align: 'right' },
                { label: lbl('VAS_PendingQty', 'Pending qty'), w: 0.9, align: 'right', cls: 'c-prim' },
                { label: lbl('VAS_NeededBy', 'Needed by'), w: 1 },
                { label: lbl('VAS_Status', 'Status'), w: 1.1 }
            ];

            var rows = reqs.map(function (r) {
                return [
                    r.requisitionNumber,
                    String(r.lineCount),
                    fmtNum(r.requisitionQty),
                    r.alreadyOrderedQty > 0 ? fmtNum(r.alreadyOrderedQty) : '—',
                    fmtNum(r.pendingQty),
                    r.neededByDisplay || r.neededBy,
                    { chip: r.statusChip, text: r.status }
                ];
            });

            var statsHtml =
                '<div class="vas-204-mstats">' +
                '  <div class="vas-204-mstat"><div class="l">' + escapeHtml(lbl('VAS_OpenRequisitionsStat', 'Open requisitions')) + '</div><div class="v">' + fmtNum(summary.openRequisitions) + '</div></div>' +
                '  <div class="vas-204-mstat"><div class="l">' + escapeHtml(lbl('VAS_ReadyToPO', 'Ready to PO')) + '</div><div class="v">' + fmtNum(summary.readyToPO) + '</div></div>' +
                '  <div class="vas-204-mstat"><div class="l">' + escapeHtml(lbl('VAS_PartlyOrdered', 'Partly ordered')) + '</div><div class="v">' + fmtNum(summary.partlyOrdered) + '</div></div>' +
                '  <div class="vas-204-mstat"><div class="l">' + escapeHtml(lbl('VAS_PendingQty', 'Pending qty')) + '</div><div class="v">' + fmtNum(summary.pendingQty) + '</div></div>' +
                '</div>';

            var bodyHtml = statsHtml +
                '<div class="vas-204-msec">' + escapeHtml(lbl('VAS_Requisitions', 'Requisitions')) + '</div>' +
                pagedTable(reqCols, rows, {
                    label: lbl('VAS_SelectRowToOpenLines', 'select a row to open its lines'),
                    rowClass: 'pick',
                    render: function (r, idx) {
                        return reqCols.map(function (c, ci) {
                            return cellHTML(r[ci], c);
                        }).join('');
                    }
                });

            $mBody.html(bodyHtml);
            drawAllTables();
            fitAllTables();

            // Row selection click handler
            $mBody.off('click.reqrow').on('click.reqrow', '.vas-204-mrow[data-i]', function (e) {
                if ($(e.target).closest('button, input, select').length) { return; }
                var idx = +($(this).data('i'));
                if (cachedRequisitions[idx]) {
                    stepRequisitionLines(cachedRequisitions[idx]);
                }
            });
        }

        /* ============================================================
           STEP 2: REQUISITION LINES SELECTION
           ============================================================ */

        function stepRequisitionLines(req) {
            activeReq = req;
            var reqId = req.requisitionId;

            openModal({
                child: true,
                reopen: function () { stepRequisitionLines(req); },
                title: req.requisitionNumber,
                subtitle: (req.neededByDisplay || req.neededBy) + ' · ' + lbl('VAS_SelectLinesToRaisePO', 'select the lines to raise a PO against'),
                body: '<div class="vas-204-loading">' + escapeHtml(lbl('Loading', 'Loading...')) + '</div>',
                foot: '<span class="vas-204-foot-note" id="vas204_selNote">' + escapeHtml(lbl('VAS_NoLinesSelected', 'No lines selected')) + '</span>' +
                    '<span><button type="button" class="vas-204-btn vas-204-btn-back">' + escapeHtml(lbl('VAS_Back', 'Back')) + '</button> ' +
                    '<button type="button" class="vas-204-btn vas-204-btn-primary vas-204-btn-continue" disabled>' + escapeHtml(lbl('VAS_Continue', 'Continue')) + '</button></span>',
                after: function () {
                    $mFoot.find('.vas-204-btn-back').on('click', function () { backModal(); });
                    $mFoot.find('.vas-204-btn-continue').on('click', function () { startNewPOForm(); });
                    loadRequisitionLinesData(reqId);
                }
            });
        }

        function loadRequisitionLinesData(reqId) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/GetRequisitionLines',
                type: 'GET',
                data: { requisitionId: reqId },
                cache: false,
                success: function (response) {
                    var res = parseResponse(response);
                    var lines = (res && res.lines) || [];
                    if (activeReq) {
                        activeReq.lines = lines;
                    }
                    renderRequisitionLinesStep(activeReq, lines);
                },
                error: function () {
                    $mBody.html('<div class="vas-204-empty-msg">' + escapeHtml(lbl('VAS_ErrorLoadingLines', 'Unable to load requisition lines.')) + '</div>');
                }
            });
        }

        function renderRequisitionLinesStep(req, lines) {
            var lineCols = [
                { label: '', w: 0.28 },
                { label: lbl('VAS_Product', 'Product'), w: 1.6, cls: 'c-dark' },
                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.25 },
                { label: lbl('VAS_UoM', 'UoM'), w: 0.5 },
                { label: lbl('VAS_ReqQty', 'Req qty'), w: 0.75, align: 'right' },
                { label: lbl('VAS_AlreadyOrdered', 'Already ordered'), w: 0.9, align: 'right' },
                { label: lbl('VAS_PendingQty', 'Pending'), w: 0.75, align: 'right', cls: 'c-prim' },
                { label: lbl('VAS_QtyToOrder', 'Qty to order'), w: 0.9, align: 'right' },
                { label: lbl('VAS_Vendor', 'Vendor'), w: 1.6 },
                { label: lbl('VAS_Rate', 'Rate'), w: 0.75, align: 'right' }
            ];

            var vendorsList = (formLookups && formLookups.vendors) || [];

            var summaryHtml =
                '<div class="vas-204-posum">' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Requisition', 'Requisition')) + '</div><div class="v">' + escapeHtml(req.requisitionNumber) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Lines', 'Lines')) + '</div><div class="v">' + fmtNum(lines.length) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_ReqQty', 'Req qty')) + '</div><div class="v">' + fmtNum(req.requisitionQty) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_AlreadyOrdered', 'Already ordered')) + '</div><div class="v">' + (req.alreadyOrderedQty > 0 ? fmtNum(req.alreadyOrderedQty) : '—') + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_PendingQty', 'Pending qty')) + '</div><div class="v">' + fmtNum(req.pendingQty) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_NeededBy', 'Needed by')) + '</div><div class="v">' + escapeHtml(req.neededByDisplay || req.neededBy) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Status', 'Status')) + '</div><div class="v">' + escapeHtml(req.status) + '</div></div>' +
                '</div>';

            var bodyHtml = summaryHtml +
                '<div class="vas-204-msec">' + escapeHtml(lbl('VAS_ReqLinesSelectHeader', 'Requisition lines · select one or many')) + '</div>' +
                pagedTable(lineCols, lines, {
                    label: lbl('VAS_SetQtyAndVendor', 'set quantity and vendor per line'),
                    rowClass: 'pick',
                    render: function (l, i) {
                        var isSel = !!l.sel;
                        var vendorOpts = '';
                        if (vendorsList.length > 0) {
                            vendorOpts = vendorsList.map(function (v) {
                                return '<option value="' + v.id + '"' + (v.id === l.vendorId || v.name === l.vendorName ? ' selected' : '') + '>' + escapeHtml(v.name) + '</option>';
                            }).join('');
                        } else {
                            vendorOpts = '<option value="' + (l.vendorId || 0) + '">' + escapeHtml(l.vendorName || 'Standard Vendor') + '</option>';
                        }

                        return '<span class="vas-204-cell"><input type="checkbox" class="vas-204-chk line" data-i="' + i + '"' + (isSel ? ' checked' : '') + ' aria-label="Select line"></span>' +
                            '<span class="vas-204-cell c-dark" title="' + escapeHtml(l.productName) + '">' + escapeHtml(l.productName) + '</span>' +
                            '<span class="vas-204-cell c-std" title="' + escapeHtml(l.attribute || 'Standard') + '">' + escapeHtml(l.attribute || 'Standard') + '</span>' +
                            '<span class="vas-204-cell c-std">' + escapeHtml(l.uom) + '</span>' +
                            '<span class="vas-204-cell right c-std">' + fmtNum(l.reqQty) + '</span>' +
                            '<span class="vas-204-cell right ' + (l.alreadyOrderedQty > 0 ? 'c-dark' : 'c-std') + '">' + (l.alreadyOrderedQty > 0 ? fmtNum(l.alreadyOrderedQty) : '—') + '</span>' +
                            '<span class="vas-204-cell right c-prim">' + fmtNum(l.pendingQty) + '</span>' +
                            '<span class="vas-204-cell"><input class="vas-204-rowin qty" type="number" min="1" max="' + l.pendingQty + '" value="' + (l.orderQty || l.pendingQty) + '" data-i="' + i + '" aria-label="Qty to order"></span>' +
                            '<span class="vas-204-cell"><select class="vas-204-rowsel vend" data-i="' + i + '" aria-label="Vendor">' + vendorOpts + '</select></span>' +
                            '<span class="vas-204-cell right c-std">' + fmtMoney(l.rate) + '</span>';
                    }
                });

            $mBody.html(bodyHtml);
            drawAllTables();
            fitAllTables();
            syncLineSelectionFooter();

            // Line change events
            $mBody.off('change.linechange').on('change.linechange', function (e) {
                var $t = $(e.target);
                if (!activeReq || !activeReq.lines) { return; }
                var idx = +($t.data('i'));
                var line = activeReq.lines[idx];
                if (!line) { return; }

                if ($t.hasClass('line')) {
                    line.sel = $t.prop('checked');
                    $t.closest('.vas-204-mrow').toggleClass('sel', line.sel);
                    syncLineSelectionFooter();
                } else if ($t.hasClass('qty')) {
                    var val = Math.max(1, Math.min(line.pendingQty, +($t.val() || 1)));
                    line.orderQty = val;
                    $t.val(val);
                    syncLineSelectionFooter();
                } else if ($t.hasClass('vend')) {
                    line.vendorId = +($t.val() || 0);
                    line.vendorName = $t.find('option:selected').text();
                    syncLineSelectionFooter();
                }
            });
        }

        function getSelectedLines() {
            return (activeReq && activeReq.lines) ? activeReq.lines.filter(function (l) { return l.sel; }) : [];
        }

        function syncLineSelectionFooter() {
            var sel = getSelectedLines();
            var $note = $('#vas204_selNote');
            var $btn = $mFoot.find('.vas-204-btn-continue');
            if (!$note.length || !$btn.length) { return; }

            if (!sel.length) {
                $note.attr('class', 'vas-204-foot-note').text(lbl('VAS_NoLinesSelected', 'No lines selected'));
                $btn.prop('disabled', true);
                return;
            }

            var vendors = sel.map(function (l) { return l.vendorName || ('Vendor_' + l.vendorId); })
                .filter(function (v, i, a) { return a.indexOf(v) === i; });

            if (vendors.length > 1) {
                $note.attr('class', 'vas-204-warnnote').text(
                    sel.length + ' ' + lbl('VAS_LinesSelected', 'lines selected') + ' · ' +
                    vendors.length + ' ' + lbl('VAS_MultiVendorWarning', 'different vendors — one PO needs a single vendor')
                );
                $btn.prop('disabled', true);
                return;
            }

            var totalVal = sel.reduce(function (acc, l) {
                return acc + ((l.orderQty || l.pendingQty) * (l.rate || 0));
            }, 0);

            $note.attr('class', 'vas-204-foot-note').text(
                sel.length + ' ' + (sel.length > 1 ? lbl('VAS_LinesSelected', 'lines selected') : lbl('VAS_LineSelected', 'line selected')) +
                ' · ' + (vendors[0] || '') + ' · ' + fmtMoney(totalVal)
            );
            $btn.prop('disabled', false);
        }

        /* ============================================================
           STEP 3: PO DETAILS (PAGE 1 OF 2)
           ============================================================ */

        function startNewPOForm() {
            var lines = getSelectedLines();
            if (!lines.length) { return; }

            selectedPoLines = lines.map(function (l) {
                return {
                    requisitionLineId: l.lineId,
                    productId: l.productId,
                    productName: l.productName,
                    productCode: l.productCode,
                    attribute: l.attribute || 'Standard',
                    uom: l.uom,
                    rate: l.rate || 0,
                    orderQty: l.orderQty || l.pendingQty,
                    taxId: (formLookups && formLookups.taxes && formLookups.taxes[0]) ? formLookups.taxes[0].id : 0,
                    taxRate: (formLookups && formLookups.taxes && formLookups.taxes[0]) ? formLookups.taxes[0].rate : 0.18,
                    taxName: (formLookups && formLookups.taxes && formLookups.taxes[0]) ? formLookups.taxes[0].name : 'Standard Tax',
                    promised: (activeReq && activeReq.neededBy) ? activeReq.neededBy : new Date().toISOString().slice(0, 10),
                    description: l.description || (l.productName + ' — ' + (l.attribute || 'Standard')),
                    printDescription: ''
                };
            });

            poHeadValues = {
                vendorId: lines[0].vendorId || 0,
                vendorName: lines[0].vendorName || lbl('VAS_StandardVendor', 'Standard Vendor'),
                docTypeId: (formLookups && formLookups.docTypes && formLookups.docTypes[0]) ? formLookups.docTypes[0].id : 0,
                warehouseId: (formLookups && formLookups.warehouses && formLookups.warehouses[0]) ? formLookups.warehouses[0].id : 0,
                paymentTermId: (formLookups && formLookups.paymentTerms && formLookups.paymentTerms[0]) ? formLookups.paymentTerms[0].id : 0,
                orderRef: 'REF/' + new Date().getFullYear().toString().slice(2) + '/' + (Math.floor(Math.random() * 800) + 100),
                poDate: new Date().toISOString().slice(0, 10),
                promisedDate: (activeReq && activeReq.neededBy) ? activeReq.neededBy : new Date().toISOString().slice(0, 10),
                priority: 'Normal',
                description: lbl('VAS_PurchaseAgainst', 'Purchase against') + ' ' + (activeReq ? activeReq.requisitionNumber : ''),
                printDescription: ''
            };

            renderPoDetailsPage();
        }

        function computePoTotals() {
            var sub = 0;
            var tax = 0;
            var qty = 0;

            selectedPoLines.forEach(function (l) {
                var lineAmt = (l.orderQty || 0) * (l.rate || 0);
                sub += lineAmt;
                tax += lineAmt * (l.taxRate || 0);
                qty += (l.orderQty || 0);
            });

            return {
                sub: sub,
                tax: tax,
                total: sub + tax,
                qty: qty
            };
        }

        function renderPoDetailsPage() {
            var totals = computePoTotals();
            var star = '<span class="vas-204-req-star">*</span>';

            var docTypeOpts = (formLookups && formLookups.docTypes && formLookups.docTypes.length)
                ? formLookups.docTypes.map(function (d) {
                    return '<option value="' + d.id + '"' + (d.id === poHeadValues.docTypeId ? ' selected' : '') + '>' + escapeHtml(d.name) + '</option>';
                }).join('')
                : '<option value="0">' + escapeHtml(lbl('VAS_PurchaseOrder', 'Purchase Order')) + '</option>';

            var whOpts = (formLookups && formLookups.warehouses && formLookups.warehouses.length)
                ? formLookups.warehouses.map(function (w) {
                    return '<option value="' + w.id + '"' + (w.id === poHeadValues.warehouseId ? ' selected' : '') + '>' + escapeHtml(w.name) + '</option>';
                }).join('')
                : '<option value="0">' + escapeHtml(lbl('VAS_MainWarehouse', 'Main Warehouse')) + '</option>';

            var termOpts = (formLookups && formLookups.paymentTerms && formLookups.paymentTerms.length)
                ? formLookups.paymentTerms.map(function (pt) {
                    return '<option value="' + pt.id + '"' + (pt.id === poHeadValues.paymentTermId ? ' selected' : '') + '>' + escapeHtml(pt.name) + '</option>';
                }).join('')
                : '<option value="0">' + escapeHtml(lbl('VAS_Net30Days', '30 days net')) + '</option>';

            var summaryHtml =
                '<div class="vas-204-posum">' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Vendor', 'Vendor')) + '</div><div class="v">' + escapeHtml(poHeadValues.vendorName) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_SourceRequisition', 'Source requisition')) + '</div><div class="v">' + escapeHtml(activeReq ? activeReq.requisitionNumber : '') + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Lines', 'Lines')) + '</div><div class="v">' + fmtNum(selectedPoLines.length) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_OrderQty', 'Order qty')) + '</div><div class="v">' + fmtNum(totals.qty) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_OrderValue', 'Order value')) + '</div><div class="v">' + fmtMoney(totals.sub) + '</div></div>' +
                '</div>';

            var priorityOpts =
                '<option value="Normal">' + escapeHtml(lbl('VAS_PriorityNormal', 'Normal')) + '</option>' +
                '<option value="High">' + escapeHtml(lbl('VAS_PriorityHigh', 'High')) + '</option>' +
                '<option value="Urgent">' + escapeHtml(lbl('VAS_PriorityUrgent', 'Urgent')) + '</option>' +
                '<option value="Low">' + escapeHtml(lbl('VAS_PriorityLow', 'Low')) + '</option>';

            var formHtml =
                '<div class="vas-204-formwrap">' +
                '  <div class="vas-204-formsec">' + escapeHtml(lbl('VAS_Document', 'Document')) + '</div>' +
                '  <div class="vas-204-form-grid">' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_TargetDocType', 'Target document type')) + star + '</label><select class="vas-204-fctl" id="vas204_fDocType">' + docTypeOpts + '</select></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_OrderReference', 'Order reference')) + '</label><input class="vas-204-fctl" id="vas204_fOrderRef" value="' + escapeHtml(poHeadValues.orderRef) + '"></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_PODate', 'PO date')) + star + '</label><input class="vas-204-fctl" type="date" id="vas204_fPoDate" value="' + escapeHtml(poHeadValues.poDate) + '"></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_DatePromised', 'Date promised')) + star + '</label><input class="vas-204-fctl" type="date" id="vas204_fPromised" value="' + escapeHtml(poHeadValues.promisedDate) + '"></div>' +
                '  </div>' +
                '  <div class="vas-204-formsec">' + escapeHtml(lbl('VAS_VendorAndPayment', 'Vendor and payment')) + '</div>' +
                '  <div class="vas-204-form-grid">' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_Vendor', 'Vendor')) + star + '</label><input class="vas-204-fctl" value="' + escapeHtml(poHeadValues.vendorName) + '" disabled></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_PaymentTerm', 'Payment term')) + star + '</label><select class="vas-204-fctl" id="vas204_fTerm">' + termOpts + '</select></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_Warehouse', 'Warehouse')) + star + '</label><select class="vas-204-fctl" id="vas204_fWh">' + whOpts + '</select></div>' +
                '    <div class="vas-204-field"><label>' + escapeHtml(lbl('VAS_Priority', 'Priority')) + '</label><select class="vas-204-fctl" id="vas204_fPriority">' + priorityOpts + '</select></div>' +
                '  </div>' +
                '  <div class="vas-204-formsec">' + escapeHtml(lbl('VAS_Description', 'Description')) + '</div>' +
                '  <div class="vas-204-form-grid">' +
                '    <div class="vas-204-field span-2"><label>' + escapeHtml(lbl('VAS_Description', 'Description')) + '</label><input class="vas-204-fctl" id="vas204_fDesc" value="' + escapeHtml(poHeadValues.description) + '"></div>' +
                '    <div class="vas-204-field span-2"><label>' + escapeHtml(lbl('VAS_PrintDescription', 'Print description')) + '</label><input class="vas-204-fctl" id="vas204_fPrintDesc" placeholder="' + escapeHtml(lbl('VAS_PrintDescPlaceholder', 'Text printed on the vendor copy')) + '" value="' + escapeHtml(poHeadValues.printDescription) + '"></div>' +
                '  </div>' +
                '</div>';

            openModal({
                child: true,
                bodyClass: 'compact',
                size: 'md',
                reopen: renderPoDetailsPage,
                title: lbl('VAS_NewPODetailsHeader', 'New Purchase Order · Details'),
                subtitle: lbl('VAS_Page1Of2', 'Page 1 of 2') + ' · ' + poHeadValues.vendorName + ' · ' + lbl('VAS_From', 'from') + ' ' + (activeReq ? activeReq.requisitionNumber : ''),
                body: summaryHtml + formHtml,
                foot: '<span class="vas-204-foot-note"><span class="vas-204-req-star">*</span> ' + escapeHtml(lbl('VAS_RequiredNextPage', 'required · line details on the next page')) + '</span>' +
                    '<span><button type="button" class="vas-204-btn vas-204-btn-back">' + escapeHtml(lbl('VAS_Back', 'Back')) + '</button> ' +
                    '<button type="button" class="vas-204-btn vas-204-btn-primary vas-204-to-lines">' + escapeHtml(lbl('VAS_ContinueToLines', 'Continue to lines')) + '</button></span>',
                after: function () {
                    $mFoot.find('.vas-204-btn-back').on('click', function () { backModal(); });
                    $mFoot.find('.vas-204-to-lines').on('click', function () {
                        captureHeadValues();
                        renderPoLinesPage();
                    });
                }
            });
        }

        function captureHeadValues() {
            poHeadValues.docTypeId = +($('#vas204_fDocType').val() || 0);
            poHeadValues.orderRef = $('#vas204_fOrderRef').val() || '';
            poHeadValues.poDate = $('#vas204_fPoDate').val() || '';
            poHeadValues.promisedDate = $('#vas204_fPromised').val() || '';
            poHeadValues.warehouseId = +($('#vas204_fWh').val() || 0);
            poHeadValues.paymentTermId = +($('#vas204_fTerm').val() || 0);
            poHeadValues.priority = $('#vas204_fPriority').val() || 'Normal';
            poHeadValues.description = $('#vas204_fDesc').val() || '';
            poHeadValues.printDescription = $('#vas204_fPrintDesc').val() || '';
        }

        /* ============================================================
           STEP 4: PO LINES & CONFIRMATION (PAGE 2 OF 2)
           ============================================================ */

        function renderPoLinesPage() {
            var totals = computePoTotals();

            var poLineCols = [
                { label: lbl('VAS_Product', 'Product'), w: 1.45, cls: 'c-dark' },
                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.35 },
                { label: lbl('VAS_UoM', 'UoM'), w: 0.75 },
                { label: lbl('VAS_Qty', 'Qty'), w: 0.6, align: 'right' },
                { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                { label: lbl('VAS_Amount', 'Amount'), w: 0.85, align: 'right', cls: 'c-emph' },
                { label: lbl('VAS_DatePromised', 'Date promised'), w: 1.05 }
            ];

            var summaryHtml =
                '<div class="vas-204-posum">' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_Vendor', 'Vendor')) + '</div><div class="v">' + escapeHtml(poHeadValues.vendorName) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_PODate', 'PO date')) + '</div><div class="v">' + escapeHtml(poHeadValues.poDate) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_DatePromised', 'Date promised')) + '</div><div class="v">' + escapeHtml(poHeadValues.promisedDate) + '</div></div>' +
                '  <div><div class="l">' + escapeHtml(lbl('VAS_OrderTotal', 'Order total')) + '</div><div class="v">' + fmtMoney(totals.total) + '</div></div>' +
                '</div>';

            var linesTableHtml = pagedTable(poLineCols, selectedPoLines, {
                label: lbl('VAS_LinesSummary', 'purchase order lines'),
                render: function (l, i) {
                    return '<span class="vas-204-cell c-dark" title="' + escapeHtml(l.productName) + '">' + escapeHtml(l.productName) + '</span>' +
                        '<span class="vas-204-cell c-std" title="' + escapeHtml(l.attribute || 'Standard') + '">' + escapeHtml(l.attribute || 'Standard') + '</span>' +
                        '<span class="vas-204-cell c-std">' + escapeHtml(l.uom) + '</span>' +
                        '<span class="vas-204-cell right c-dark">' + fmtNum(l.orderQty) + '</span>' +
                        '<span class="vas-204-cell right c-std">' + fmtMoney(l.rate) + '</span>' +
                        '<span class="vas-204-cell right c-emph">' + fmtMoney(l.orderQty * l.rate) + '</span>' +
                        '<span class="vas-204-cell"><input class="vas-204-rowin ldate" type="date" value="' + escapeHtml(l.promised || poHeadValues.promisedDate) + '" data-i="' + i + '" aria-label="Date promised"></span>';
                }
            });

            var totalsBarHtml =
                '<div class="vas-204-totline">' +
                '  <span>' + escapeHtml(lbl('VAS_Subtotal', 'Subtotal')) + ': <b>' + fmtMoney(totals.sub) + '</b></span>' +
                '  <span>' + escapeHtml(lbl('VAS_Tax', 'Tax')) + ': <b>' + fmtMoney(totals.tax) + '</b></span>' +
                '  <span>' + escapeHtml(lbl('VAS_OrderTotal', 'Order total')) + ': <b>' + fmtMoney(totals.total) + '</b></span>' +
                '</div>';

            openModal({
                child: true,
                bodyClass: 'compact',
                reopen: renderPoLinesPage,
                title: lbl('VAS_NewPOLinesHeader', 'New Purchase Order · Lines'),
                subtitle: lbl('VAS_Page2Of2', 'Page 2 of 2') + ' · ' + poHeadValues.vendorName + ' · ' + selectedPoLines.length + ' ' + lbl('VAS_Lines', 'lines'),
                body: summaryHtml +
                    '<div class="vas-204-msec">' + escapeHtml(lbl('VAS_ConfirmLinesHeader', 'Purchase order lines')) + '</div>' +
                    linesTableHtml +
                    totalsBarHtml,
                foot: '<span class="vas-204-foot-note" id="vas204_poFootNote">' +
                    selectedPoLines.length + ' ' + lbl('VAS_Lines', 'lines') + ' · ' + fmtNum(totals.qty) + ' ' + lbl('VAS_Qty', 'qty') + ' · ' +
                    lbl('VAS_OrderTotal', 'order total') + ' ' + fmtMoney(totals.total) +
                    '</span>' +
                    '<span><button type="button" class="vas-204-btn vas-204-btn-back">' + escapeHtml(lbl('VAS_BackToDetails', 'Back to details')) + '</button> ' +
                    '<button type="button" class="vas-204-btn vas-204-btn-primary vas-204-btn-create-po">' + escapeHtml(lbl('VAS_CreatePO', 'Create PO')) + '</button></span>',
                after: function () {
                    $mFoot.find('.vas-204-btn-back').on('click', function () { backModal(); });
                    $mFoot.find('.vas-204-btn-create-po').on('click', function () {
                        executeCreatePO();
                    });
                }
            });
        }

        function executeCreatePO() {
            if (isBusy) { return; }
            isBusy = true;

            var $createBtn = $mFoot.find('.vas-204-btn-create-po');
            $createBtn.prop('disabled', true).text(lbl('Saving', 'Saving...'));

            var linesPayload = selectedPoLines.map(function (l) {
                return {
                    requisitionLineId: l.requisitionLineId || 0,
                    productId: l.productId || 0,
                    qty: l.orderQty || 0,
                    rate: l.rate || 0,
                    taxId: l.taxId || 0,
                    description: l.description || ''
                };
            });

            var payload = {
                requisitionId: activeReq ? activeReq.requisitionId : 0,
                vendorId: poHeadValues.vendorId || 0,
                warehouseId: poHeadValues.warehouseId || 0,
                docTypeId: poHeadValues.docTypeId || 0,
                paymentTermId: poHeadValues.paymentTermId || 0,
                dateOrdered: poHeadValues.poDate,
                datePromised: poHeadValues.promisedDate,
                description: poHeadValues.description,
                linesJson: JSON.stringify(linesPayload)
            };

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/CreatePurchaseOrder',
                type: 'POST',
                data: payload,
                cache: false,
                success: function (response) {
                    isBusy = false;
                    var res = parseResponse(response);
                    if (res && res.success) {
                        closeModal();
                        toast(lbl('VAS_POCreatedSuccess', 'Purchase order created successfully.') + ' (' + (res.documentNo || '') + ')');
                        if (res.orderId > 0) {
                            openPurchaseOrderRecord(res.orderId);
                        }
                    } else {
                        $createBtn.prop('disabled', false).text(lbl('VAS_CreatePO', 'Create PO'));
                        VIS.ADialog.error('', true, (res && (res.error || res.message)) || lbl('VAS_ErrorCreatingPO', 'Error creating Purchase Order'));
                    }
                },
                error: function (xhr, status, error) {
                    isBusy = false;
                    $createBtn.prop('disabled', false).text(lbl('VAS_CreatePO', 'Create PO'));
                    VIS.ADialog.error('', true, error || lbl('VAS_ServerTimeoutError', 'Server error creating Purchase Order'));
                }
            });
        }

        /* ============================================================
           RECORD NAVIGATION
           ============================================================ */

        function openPurchaseOrderRecord(orderId) {
            if (orderId <= 0) { return; }
            if (poWindowId > 0) {
                var query = new VIS.Query("C_Order");
                query.addRestriction("C_Order_ID", VIS.Query.prototype.EQUAL, orderId);
                if (VIS.viewManager && VIS.viewManager.startWindow) {
                    VIS.viewManager.startWindow(poWindowId, query);
                } else if (VIS.AEnv && VIS.AEnv.startWindow) {
                    VIS.AEnv.startWindow(poWindowId, query);
                }
            }
        }

        function openDirectNewPORecord() {
            closeModal();
            try {
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
            } catch (e) {
                if (poWindowId > 0) {
                    var query = new VIS.Query("C_Order");
                    query.addRestriction(VIS.Query.prototype.NEWRECORD);
                    query.newRecord = true;
                    if (VIS.viewManager && VIS.viewManager.startWindow) {
                        VIS.viewManager.startWindow(poWindowId, query);
                    } else if (VIS.AEnv && VIS.AEnv.startWindow) {
                        VIS.AEnv.startWindow(poWindowId, query);
                    }
                }
            }
        }

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            if ($mask) {
                $mask.remove();
                $mask = null;
            }
            $(document).off('keydown.vas204');
            $(window).off('resize.vas204');
            $root.remove();
        };
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.refreshWidget = function () { };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
