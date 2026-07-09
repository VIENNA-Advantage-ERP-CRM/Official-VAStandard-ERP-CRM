/************************************************************
 * Module Name    : VAS
 * Purpose        : Vendor Credit Utilisation Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_026_VendorCreditUtil  => "Vendor Credit Utilisation"
 *   VAS_026_Breached          => "breached"
 *   VAS_026_BreachedChip      => "Breached"
 *   VAS_026_NoCreditVendors   => "No vendors with a credit limit configured"
 *   VAS_026_Showing           => "Showing"
 *   VAS_026_Of                => "of"
 *   VAS_026_Prev              => "Previous"
 *   VAS_026_Next              => "Next"
 *
 * Paging: server-side paging with the canonical footer pager (mirrors VAS_020).
 * The page size is ADAPTIVE — measured from the list's available height so the
 * visible rows always fit the card; a ResizeObserver re-pages on resize. A
 * localised busy indicator is shown inside the list while a page is fetched.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* ---- Message helper: returns the AD_Message text, or the inline default
         when the system has no message for the key. ---- */
    function msg(key, fallback) {
        var value = VIS.Msg.getMsg(key);
        return value && value !== key && value !== '[' + key + ']' ? value : fallback;
    }

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget's em-anchor clamps resolve against the dashboard's
       visible width, not the viewport. (Mirrors VAS_020.) */
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

    VAS.VAS_026_VendorCreditUtilisationWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self    = this;
        var $root    = $('<div class="h-100 w-100 vas-widget-bg vas-vcuwdg-root">');
        var $container;
        var widgetID = null;

        /* Base-currency (from payload), reused for compact amount formatting. */
        var _curSym  = '';
        var _curPrec = 2;
        var _breach  = 0;   // total breached vendors (whole set, not just the page)

        /* Server-side paging state. Page size is ADAPTIVE — derived from the list's
           available height at runtime (mirrors VAS_020) so the visible rows always fit
           the card (no clipped last row); a ResizeObserver re-pages on resize. */
        var _items     = [];
        var _page      = 1;
        var _total     = 0;
        var _pageSize  = 8;      // initial guess; replaced by the adaptive measure + server echo
        var _loading   = false;
        var _rowH      = 0;      // last measured row height (px)
        var _needsSync = true;   // measure capacity on first paint only; resize handled by observer
        var _observer  = null;
        var _shellDone = false;  // header/list/pager scaffolding rendered
        var MIN_ROWS      = 1;   // never force more rows than physically fit
        var ROW_FALLBACK  = 40;  // px, used only before a real row is measured

        $self._kpiData = null;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        /* ---- Data load (first page) ---- */
        this.intialLoad = function () {
            _page      = 1;
            _total     = 0;
            _needsSync = true;
            _loading   = false;
            _shellDone = false;
            fetchPage(1, true);
        };

        function buildShell() {
            $container = $('<div class="vas-vcuwdg-container" id="vas_vcuwdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Header + list + pager scaffolding (rendered once per load) ---- */
        function renderShell() {
            $container.empty();

            var html =
                '<div class="vas-vcuwdg-header">' +
                    '<div class="vas-vcuwdg-title">' +
                        '<svg class="vas-vcuwdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<rect x="1" y="4" width="22" height="16" rx="2"/>' +
                            '<line x1="1" y1="10" x2="23" y2="10"/>' +
                        '</svg>' +
                        vcuEsc(msg('VAS_026_VendorCreditUtil', 'Vendor Credit Utilisation')) +
                    '</div>' +
                    (_breach > 0
                        ? '<span class="vas-vcuwdg-badge">' + _breach + ' ' +
                          vcuEsc(msg('VAS_026_Breached', 'breached')) + '</span>'
                        : '') +
                '</div>' +
                '<div class="vas-vcuwdg-list"></div>' +
                '<div class="vas-vcuwdg-pager"></div>';

            $container.html(html);
            _shellDone = true;
        }

        /* ---- Paint the current page (one server page + footer pager) ---- */
        function paintList() {
            if (!$container) { return; }
            var $list  = $container.find('.vas-vcuwdg-list');
            var $pager = $container.find('.vas-vcuwdg-pager');
            var totalPages = _pageSize > 0 ? Math.ceil(_total / _pageSize) : 0;

            if (!_items || _items.length === 0) {
                $list.html('<div class="vas-vcuwdg-empty">' +
                    vcuEsc(msg('VAS_026_NoCreditVendors', 'No vendors with a credit limit configured')) +
                    '</div>');
                $pager.empty();
                return;
            }

            var rows = '';
            for (var i = 0; i < _items.length; i++) {
                rows += vcuBuildRow(_items[i], _curSym, _curPrec);
            }
            $list.html(rows);

            var from = (_page - 1) * _pageSize + 1;
            var to   = Math.min(_page * _pageSize, _total);
            $pager.html(pagerHtml(_page, totalPages, from, to, _total));
            $pager.find('.vas-vcuwdg-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1, false); }
            });
            $pager.find('.vas-vcuwdg-pg-next').on('click', function () {
                if (!_loading && _page < totalPages) { fetchPage(_page + 1, false); }
            });

            // Adapt the page size to the list height ONLY on the first paint / after a resize —
            // never on manual navigation (that would flip pageSize and re-clamp the page).
            if (_needsSync) { scheduleSync(); }
        }

        /* ---- Adaptive row capacity (mirrors VAS_020) ---- */
        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (_loading || !$container) { return; }
            var el = $container.find('.vas-vcuwdg-list')[0];
            if (!el) { return; }
            if (_total <= 0) { return; }
            var avail = el.clientHeight;
            if (avail <= 0) {
                if (_needsSync) { scheduleSync(); }   // layout not settled — retry
                return;
            }
            // Size off the tallest rendered row so a wrapped vendor name never clips.
            var rows = el.querySelectorAll('.vas-vcuwdg-prog');
            var maxH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_FALLBACK;

            _needsSync = false;
            var capacity = Math.max(MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== _pageSize) {
                _pageSize = capacity;
                fetchPage(_page, false);   // server re-clamps the page to the new total
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$container) { return; }
            var el = $container.find('.vas-vcuwdg-list')[0];
            if (!el) { return; }
            if (_observer) { _observer.disconnect(); }
            _observer = new ResizeObserver(function () {
                if (!_loading) { syncCapacity(); }
            });
            _observer.observe(el);
        }

        /* ---- Fetch one server page ---- */
        function fetchPage(pageNo, isInitial) {
            if (_loading) { return; }
            _loading = true;
            if (isInitial) {
                $bsyDiv[0].style.visibility = 'visible';
            } else {
                showListBusy();
            }
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_026_VendorCreditUtilisationWidget/GetVendorCreditUtilisation',
                data: { pageNo: pageNo, pageSize: _pageSize },
                dataType: 'json',
                async: true,
                success: function (res) {
                    var d = null;
                    try { d = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    $self._kpiData = d;
                    _loading = false;
                    if (d) {
                        _items    = d.Vendors || [];
                        _page     = d.PageNo || pageNo;
                        _total    = d.TotalCount != null ? d.TotalCount : _total;
                        _pageSize = d.PageSize || _pageSize;
                        _curSym   = d.CurSymbol || _curSym;
                        _curPrec  = VIS.Env.getCtx().getStdPrecision() || (d.StdPrecision != null ? d.StdPrecision : _curPrec);
                        _breach   = d.BreachCount || 0;
                    }
                    if (isInitial || !_shellDone) {
                        renderShell();
                        observeList();
                    }
                    if (isInitial) { $bsyDiv[0].style.visibility = 'hidden'; }
                    paintList();
                },
                error: function () {
                    _loading = false;
                    if (isInitial) {
                        renderShell();
                        observeList();
                        $bsyDiv[0].style.visibility = 'hidden';
                    }
                    paintList();
                }
            });
        }

        // Localised busy indicator inside the list while a page is fetched
        // (uses the framework loader, like the widget's own busy overlay).
        function showListBusy() {
            if (!$container) { return; }
            $container.find('.vas-vcuwdg-list').html('<div class="vas-vcuwdg-list-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
        }

        /* ---- Build a progress row ---- */
        function vcuBuildRow(v, sym, prec) {
            var pct        = Math.max(0, v.UtilPct);
            var fillW      = Math.min(100, pct);
            var barClass   = pct >= 100 ? 'vas-vcuwdg-prog-fill--red' : (pct >= 75 ? 'vas-vcuwdg-prog-fill--amber' : 'vas-vcuwdg-prog-fill--green');
            var valClass   = pct >= 100 ? 'vas-vcuwdg-prog-val--breached' : 'vas-vcuwdg-prog-val--normal';
            var breachChip = v.IsBreached
                ? '<span class="vas-vcuwdg-breach-chip">' +
                  vcuEsc(msg('VAS_026_BreachedChip', 'Breached')) + '</span>'
                : '';
            var pctStr = Math.round(pct) + '%';
            var amtStr = vcuFmt(v.CreditUsed, sym, prec) + '/' + vcuFmt(v.CreditLimit, sym, prec);

            return (
                '<div class="vas-vcuwdg-prog">' +
                    '<div class="vas-vcuwdg-prog-meta">' +
                        '<span class="vas-vcuwdg-prog-name">' +
                            vcuEsc(v.VendorName) + breachChip +
                        '</span>' +
                        '<span class="vas-vcuwdg-prog-val ' + valClass + '">' +
                            vcuEsc(pctStr + ' · ' + amtStr) +
                        '</span>' +
                    '</div>' +
                    '<div class="vas-vcuwdg-prog-track">' +
                        '<div class="vas-vcuwdg-prog-fill ' + barClass + '" style="width:' + fillW + '%"></div>' +
                    '</div>' +
                '</div>'
            );
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
             compact prev / "n of m" / next control right. Hidden on a single page. ---- */
        function pagerHtml(pageNo, totalPages, from, to, total) {
            if (totalPages <= 1) { return ''; }
            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = pageNo >= totalPages ? ' disabled' : '';
            var showing = msg('VAS_026_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + msg('VAS_026_Of', 'of') + ' ' + total;
            return '<div class="vas-vcuwdg-pager-row">' +
                '<span class="vas-vcuwdg-pager-info">' + vcuEsc(showing) + '</span>' +
                '<div class="vas-vcuwdg-pager-nav">' +
                    '<button type="button" class="vas-vcuwdg-pgbtn vas-vcuwdg-pg-prev" aria-label="' + vcuEsc(msg('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-vcuwdg-pager-label">' + pageNo + ' ' + msg('VAS_026_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-vcuwdg-pgbtn vas-vcuwdg-pg-next" aria-label="' + vcuEsc(msg('VAS_026_Next', 'Next')) + '"' + nextDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>' +
                '</div>' +
            '</div>';
        }

        /* ---- Helpers ---- */
        function vcuEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function vcuFmt(amount, sym, precision) {
            if (!amount || amount === 0) { return sym + '0'; }
            var abs  = Math.abs(amount);
            var loc  = window.navigator.language;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            var prec  = VIS.Env.getCtx().getStdPrecision() || precision || 2;
            if (abs >= 10000000) { return sym + (abs / 10000000).toLocaleString(loc, opts1) + 'Cr'; }
            if (abs >= 100000)   { return sym + (abs / 100000).toLocaleString(loc, opts1)   + 'L';  }
            if (abs >= 1000)     { return sym + (abs / 1000).toLocaleString(loc, opts1)     + 'K';  }
            return sym + abs.toLocaleString(loc, { minimumFractionDigits: prec, maximumFractionDigits: prec });
        }

        /* ---- Busy indicator ---- */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        /* ---- Refresh ---- */
        this.refreshWidget = function () {
            $self._kpiData = null;
            if (_observer) { _observer.disconnect(); _observer = null; }
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this._teardown = function () {
            if (_observer) { _observer.disconnect(); _observer = null; }
        };

        this.getRoot = function () { return $root; };
    };

    /* ---- Prototype ---- */
    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        // Self-wire the dashboard-width CSS variable (--dash-inline-size) the clamps read.
        ensureDashInlineSizeVar(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_026_VendorCreditUtilisationWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
