/**
 * VAS.RequisitionWidgetUtil
 * Shared helpers of the Requisition screen widgets VAS_305 - VAS_315 (window VAS_Requisition)
 * and their Home copies: labels with fallback, formatting, ajax, adaptive rows + pager, and the
 * requisition popup (Onfinity panel: hero card, detail card, lines with PO / GRN / transfer).
 *
 * Each widget loads its own data from its own controller; nothing here keeps state shared
 * between widgets except the single popup element, so no widget can affect another one.
 *
 * Opening a requisition from a widget:
 *   - on the window (windowNo >= 0) through the widget framework value change (TabWhereClause),
 *   - on Home through VAS.ZoomUtil (window VAS_Requisition, old name Requisition).
 *
 * Labels / Message Keys (popup, shared): VAS_305_Requisition, VAS_305_OpenRequisition, VAS_305_Close,
 * VAS_305_Back, VAS_305_Lines, VAS_305_RequestedBy, VAS_305_Warehouse, VAS_305_SourceWarehouse,
 * VAS_305_Priority, VAS_305_RequisitionDate, VAS_305_EarliestNeedBy, VAS_305_Product, VAS_305_Category,
 * VAS_305_Qty, VAS_305_NeedBy, VAS_305_Amount, VAS_305_Vendor, VAS_305_Ordered, VAS_305_Received,
 * VAS_305_OrderedReceived, VAS_305_Status, VAS_305_Documents, VAS_305_Total, VAS_305_StillOpen, VAS_305_RaisedBy, VAS_305_LinesCount,
 * VAS_305_FullyReceived, VAS_305_Late, VAS_305_DueToday, VAS_305_DueIn, VAS_305_OnTime, VAS_305_NotOrdered,
 * VAS_305_PartOrdered, VAS_305_OrderedState, VAS_305_ReceivedState, VAS_305_DaysAgo, VAS_305_InDays,
 * VAS_305_Today, VAS_305_NoVendor, VAS_305_Loading, VAS_305_LoadError, VAS_305_NoRecord, VAS_305_NothingHere,
 * VAS_305_Showing, VAS_305_Of, VAS_305_Requester, VAS_305_Value, VAS_305_PrevPage, VAS_305_NextPage,
 * VAS163 2026-11-13: VAS_305_Days, VAS_305_Attribute, VAS_305_UOM, VAS_305_NoLines, VAS_305_DocPO, VAS_305_DocBPO,
 * VAS_305_DocMT, VAS_305_DocIU, VAS_305_DocRFQ
 */
; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    var U = VAS.RequisitionWidgetUtil = VAS.RequisitionWidgetUtil || {};

    /* ---------- labels & formatting ---------- */

    /** Message text, or the English fallback when the key is not defined. */
    U.lbl = function (key, fallback) {
        var t = (window.VIS && VIS.Msg) ? VIS.Msg.getMsg(key) : null;
        if (!t || t === key || t === '[' + key + ']') { return fallback; }
        return t;
    };

    U.esc = function (v) {
        return String(v == null ? '' : v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    };

    U.parse = function (res) {
        var d = res;
        try {
            if (typeof d === 'string') { d = JSON.parse(d); }
            if (typeof d === 'string') { d = JSON.parse(d); }
        } catch (e) { d = null; }
        return d || {};
    };

    function locale() { return window.navigator.language || 'en-US'; }

    /** Same compact money style as the other VAS widgets (L / Cr). */
    U.money = function (value, cur) {
        var v = Number(value || 0);
        var sym = (cur && cur.Symbol) ? cur.Symbol : '';
        var pre = sym ? sym + ' ' : '';
        var a = Math.abs(v);
        if (a >= 10000000) { return pre + (v / 10000000).toFixed(2) + ' Cr'; }
        if (a >= 100000) { return pre + (v / 100000).toFixed(2) + ' L'; }
        return pre + Math.round(v).toLocaleString(locale());
    };

    /** Full amount with grouping (popup tables). */
    U.amount = function (value, cur) {
        var p = (cur && cur.Precision != null) ? Math.min(Number(cur.Precision), 2) : 2;
        return Number(value || 0).toLocaleString(locale(), { minimumFractionDigits: 0, maximumFractionDigits: p });
    };

    U.num = function (value) {
        return Number(value || 0).toLocaleString(locale(), { maximumFractionDigits: 3 });
    };

    function toDate(iso) {
        if (!iso) { return null; }
        var p = String(iso).substring(0, 10).split('-');
        if (p.length !== 3) { return null; }
        return new Date(Number(p[0]), Number(p[1]) - 1, Number(p[2]));
    }
    U.toDate = toDate;

    U.date = function (iso) {
        var d = toDate(iso);
        return d ? d.toLocaleDateString(locale(), { day: '2-digit', month: 'short' }) : '—';
    };

    U.dateLong = function (iso) {
        var d = toDate(iso);
        return d ? d.toLocaleDateString(locale(), { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
    };

    U.todayIso = function () {
        var d = new Date();
        return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2);
    };

    /** Whole days from today to the date (negative = past). */
    U.daysFromToday = function (iso) {
        var d = toDate(iso);
        if (!d) { return null; }
        var t = toDate(U.todayIso());
        return Math.round((d - t) / 864e5);
    };

    /* ---------- chips ---------- */

    U.chip = function (cls, text) {
        return '<span class="vas-rqw-chip ' + cls + '">' + U.esc(text) + '</span>';
    };

    /** Need-by chip for a quantity still to come. */
    U.needChip = function (iso) {
        var d = U.daysFromToday(iso);
        if (d == null) { return ''; }
        // VAS163 2026-11-13: row 1127 - full day text ('Late 63 days', 'Due in 1 day') instead of '63d'
        if (d < 0) { return U.chip('r', U.lbl('VAS_305_Late', 'Late') + ' ' + (-d) + ' ' + U.unit(-d, 'VAS_305_Days', 'days')); }
        if (d === 0) { return U.chip('a', U.lbl('VAS_305_DueToday', 'Due today')); }
        if (d <= 2) { return U.chip('a', U.lbl('VAS_305_DueIn', 'Due in') + ' ' + d + ' ' + U.unit(d, 'VAS_305_Days', 'days')); }
        return U.chip('b', U.lbl('VAS_305_OnTime', 'On time'));
    };

    /** Line state chip (received / notordered / partordered / ordered / late). */
    /** Count unit: singular label when n is 1 (plural label otherwise). */
    var SINGULAR = {
        lines: function () { return U.lbl('VAS_305_Line1', 'line'); },
        reqs: function () { return U.lbl('VAS_305_Req1', 'req'); },
        requisitions: function () { return U.lbl('VAS_305_Requisition1', 'requisition'); },
        results: function () { return U.lbl('VAS_305_Result1', 'result'); },
        categories: function () { return U.lbl('VAS_305_Category1', 'category'); },
        days: function () { return U.lbl('VAS_305_Day1', 'day'); }
    };
    U.unit = function (n, key, fallback) {
        if (Number(n) === 1 && SINGULAR[fallback]) { return SINGULAR[fallback](); }
        return U.lbl(key, fallback);
    };
    /**
     * Calls fn once el has left the page. Closing a window removes its widgets without calling dispose(), so
     * widgets that put elements on <body> (search list, tooltip) or bind document events clean up through this.
     * Two checks in a row (3 s apart) must find it gone, so a short detach while a dashboard is rearranged is ignored.
     * Returns a function that stops the watch.
     */
    U.whenRemoved = function (el, fn) {
        var misses = 0;
        var t = window.setInterval(function () {
            misses = el.isConnected ? 0 : misses + 1;
            if (misses >= 2) { window.clearInterval(t); fn(); }
        }, 3000);
        return function () { window.clearInterval(t); };
    };

    /** Plural phrase label; when n is 1 the singular builder is used instead (built from short labels). */
    U.phrase = function (n, key, fallback, singular) {
        return Number(n) === 1 && singular ? singular() : U.lbl(key, fallback);
    };

    U.stateChip = function (state, needBy) {
        switch (state) {
            case 'received': return U.chip('g', U.lbl('VAS_305_ReceivedState', 'Received'));
            case 'ordered': return U.chip('a', U.lbl('VAS_305_OrderedState', 'Ordered'));
            case 'partreceived': return U.chip('a', U.lbl('VAS_305_PartReceived', 'Part received'));
            case 'partordered': return U.chip('a', U.lbl('VAS_305_PartOrdered', 'Part ordered'));
            case 'late': return U.needChip(needBy) || U.chip('r', U.lbl('VAS_305_Late', 'Late'));
            default: return U.chip('b', U.lbl('VAS_305_NotOrdered', 'Not ordered'));
        }
    };

    /** Requisition status chip from the bucket code (OP/PO/OR/PD/DE/DR/CL). */
    U.statusChip = function (code, name) {
        var cls = { OP: 'b', PO: 'a', OR: 'a', PD: 'a', DE: 'g', DR: 'p', CL: 'n' }[code] || 'n';
        return U.chip(cls, name || code);
    };

    /* ---------- icons ---------- */

    var I = 'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
    U.icon = {
        req: '<svg ' + I + '><path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2"/><rect x="9" y="3" width="6" height="4" rx="1"/><path d="M9 12h6M9 16h4"/></svg>',
        cart: '<svg ' + I + '><circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2.5 3h3l2.6 12.4a1.5 1.5 0 0 0 1.5 1.1h8.6a1.5 1.5 0 0 0 1.5-1.2L21 8H6.3"/></svg>',
        user: '<svg ' + I + '><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>',
        cal: '<svg ' + I + '><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 10h18"/></svg>',
        pie: '<svg ' + I + '><path d="M21 12A9 9 0 1 1 12 3v9z"/><path d="M15 3.5A9 9 0 0 1 20.5 9H15z"/></svg>',
        grid: '<svg ' + I + '><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/></svg>',
        search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>',
        plus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
        prev: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="m15 18-6-6 6-6"/></svg>',
        next: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="m9 18 6-6-6-6"/></svg>',
        close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>'
    };

    /* ---------- ajax ---------- */

    /** GET a widget endpoint; resolves with parsed data, rejects with an error text. */
    U.get = function (url, data) {
        var dfd = $.Deferred();
        $.ajax({
            url: VIS.Application.contextUrl + url,
            type: 'GET',
            data: data || {},
            cache: false,
            success: function (res) {
                var d = U.parse(res);
                if (d && d.error) { dfd.reject(d.error); return; }
                dfd.resolve(d);
            },
            error: function () { dfd.reject(U.lbl('VAS_305_LoadError', 'Could not load the data.')); }
        });
        return dfd.promise();
    };

    /* ---------- dashboard width variable (header clamps) ---------- */

    U.ensureDashVar = function ($el) {
        if (window.__vasDashInlineSizeObserver || typeof ResizeObserver === 'undefined') { return; }
        var c = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!c) { return; }
        var write = function () { document.documentElement.style.setProperty('--dash-inline-size', c.clientWidth + 'px'); };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(c);
        write();
    };

    /* ---------- widget scaffolding ---------- */

    U.header = function (icon, title, subtitle, pillId) {
        return '<div class="vas-rqw-hd"><div class="vas-rqw-hl"><span class="vas-rqw-iw">' + icon + '</span><div style="min-width:0">' +
            '<div class="vas-rqw-tt" title="' + U.esc(title) + '">' + U.esc(title) + '</div>' +
            (subtitle ? '<div class="vas-rqw-st" title="' + U.esc(subtitle) + '">' + U.esc(subtitle) + '</div>' : '') +
            '</div></div>' + (pillId ? '<span class="vas-rqw-pill" data-pill="' + pillId + '"></span>' : '') + '</div>';
    };

    U.busy = function ($root, on) { $root.find('> .vas-rqw-card > .vas-rqw-busy, > .vas-rqw-busy').toggleClass('on', !!on); };

    /** Grid header row; right = array of column indexes aligned right. */
    U.gridHead = function (heads, right) {
        return '<div class="vas-rqw-gh">' + heads.map(function (h, i) {
            return '<span class="' + (right && right.indexOf(i) >= 0 ? 'vas-rqw-r' : '') + '">' + U.esc(h) + '</span>';
        }).join('') + '</div>';
    };

    /**
     * Adaptive rows + footer pager (no inner scrollbar): measures one row, shows as many as fit
     * (minimum 3) and pages the rest. cfg: { $tb, $help, $pager, rows, head, cell(row) -> html,
     * onClick(row), state: {page}, helpText(a, b, n) }.
     */
    U.renderRows = function (cfg) {
        var rows = cfg.rows || [];
        var $tb = cfg.$tb;
        if (!rows.length) {
            $tb.html(cfg.head + '<div class="vas-rqw-msg">' + U.esc(cfg.emptyText || U.lbl('VAS_305_NothingHere', 'Nothing here')) + '</div>');
            cfg.$help.text('');
            cfg.$pager.empty();
            return;
        }
        $tb.html(cfg.head + '<div class="vas-rqw-gr">' + cfg.cell(rows[0]) + '</div>');
        var hH = $tb.children().first().outerHeight() || 0;
        var rH = $tb.children().last().outerHeight() || 30;
        var avail = $tb.innerHeight() - hH;
        var per = Math.max(3, Math.floor(avail / rH));
        var pages = Math.max(1, Math.ceil(rows.length / per));
        var st = cfg.state;
        st.page = Math.min(st.page || 0, pages - 1);
        var start = st.page * per;
        var slice = rows.slice(start, start + per);
        $tb.html(cfg.head + slice.map(function (r, i) {
            return '<div class="vas-rqw-gr" data-i="' + i + '">' + cfg.cell(r) + '</div>';
        }).join(''));
        $tb.off('click.rqw').on('click.rqw', '.vas-rqw-gr[data-i]', function () {
            cfg.onClick(slice[Number($(this).attr('data-i'))]);
        });
        var help = cfg.helpText ? cfg.helpText(start + 1, start + slice.length, rows.length)
            : U.lbl('VAS_305_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + U.lbl('VAS_305_Of', 'of') + ' ' + rows.length;
        cfg.$help.text(help).attr('title', help);
        if (pages <= 1) { cfg.$pager.empty(); return; }
        cfg.$pager.html('<button type="button" data-p="-1" aria-label="' + U.esc(U.lbl('VAS_305_PrevPage', 'Previous page')) + '"' + (st.page ? '' : ' disabled') + '>' + U.icon.prev + '</button>' +
            (st.page + 1) + ' ' + U.esc(U.lbl('VAS_305_Of', 'of')) + ' ' + pages +
            '<button type="button" data-p="1" aria-label="' + U.esc(U.lbl('VAS_305_NextPage', 'Next page')) + '"' + (st.page < pages - 1 ? '' : ' disabled') + '>' + U.icon.next + '</button>');
        cfg.$pager.find('button').off('click').on('click', function () {
            st.page += Number($(this).attr('data-p'));
            U.renderRows(cfg);
        });
    };

    /** Re-run fn when the element is resized (debounced); returns a disposer. */
    U.onResize = function (el, fn) {
        if (typeof ResizeObserver === 'undefined' || !el) { return function () { }; }
        var t = null, w = -1, h = -1;
        var ro = new ResizeObserver(function (en) {
            var r = en[0].contentRect;
            if (Math.abs(r.width - w) < 1 && Math.abs(r.height - h) < 1) { return; }
            w = r.width; h = r.height;
            if (t) { window.clearTimeout(t); }
            t = window.setTimeout(fn, 120);
        });
        ro.observe(el);
        return function () { ro.disconnect(); if (t) { window.clearTimeout(t); } };
    };

    /* ---------- open the requisition window ---------- */

    U.openRequisition = function (widget, requisitionId) {
        if (!requisitionId) { return; }
        U.closeModal();
        if (widget && widget.windowNo >= 0 && widget.listener && typeof widget.listener.widgetFirevalueChanged === 'function') {
            widget.listener.widgetFirevalueChanged({
                "TabWhereClause": "M_Requisition.M_Requisition_ID=" + requisitionId,
                "TabLayout": "Y",
                "TabIndex": "0"
            });
            return;
        }
        if (VAS.ZoomUtil) {
            VAS.ZoomUtil.zoomToRecord("M_Requisition_ID", requisitionId, U._reqWindowId || 0, "VAS_Requisition", "Requisition")
                .done(function (id) { if (id > 0) { U._reqWindowId = id; } });
        }
    };

    /* ---------- popup engine (Onfinity panel) ---------- */

    var $ov = null, stack = [], current = null;

    function ensureShell() {
        if ($ov) { return; }
        $ov = $('<div class="vas-rqw-ov" role="dialog" aria-modal="true">' +
            '<div class="vas-rqw-panel">' +
            '<div class="vas-rqw-ph"><div class="l">' +
            '<button type="button" class="vas-rqw-icn" data-act="back" hidden aria-label="' + U.esc(U.lbl('VAS_305_Back', 'Back')) + '">' + U.icon.prev + '</button>' +
            U.icon.req + '<span class="ttl"></span></div>' +
            '<button type="button" class="vas-rqw-icn" data-act="close" aria-label="' + U.esc(U.lbl('VAS_305_Close', 'Close')) + '">' + U.icon.close + '</button></div>' +
            '<div class="vas-rqw-pb"></div><div class="vas-rqw-pf"></div></div></div>');
        $('body').append($ov);
        $ov.on('click', function (e) {
            // VAS163 2026-11-13: row 1121 - a click on the dark backdrop no longer closes the popup (close icon / Close button only)
            if (e.target === $ov[0]) { return; }
            var act = $(e.target).closest('[data-act]').attr('data-act');
            if (act === 'close') { U.closeModal(); }
            else if (act === 'back') { current = stack.pop(); paint(); }
        });
        $(document).on('keydown.vasrqw', function (e) {
            if ((e.key === 'Escape' || e.keyCode === 27) && $ov) { U.closeModal(); }
        });
    }

    function paint() {
        $ov.find('.ttl').text(current.title);
        var $b = $ov.find('.vas-rqw-pb').html(current.body);
        $b.scrollTop(0);
        $ov.find('.vas-rqw-pf').html((current.foot || '') +
            '<button type="button" class="vas-rqw-btn" data-act="close">' + U.esc(U.lbl('VAS_305_Close', 'Close')) + '</button>');
        $ov.find('[data-act="back"]').prop('hidden', !stack.length);
        if (current.after) { current.after($ov); }
        $ov.find('[data-act="close"]').last().trigger('focus');
    }

    /** Opens a view {title, body, foot, after($ov)}; from an open popup it is pushed (Back returns). */
    U.openModal = function (view) {
        if ($ov && current) { stack.push(current); } else { stack = []; }
        ensureShell();
        current = view;
        paint();
    };

    U.closeModal = function () {
        if ($ov) {
            $(document).off('keydown.vasrqw');
            $ov.remove();
        }
        $ov = null; stack = []; current = null;
    };

    function table(cols, right, heads, rowsHtml) {
        return '<div class="vas-rqw-tb" style="--vas-rqw-cols:' + cols + '">' + U.gridHead(heads, right) + rowsHtml.join('') + '</div>';
    }
    U.table = table;

    /** Popup list of requisitions (KPI, donut slice, ...); a row opens the requisition. */
    U.showList = function (widget, title, subtitle, records, cur) {
        records = records || [];
        var rows = records.map(function (r) {
            return '<div class="vas-rqw-gr" data-r="' + r.requisitionId + '"><span class="b">' + U.esc(r.documentNo) + '</span>' +
                '<span>' + U.esc(r.requester || '') + '</span><span class="m">' + U.esc(r.warehouse || '') + '</span>' +
                '<span class="vas-rqw-r">' + U.date(r.needBy) + '</span><span class="vas-rqw-r">' + U.amount(r.amount, cur) + '</span>' +
                '<span>' + (r.isLate ? U.needChip(r.needBy) : U.statusChip(r.status, r.statusName)) + '</span></div>';
        });
        var body = '<div class="vas-rqw-sh"><b>' + U.esc(title) + '</b><span>' + U.esc(subtitle || '') + '</span></div>' +
            (rows.length ? table('minmax(0,1fr) minmax(0,1.3fr) minmax(0,1.3fr) 5em 6.5em 8em', [3, 4],
                [U.lbl('VAS_305_Requisition', 'Requisition'), U.lbl('VAS_305_Requester', 'Requested by'), U.lbl('VAS_305_Warehouse', 'Warehouse'),
                U.lbl('VAS_305_NeedBy', 'Need by'), U.lbl('VAS_305_Value', 'Value') + (cur && cur.Symbol ? ' ' + cur.Symbol : ''), U.lbl('VAS_305_Status', 'Status')], rows)
                : '<div class="vas-rqw-msg">' + U.esc(U.lbl('VAS_305_NothingHere', 'Nothing here')) + '</div>');
        U.openModal({
            title: title,
            body: body,
            after: function ($m) {
                $m.find('.vas-rqw-pb').off('click.rqwl').on('click.rqwl', '.vas-rqw-gr[data-r]', function () {
                    U.showRequisition(widget, Number($(this).attr('data-r')));
                });
            }
        });
    };

    /** Popup of one requisition: header, detail card, lines with their PO / GRN / transfer documents. */
    U.showRequisition = function (widget, requisitionId) {
        if (!requisitionId) { return; }
        U.openModal({
            title: U.lbl('VAS_305_Requisition', 'Requisition'),
            body: '<div class="vas-rqw-msg">' + U.esc(U.lbl('VAS_305_Loading', 'Loading…')) + '</div>'
        });
        var token = current;
        U.get('VAS_RequisitionWidgetCommon/GetRequisitionDetail', { requisitionId: requisitionId })
            .done(function (d) {
                if (current !== token) { return; }
                current = detailView(widget, d);
                paint();
            })
            .fail(function (err) {
                if (current !== token) { return; }
                current = { title: U.lbl('VAS_305_Requisition', 'Requisition'), body: '<div class="vas-rqw-msg err">' + U.esc(err) + '</div>' };
                paint();
            });
    };

    function detailView(widget, d) {
        var h = d.header || {}, cur = d.currency, lines = d.lines || [];
        var tone = h.isLate || h.docStatus === 'VO' ? 'risk' : (h.statusCode === 'DE' ? 'ok' : (h.statusCode === 'PO' || h.statusCode === 'PD' || h.statusCode === 'OR') ? 'warn' : '');
        var pillTxt = (h.docStatus === 'CO' ? h.statusName : h.docStatusName) + (h.isLate ? ' · ' + U.lbl('VAS_305_Late', 'Late') : '');
        var ed = U.daysFromToday(h.earliestNeedBy);
        // VAS163 2026-11-13: row 1127 - 'In 5 days' instead of 'In 5d'
        var edTxt = ed == null ? '' : ed < 0 ? (-ed) + ' ' + U.lbl('VAS_305_DaysAgo', 'days ago') : ed === 0 ? U.lbl('VAS_305_Today', 'Today') : U.lbl('VAS_305_InDays', 'In') + ' ' + ed + ' ' + U.unit(ed, 'VAS_305_Days', 'days');
        var mc = function (l, v, cls, meta) {
            return '<div class="vas-rqw-mc"><div class="ml">' + U.esc(l) + '</div><div class="mv ' + (cls || '') + '" title="' + U.esc(v) + '">' + U.esc(v || '—') + '</div>' +
                (meta ? '<div class="mm">' + U.esc(meta) + '</div>' : '') + '</div>';
        };
        // VAS163 2026-11-13: row 1142 - document types raised from a requisition line (PO, blanket PO, material
        // transfer, inventory use, RFQ); the chip keeps the short code, its hover title the full type name.
        var DOC_TYPES = {
            PO: function () { return U.lbl('VAS_305_DocPO', 'Purchase order'); },
            BPO: function () { return U.lbl('VAS_305_DocBPO', 'Blanket purchase order'); },
            MT: function () { return U.lbl('VAS_305_DocMT', 'Material transfer'); },
            IU: function () { return U.lbl('VAS_305_DocIU', 'Inventory use'); },
            RFQ: function () { return U.lbl('VAS_305_DocRFQ', 'RFQ'); }
        };
        var docOf = function (x) {
            var done = x.type === 'PO' ? (x.receivedQty >= x.qty) : (x.docStatus === 'CO' || x.docStatus === 'CL');
            var full = (DOC_TYPES[x.type] ? DOC_TYPES[x.type]() : x.type) + ' ' + x.documentNo;
            return { html: '<span class="vas-rqw-chip ' + (done ? 'g' : 'n') + '" title="' + U.esc(full) + '">' + U.esc((x.type || '') + ' ' + x.documentNo) + '</span>', text: full };
        };
        var textOf = function (html) { return $('<div>').html(html).text(); };
        var cell = function (cls, html, title) {
            return '<span' + (cls ? ' class="' + cls + '"' : '') + ' title="' + U.esc(title == null ? textOf(html) : title) + '">' + html + '</span>';
        };
        // VAS163 2026-11-13: row 1134 - 'Ord. / Rcvd.' and 'Documents' only when at least one line has a value
        var showOrd = lines.some(function (l) { return Number(l.orderedQty || 0) !== 0 || Number(l.receivedQty || 0) !== 0; });
        var showDocs = lines.some(function (l) { return (l.documents || []).length > 0; });
        // VAS163 2026-11-13: rows 1129 / 1130 / 1131 - separate UOM column (qty without unit), Attribute instead of
        // Category (blank when the line has no attribute set instance), and a hover title on every value cell.
        var cols = [
            { w: 'minmax(0,1.7fr)', head: U.lbl('VAS_305_Product', 'Product'), cell: function (l) { return cell('b', U.esc(l.product), l.product); } },
            { w: 'minmax(0,1fr)', head: U.lbl('VAS_305_Attribute', 'Attribute'), cell: function (l) { return cell('m', U.esc(l.attribute || ''), l.attribute || ''); } },
            { w: '4.4em', right: true, head: U.lbl('VAS_305_Qty', 'Qty'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.num(l.qty))); } },
            { w: 'minmax(3.5em,0.6fr)', head: U.lbl('VAS_305_UOM', 'UOM'), cell: function (l) { return cell('m', U.esc(l.uom || ''), l.uom || ''); } },
            { w: '4.6em', right: true, head: U.lbl('VAS_305_NeedBy', 'Need by'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.date(l.needBy)), U.dateLong(l.needBy)); } },
            { w: '5.6em', right: true, head: U.lbl('VAS_305_Amount', 'Amount') + (cur && cur.Symbol ? ' ' + cur.Symbol : ''), cell: function (l) { return cell('vas-rqw-r', U.esc(U.amount(l.amount, cur))); } }
        ];
        if (showOrd) {
            cols.push({ w: '6em', right: true, head: U.lbl('VAS_305_OrderedReceived', 'Ord. / Rcvd.'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.num(l.orderedQty) + ' / ' + U.num(l.receivedQty))); } });
        }
        if (showDocs) {
            cols.push({
                w: 'minmax(0,1.4fr)', head: U.lbl('VAS_305_Documents', 'Documents'), cell: function (l) {
                    var ds = (l.documents || []).map(docOf);
                    return '<span class="vas-rqw-docs" style="white-space:normal" title="' + U.esc(ds.map(function (x) { return x.text; }).join(', ')) + '">' +
                        (ds.length ? ds.map(function (x) { return x.html; }).join(' ') : '<span class="m">—</span>') + '</span>';
                }
            });
        }
        cols.push({
            w: '7.6em', head: U.lbl('VAS_305_Status', 'Status'), cell: function (l) {
                return cell('', h.docStatus === 'CO' ? U.stateChip(l.isLate ? 'late' : (l.state === 'ordered' && l.receivedQty > 0 ? 'partreceived' : l.state), l.needBy) : U.statusChip(h.docStatus === 'VO' || h.docStatus === 'RE' || h.docStatus === 'CL' ? 'CL' : 'DR', h.docStatusName));
            }
        });
        var rows = lines.map(function (l) {
            return '<div class="vas-rqw-gr">' + cols.map(function (c) { return c.cell(l); }).join('') + '</div>';
        });
        var right = [];
        cols.forEach(function (c, i) { if (c.right) { right.push(i); } });
        // VAS163 2026-11-13: row 1120 - a requisition without lines shows its header and an empty-lines message
        var linesHtml = lines.length
            ? table(cols.map(function (c) { return c.w; }).join(' '), right, cols.map(function (c) { return c.head; }), rows)
            : '<div class="vas-rqw-msg">' + U.esc(U.lbl('VAS_305_NoLines', 'No lines on this requisition yet')) + '</div>';
        var body =
            '<div class="vas-rqw-hero ' + tone + '"><div class="top"><div><div class="t">' + U.esc(h.documentNo) + '</div>' +
            // VAS163 2026-11-13: row 1122 - 'Raised' is the creation date of the requisition (Created), not the document date
            '<div class="s">' + U.esc(U.lbl('VAS_305_RaisedBy', 'Raised') + ' ' + U.dateLong(h.created || h.dateDoc) + ' · ' + (h.requester || '')) + '</div></div>' +
            '<span class="pl">' + U.esc(pillTxt) + '</span></div>' +
            '<div class="v">' + U.esc(U.money(h.totalAmount, cur)) + '<small>' + U.esc(h.lineCount + ' ' + U.unit(h.lineCount, 'VAS_305_LinesCount', 'lines') + ' · ' +
                h.receivedLines + ' ' + U.lbl('VAS_305_FullyReceived', 'fully received')) + '</small></div></div>' +
            '<div class="vas-rqw-dcard">' +
            mc(U.lbl('VAS_305_RequestedBy', 'Requested by'), h.requester) +
            mc(U.lbl('VAS_305_Priority', 'Priority'), h.priority) +
            mc(U.lbl('VAS_305_Warehouse', 'Warehouse'), h.warehouse) +
            mc(U.lbl('VAS_305_SourceWarehouse', 'Source warehouse'), h.sourceWarehouse) +
            mc(U.lbl('VAS_305_RequisitionDate', 'Requisition date'), U.dateLong(h.dateDoc)) +
            mc(U.lbl('VAS_305_EarliestNeedBy', 'Earliest need-by'), U.dateLong(h.earliestNeedBy), h.isLate ? 'risk' : '', edTxt) +
            '</div>' +
            '<div class="vas-rqw-sh"><b>' + U.esc(U.lbl('VAS_305_Lines', 'Lines')) + '</b><span>' + U.esc(lines.length + ' ' + U.unit(lines.length, 'VAS_305_LinesCount', 'lines')) + '</span></div>' +
            linesHtml +
            '<div class="vas-rqw-tot"><span>' + U.esc(U.lbl('VAS_305_Total', 'Total')) + ' <b>' + U.esc(U.money(h.totalAmount, cur)) + '</b></span>' +
            '<span>' + U.esc(U.lbl('VAS_305_StillOpen', 'Still to come')) + ' <b>' + U.esc(U.money(h.openAmount, cur)) + '</b></span></div>';
        return {
            title: U.lbl('VAS_305_Requisition', 'Requisition') + ' ' + (h.documentNo || ''),
            body: body,
            foot: '<button type="button" class="vas-rqw-btn primary" data-open="' + h.requisitionId + '">' + U.esc(U.lbl('VAS_305_OpenRequisition', 'Open requisition')) + '</button>',
            after: function ($m) {
                $m.find('[data-open]').off('click').on('click', function () { U.openRequisition(widget, Number($(this).attr('data-open'))); });
            }
        };
    }

    /* ---------- KPI card (2x1): count + amount, popup list on click ---------- */

    /**
     * Builds a KPI widget constructor. cfg: { url, title, tone ('' | 'amb' | 'red'), unit(d),
     * meta(d), listTitle, listSub(d) } - texts are already-resolved strings or functions of the data.
     */
    U.buildKpi = function (cfg) {
        var T = function (v) { return typeof v === 'function' ? v() : v; };
        var Ctor = function () {
            var self = this;
            var $root = $('<div class="vas-rqw-root">');
            var $card = null, data = null;
            this.frame = null;
            this.windowNo = -1;

            this.Initalize = function () {
                $card = $('<button type="button" class="vas-rqw-card vas-rqw-kpi ' + (cfg.tone || '') + '">' +
                    '<span class="vas-rqw-kl">' + U.esc(T(cfg.title)) + '</span>' +
                    '<span class="vas-rqw-kv"><b>–</b></span><span class="vas-rqw-km">' + U.esc(U.lbl('VAS_305_Loading', 'Loading…')) + '</span>' +
                    '<span class="vas-rqw-busy"><i></i></span></button>');
                $root.empty().append($card);
                $card.on('click', function () {
                    this.blur();
                    if (!data) { return; }
                    U.showList(self, T(cfg.listTitle), cfg.listSub(data), data.records, data.currency);
                });
                self.reload();
            };

            this.reload = function () {
                if (!$card) { return; }
                $card.find('.vas-rqw-busy').addClass('on');
                U.get(cfg.url).done(function (d) {
                    data = d;
                    $card.find('.vas-rqw-kv').html('<b>' + U.esc(U.num(d.count)) + '</b><small>' + U.esc(cfg.unit(d)) + '</small>' +
                        '<span class="sep"></span><b>' + U.esc(U.money(d.amount, d.currency)) + '</b>');
                    var meta = cfg.meta(d);
                    $card.find('.vas-rqw-km').text(meta).attr('title', meta);
                    $card.attr('aria-label', T(cfg.title) + ': ' + d.count + ' ' + cfg.unit(d) + ', ' + U.money(d.amount, d.currency));
                }).fail(function (err) {
                    data = null;
                    $card.find('.vas-rqw-kv').html('<b>–</b>');
                    $card.find('.vas-rqw-km').text(err);
                }).always(function () {
                    $card.find('.vas-rqw-busy').removeClass('on');
                });
            };

            this.getRoot = function () { return $root; };
            this.disposeComponent = function () { if ($card) { $card.off(); } $root.remove(); };
        };
        U.makeWidget(Ctor);
        return Ctor;
    };

    /* ---------- widget base (lifecycle shared by every Requisition widget) ---------- */

    /**
     * Adds the framework methods to a widget constructor. The constructor must define
     * this.Initalize(), this.getRoot(), this.reload() and this.disposeComponent().
     */
    U.makeWidget = function (Ctor) {
        Ctor.prototype.widgetFirevalueChanged = function (value) {
            if (this.listener) { this.listener.widgetFirevalueChanged(value); }
        };
        Ctor.prototype.addChangeListener = function (listener) { this.listener = listener; };
        Ctor.prototype.init = function (windowNo, frame) {
            this.frame = frame;
            this.windowNo = windowNo;
            this.AD_UserHomeWidgetID = frame && frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
            this.frame.getContentGrid().append(this.getRoot());
            U.ensureDashVar(this.getRoot());
            this.Initalize();
        };
        Ctor.prototype.widgetSizeChange = function () { if (this.relayout) { this.relayout(); } };
        Ctor.prototype.refreshWidget = function () { if (this.reload) { this.reload(); } };
        Ctor.prototype.dispose = function () {
            if (this.disposeComponent) { this.disposeComponent(); }
            if (this.frame) { this.frame.dispose(); }
            this.frame = null;
        };
    };

})(VAS, jQuery);
