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
 * VAS163 2026-11-20: VAS_305_OnTime no longer used (every future date shows 'Due in N days', Sr 38)
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

    /** Fallback only (VAS.formatAmountByBrowser missing): compact money style (L / Cr). */
    function moneyFallback(value, cur) {
        var v = Number(value || 0);
        var sym = (cur && cur.Symbol) ? cur.Symbol : '';
        var pre = sym ? sym + ' ' : '';
        var a = Math.abs(v);
        if (a >= 10000000) { return pre + (v / 10000000).toFixed(2) + ' Cr'; }
        if (a >= 100000) { return pre + (v / 100000).toFixed(2) + ' L'; }
        return pre + Math.round(v).toLocaleString(locale());
    }

    /**
     * Compact money for card / pill / legend totals.
     * VAS163 2026-11-20: Sr 244 / Sr 77 - browser culture through VAS.formatAmountByBrowser with the compact option
     * (VIS.Util.formatCompactAmount: L / Cr for Indian-numbering currencies, digits grouped by the browser culture,
     * e.g. en-IN 24,40,500); amounts below 1,00,000 are shown in full.
     */
    U.money = function (value, cur) {
        if (typeof VAS.formatAmountByBrowser === 'function') { return U.docMoney(value, cur, true); }
        return moneyFallback(value, cur);
    };

    /** Full amount with grouping (popup tables). */
    U.amount = function (value, cur) {
        var p = (cur && cur.Precision != null) ? Math.min(Number(cur.Precision), 2) : 2;
        return Number(value || 0).toLocaleString(locale(), { minimumFractionDigits: 0, maximumFractionDigits: p });
    };

    /**
     * VAS163 2026-11-17: Sr 11 / Sr 14 - amount in the given currency (ISO + precision from the server) in the browser
     * culture through VAS.formatAmountByBrowser; compact for the large hero / total figures.
     */
    U.docMoney = function (value, cur, compact) {
        var v = Number(value || 0);
        var p = (cur && cur.Precision != null) ? Number(cur.Precision) : 2;
        if (typeof VAS.formatAmountByBrowser === 'function') {
            return VAS.formatAmountByBrowser(v, cur ? cur.Iso : '', p, { compact: !!compact && Math.abs(v) >= 1e5, symbol: cur ? cur.Symbol : '' });
        }
        return compact ? moneyFallback(v, cur) : ((cur && cur.Symbol ? cur.Symbol + ' ' : '') + U.amount(v, cur));
    };

    /**
     * VAS163 2026-11-17: Sr 27 - currency of one row (price-list currency of the requisition, sent by the server as
     * currencyIso / currencySymbol / currencyPrecision), else the given list currency.
     * VAS163 2026-11-20: Sr 41 / Sr 47 / Sr 68 - shared, also used by Lines to Order, Need-by Calendar, Category wise.
     */
    U.rowCurrency = function (r, cur) {
        if (r && r.currencyIso) {
            return { Iso: r.currencyIso, Symbol: r.currencySymbol || '', Precision: r.currencyPrecision != null && r.currencyPrecision >= 0 ? r.currencyPrecision : 2 };
        }
        return cur || null;
    };

    /** VAS163 2026-11-20: Sr 41 / Sr 47 / Sr 68 - full amount of a row in its own (price-list) currency with the currency sign. */
    U.rowMoney = function (r, cur) {
        var hasDoc = r && r.docAmount != null && !!r.currencyIso;
        return U.docMoney(hasDoc ? r.docAmount : (r ? r.amount : 0), hasDoc ? U.rowCurrency(r, cur) : cur, false);
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

    /** VAS163 2026-11-20: Sr 36 / Sr 43 / Sr 66 - dates are shown with the year (browser culture), same as dateLong. */
    U.date = function (iso) {
        var d = toDate(iso);
        return d ? d.toLocaleDateString(locale(), { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
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
        // VAS163 2026-11-20: Sr 38 - every future date shows 'Due in N days' (was 'On time' beyond 2 days); amber
        // up to 2 days, blue after that
        return U.chip(d <= 2 ? 'a' : 'b', U.lbl('VAS_305_DueIn', 'Due in') + ' ' + d + ' ' + U.unit(d, 'VAS_305_Days', 'days'));
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

    /** Requisition status chip from the bucket code (OP/PO/OR/PD/DE/DR/CL/VO). */
    U.statusChip = function (code, name) {
        // VAS163 2026-11-20: Sr 44 / Sr 57 - VO (voided / reversed) is its own bucket, red-toned
        var cls = { OP: 'b', PO: 'a', OR: 'a', PD: 'a', DE: 'g', DR: 'p', CL: 'n', VO: 'r' }[code] || 'n';
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
            // VAS163 2026-11-20: Sr 34 - hover title on the headings too (they can be cut on narrow widgets)
            return '<span class="' + (right && right.indexOf(i) >= 0 ? 'vas-rqw-r' : '') + '" title="' + U.esc(h) + '">' + U.esc(h) + '</span>';
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

    /**
     * VAS163 2026-11-20: Sr 15 - after 'Open requisition' the bottom tab panel and the right (Overview) panel stayed
     * hidden until the user switched to grid and back to form. The framework applies TabLayout 'Y' (switchRow ->
     * GridController.switchSingleRow -> APanel.showTabPanel) BEFORE the record is queried, and switchSingleRow does
     * nothing when the grid controller is already in single-record mode, so the panels are not attached / filled for
     * the opened record. Once the record is loaded, this runs the same grid -> form switch the user's toggle runs
     * (APanel.switchRow 'N' then 'Y'), which attaches the tab panels and refreshes them for the current record.
     * ap = the window's APanel (landing-page listener .apanel on the window, AWindow.cPanel from startWindow on Home).
     */
    function showPanelsWhenLoaded(ap, requisitionId) {
        if (!ap) { return; }
        var tries = 0, seen = 0;
        var t = window.setInterval(function () {
            tries++;
            var ok = false;
            try {
                // VAS163 2026-11-23: Sr 15 wait for the record only - from Home the window opens in its default (card)
                // layout, so single-row mode cannot be part of the condition.
                ok = !!(ap.curTab && typeof ap.curTab.getRecord_ID === 'function' && Number(ap.curTab.getRecord_ID()) === Number(requisitionId) &&
                    ap.curGC);
            } catch (e) { ok = false; }
            // the record must be current on two ticks in a row, so the framework's own row-change handling has finished
            seen = ok ? seen + 1 : 0;
            if (seen >= 2) {
                window.clearInterval(t);
                try {
                    if (typeof ap.switchRow === 'function') {
                        // already single-record: leave and re-enter it so the panels attach; card/grid: switch to it
                        if (typeof ap.curGC.getIsSingleRow === 'function' && ap.curGC.getIsSingleRow()) {
                            ap.switchRow(ap.curGC, 'N', false);
                        }
                        ap.switchRow(ap.curGC, 'Y', true);
                    } else if (typeof ap.showTabPanel === 'function') {
                        ap.showTabPanel(true);
                    }
                } catch (e2) { /* panels are an extra; the record is open either way */ }
                return;
            }
            if (tries >= 240) { window.clearInterval(t); } // ~60 s - VAS163 2026-11-23: a first (cold) window load can take ~25 s
        }, 250);
    }

    U.openRequisition = function (widget, requisitionId) {
        if (!requisitionId) { return; }
        U.closeModal();
        if (widget && widget.windowNo >= 0 && widget.listener && typeof widget.listener.widgetFirevalueChanged === 'function') {
            widget.listener.widgetFirevalueChanged({
                "TabWhereClause": "M_Requisition.M_Requisition_ID=" + requisitionId,
                "TabLayout": "Y",
                "TabIndex": "0"
            });
            showPanelsWhenLoaded(widget.listener.apanel, requisitionId); // VAS163 2026-11-20: Sr 15
            return;
        }
        // VAS163 2026-11-17: Sr 15 - VAS.ZoomUtil.zoomToRecord opens the window with a plain zoom query and no action
        // params, so the Requisition tab (AD_Tab 1000784) opens in its default layout TabLayout 'C' (card). Its bottom
        // panel VAS_240 (AD_TabPanel 1000464, alignment B) is defined for ViewType 'Y' (single record) only, so it was
        // not shown. The window is now opened the way the framework's own record links do (startWindow with action
        // params), in the single-record layout - the same TabWhereClause / TabLayout 'Y' the in-window path uses.
        var params = {
            "TabWhereClause": "M_Requisition.M_Requisition_ID=" + Number(requisitionId),
            "TabLayout": "Y",
            "TabIndex": "0"
        };
        var zoom = function () {
            if (VAS.ZoomUtil) {
                VAS.ZoomUtil.zoomToRecord("M_Requisition_ID", requisitionId, U._reqWindowId || 0, "VAS_Requisition", "Requisition")
                    .done(function (id) { if (id > 0) { U._reqWindowId = id; } });
            }
        };
        var start = function (windowId) {
            if (!(windowId > 0) || !window.VIS || !VIS.viewManager || typeof VIS.viewManager.startWindow !== 'function') { zoom(); return; }
            U._reqWindowId = windowId;
            try {
                // VAS163 2026-11-23: Sr 15 startWindow with action params (TabWhereClause/TabLayout) never finishes
                // loading when called from Home; open it with a zoom query (as VAS.ZoomUtil does) and switch the
                // loaded record to the single-record view, which attaches the bottom and right tab panels.
                var zoomQuery = new VIS.Query();
                zoomQuery.addRestriction("M_Requisition_ID", VIS.Query.prototype.EQUAL, Number(requisitionId));
                zoomQuery.setRecordCount(1);
                var w = VIS.viewManager.startWindow(windowId, zoomQuery);
                showPanelsWhenLoaded(w && w.cPanel, requisitionId); // VAS163 2026-11-20: Sr 15
            } catch (e) { zoom(); }
        };
        if (U._reqWindowId > 0) { start(U._reqWindowId); return; }
        if (VAS.ZoomUtil && typeof VAS.ZoomUtil.getWindowId === 'function') {
            VAS.ZoomUtil.getWindowId("VAS_Requisition", "Requisition").done(start).fail(zoom);
            return;
        }
        zoom();
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

    /** VAS163 2026-11-17: Sr 29 - rows per page of the requisition list popup. */
    var LIST_PAGE = 12;

    /**
     * Popup list of requisitions (KPI, donut slice, ...); a row opens the requisition.
     * opts (VAS163 2026-11-20): { docStatusLabel } - when set, a 'Requisition status' column (document status of the
     * requisition, server docStatusName) is shown before the Status column (Sr 60, Requisitions by Status).
     */
    U.showList = function (widget, title, subtitle, records, cur, opts) {
        records = records || [];
        opts = opts || {};
        var page = 0;
        var pages = Math.max(1, Math.ceil(records.length / LIST_PAGE));
        // VAS163 2026-11-17: Sr 27 / Sr 33 / Sr 34 - value in the requisition's price-list currency (full amount, wrapped
        // when long, VAS.formatAmountByBrowser), Need by with the year (browser culture), Requested by = requester name.
        // VAS163 2026-11-20: Sr 34 / Sr 54 - hover title with the full text on every cell
        var rowHtml = function (r) {
            var val = U.rowMoney(r, cur);
            var need = U.dateLong(r.needBy);
            var chip = r.isLate ? U.needChip(r.needBy) : U.statusChip(r.status, r.statusName);
            var chipText = $('<div>').html(chip).text();
            return '<div class="vas-rqw-gr" data-r="' + r.requisitionId + '"><span class="b" title="' + U.esc(r.documentNo) + '">' + U.esc(r.documentNo) + '</span>' +
                '<span title="' + U.esc(r.requester || '') + '">' + U.esc(r.requester || '') + '</span><span class="m" title="' + U.esc(r.warehouse || '') + '">' + U.esc(r.warehouse || '') + '</span>' +
                '<span class="vas-rqw-r" title="' + U.esc(need) + '">' + U.esc(need) + '</span><span class="vas-rqw-r vas-rqw-wrap" title="' + U.esc(val) + '">' + U.esc(val) + '</span>' +
                (opts.docStatusLabel ? '<span title="' + U.esc(r.docStatusName || '') + '">' + U.esc(r.docStatusName || '') + '</span>' : '') +
                '<span title="' + U.esc(chipText) + '">' + chip + '</span></div>';
        };
        var listHtml = function () {
            if (!records.length) { return '<div class="vas-rqw-msg">' + U.esc(U.lbl('VAS_305_NothingHere', 'Nothing here')) + '</div>'; }
            var start = page * LIST_PAGE;
            var slice = records.slice(start, start + LIST_PAGE);
            // the Value heading carries no currency sign: each row shows its own currency (Sr 27)
            var heads = [U.lbl('VAS_305_Requisition', 'Requisition'), U.lbl('VAS_305_Requester', 'Requested by'), U.lbl('VAS_305_Warehouse', 'Warehouse'),
                U.lbl('VAS_305_NeedBy', 'Need by'), U.lbl('VAS_305_Value', 'Value')];
            var cols = 'minmax(0,1fr) minmax(0,1.3fr) minmax(0,1.3fr) 6.6em minmax(7.5em,1.1fr)';
            if (opts.docStatusLabel) { heads.push(opts.docStatusLabel); cols += ' minmax(5.5em,0.9fr)'; }
            heads.push(U.lbl('VAS_305_Status', 'Status'));
            cols += ' 8em';
            var html = table(cols, [3, 4], heads, slice.map(rowHtml));
            if (pages > 1) {
                // VAS163 2026-11-17: Sr 29 - 12 rows per page, pager under the table
                html += '<div class="vas-rqw-lpg"><span class="vas-rqw-hp">' + U.esc(U.lbl('VAS_305_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' +
                    U.lbl('VAS_305_Of', 'of') + ' ' + records.length) + '</span><span class="vas-rqw-pg">' +
                    '<button type="button" data-lp="-1" aria-label="' + U.esc(U.lbl('VAS_305_PrevPage', 'Previous page')) + '"' + (page ? '' : ' disabled') + '>' + U.icon.prev + '</button>' +
                    (page + 1) + ' ' + U.esc(U.lbl('VAS_305_Of', 'of')) + ' ' + pages +
                    '<button type="button" data-lp="1" aria-label="' + U.esc(U.lbl('VAS_305_NextPage', 'Next page')) + '"' + (page < pages - 1 ? '' : ' disabled') + '>' + U.icon.next + '</button></span></div>';
            }
            return html;
        };
        var bodyHtml = function () {
            return '<div class="vas-rqw-sh"><b>' + U.esc(title) + '</b><span>' + U.esc(subtitle || '') + '</span></div>' +
                '<div class="vas-rqw-lst">' + listHtml() + '</div>';
        };
        var view = {
            title: title,
            body: bodyHtml(),
            after: function ($m) {
                var $pb = $m.find('.vas-rqw-pb');
                $pb.off('click.rqwl').on('click.rqwl', '.vas-rqw-gr[data-r]', function () {
                    U.showRequisition(widget, Number($(this).attr('data-r')));
                });
                $pb.off('click.rqwp').on('click.rqwp', '[data-lp]', function () {
                    page = Math.max(0, Math.min(pages - 1, page + Number($(this).attr('data-lp'))));
                    $pb.find('.vas-rqw-lst').html(listHtml());
                    view.body = bodyHtml(); // Back from a requisition returns to this page
                    $pb.scrollTop(0);
                });
            }
        };
        U.openModal(view);
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
            // VAS163 2026-11-17: Sr 7 - qty and Ord. / Rcvd. come from the server in the line's UOM (QtyEntered)
            { w: '4.4em', right: true, head: U.lbl('VAS_305_Qty', 'Qty'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.num(l.qty))); } },
            { w: 'minmax(3.5em,0.6fr)', head: U.lbl('VAS_305_UOM', 'UOM'), cell: function (l) { return cell('m', U.esc(l.uom || ''), l.uom || ''); } },
            // VAS163 2026-11-20: Need by column wider, the date carries the year
            { w: '6.6em', right: true, head: U.lbl('VAS_305_NeedBy', 'Need by'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.date(l.needBy)), U.dateLong(l.needBy)); } },
            // VAS163 2026-11-17: Sr 11 - line amount in the price-list currency of the requisition (server ISO + precision)
            { w: '7.2em', right: true, head: U.lbl('VAS_305_Amount', 'Amount'), cell: function (l) { return cell('vas-rqw-r', U.esc(U.docMoney(l.amount, cur))); } }
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
        // VAS163 2026-11-17: Sr 22 - a requisition without lines shows only its header and detail card: the whole
        // lines section (title, table / empty message and the totals under it) is left out.
        var linesHtml = !lines.length ? '' :
            '<div class="vas-rqw-sh"><b>' + U.esc(U.lbl('VAS_305_Lines', 'Lines')) + '</b><span>' + U.esc(lines.length + ' ' + U.unit(lines.length, 'VAS_305_LinesCount', 'lines')) + '</span></div>' +
            table(cols.map(function (c) { return c.w; }).join(' '), right, cols.map(function (c) { return c.head; }), rows) +
            // VAS163 2026-11-17: Sr 14 / Sr 11 - exact totals in the price-list currency
            '<div class="vas-rqw-tot"><span>' + U.esc(U.lbl('VAS_305_Total', 'Total')) + ' <b>' + U.esc(U.docMoney(h.totalAmount, cur)) + '</b></span>' +
            '<span>' + U.esc(U.lbl('VAS_305_StillOpen', 'Still to come')) + ' <b>' + U.esc(U.docMoney(h.openAmount, cur)) + '</b></span></div>';
        var body =
            '<div class="vas-rqw-hero ' + tone + '"><div class="top"><div><div class="t">' + U.esc(h.documentNo) + '</div>' +
            // VAS163 2026-11-13: row 1122 - 'Raised' is the creation date of the requisition (Created), not the document date
            '<div class="s">' + U.esc(U.lbl('VAS_305_RaisedBy', 'Raised') + ' ' + U.dateLong(h.created || h.dateDoc) + ' · ' + (h.requester || '')) + '</div></div>' +
            '<span class="pl">' + U.esc(pillTxt) + '</span></div>' +
            '<div class="v">' + U.esc(U.docMoney(h.totalAmount, cur, true)) + '<small>' + U.esc(h.lineCount + ' ' + U.unit(h.lineCount, 'VAS_305_LinesCount', 'lines') + ' · ' +
                h.receivedLines + ' ' + U.lbl('VAS_305_FullyReceived', 'fully received')) + '</small></div></div>' +
            // VAS163 2026-11-17: Sr 25 - three fields per line: Requested by, Priority, Requisition date /
            // Warehouse, Source warehouse, Date required (3-column grid, see .vas-rqw-dcard)
            '<div class="vas-rqw-dcard">' +
            mc(U.lbl('VAS_305_RequestedBy', 'Requested by'), h.requester) +
            mc(U.lbl('VAS_305_Priority', 'Priority'), h.priority) +
            mc(U.lbl('VAS_305_RequisitionDate', 'Requisition date'), U.dateLong(h.dateDoc)) +
            mc(U.lbl('VAS_305_Warehouse', 'Warehouse'), h.warehouse) +
            mc(U.lbl('VAS_305_SourceWarehouse', 'Source warehouse'), h.sourceWarehouse) +
            // VAS163 2026-11-17: Sr 5 - the server now sends the header Date Required here (current after an edit)
            mc(U.lbl('VAS_305_EarliestNeedBy', 'Earliest need-by'), U.dateLong(h.earliestNeedBy), h.isLate ? 'risk' : '', edTxt) +
            '</div>' +
            linesHtml;
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
                    // VAS163 2026-11-17: Sr 33 - amount in the browser culture; it wraps onto the next line when it
                    // does not fit (.vas-rqw-kv b.amt) instead of being cut off.
                    // VAS163 2026-11-20: Sr 77 - card total compact (L / Cr from 1,00,000 for INR, browser-culture digit
                    // grouping); the list popup rows keep the full amount. The full amount is the hover title.
                    var amt = U.docMoney(d.amount, d.currency, true);
                    var amtFull = U.docMoney(d.amount, d.currency, false);
                    $card.find('.vas-rqw-kv').html('<b>' + U.esc(U.num(d.count)) + '</b><small>' + U.esc(cfg.unit(d)) + '</small>' +
                        '<span class="sep"></span><b class="amt" title="' + U.esc(amtFull) + '">' + U.esc(amt) + '</b>');
                    var meta = cfg.meta(d);
                    $card.find('.vas-rqw-km').text(meta).attr('title', meta);
                    $card.attr('aria-label', T(cfg.title) + ': ' + d.count + ' ' + cfg.unit(d) + ', ' + amt);
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
/* VAS163 2026-11-17: Sr 11 / Sr 14 - copied from VAS_217 (VAS163 2026-11-12): VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
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
