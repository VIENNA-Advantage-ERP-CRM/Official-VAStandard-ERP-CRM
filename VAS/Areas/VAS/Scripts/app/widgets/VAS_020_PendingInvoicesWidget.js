/************************************************************
 * Module Name    : VAS
 * Purpose        : Pending Invoices Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_020_PendingInvoices     => "Pending Invoices"
 *   VAS_020_NeedsAttention      => "Needs Attention"
 *   VAS_020_GRNMismatch         => "GRN Mismatch"
 *   VAS_020_PONotRaised         => "PO Not Raised"
 *   VAS_020_ReadyToPay          => "Ready to Pay"
 *   VAS_020_UpcomingPaymentsDue => "Upcoming Payments Due"
 *   VAS_020_Pending             => "pending"
 *   VAS_020_NoDuePayments       => "No payments due in the next 14 days"
 *   VAS_020_Value               => "value"
 *   VAS_020_Showing             => "Showing"
 *   VAS_020_Of                  => "of"
 *   VAS_020_Prev                => "Previous"
 *   VAS_020_Next                => "Next"
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
       visible width, not the viewport. (Mirrors VAS_018.) */
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

    VAS.VAS_020_PendingInvoicesWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self    = this;
        var $root    = $('<div class="h-100 w-100 vas-widget-bg vas-piawdg-root">');
        var $container;
        var widgetID = null;

        /* Base-currency (from KPI payload), reused for compact amount formatting. */
        var _curSym  = '';
        var _curIso  = '';
        var _curPrec = 2;

        /* Upcoming-due server-side paging state. The page size is ADAPTIVE — derived from
           the list's available height at runtime (mirrors VAS_021) so the visible rows always
           fit the card (no clipped last row); a ResizeObserver re-pages on resize. */
        var _dueItems     = [];
        var _duePage      = 1;
        var _dueTotal     = 0;
        var _duePageSize  = 4;      // initial guess; replaced by the adaptive measure + server echo
        var _dueLoading   = false;
        var _dueRowH      = 0;      // last measured due-row height (px)
        var _dueNeedsSync = true;   // measure capacity on first paint only; resize handled by observer
        var _dueObserver  = null;
        var DUE_MIN_ROWS      = 1;  // never force more rows than physically fit
        var DUE_ROW_FALLBACK  = 40; // px, used only before a real row is measured

        /* Category drill-down modal server-side paging state. Page size is ADAPTIVE —
           measured from the popup list's available height (mirrors VAS_021 / the due list). */
        var $catDialog    = null;
        var _catCat       = '';
        var _catPage      = 1;
        var _catTotal     = 0;
        var _catTotalPgs  = 0;
        var _catPageSize  = 20;     // initial guess; replaced by the adaptive measure + server echo
        var _catLoading   = false;
        var _catRowH      = 0;      // last measured row height (px)
        var _catNeedsSync = true;   // measure capacity on first render only; resize handled by observer
        var _catObserver  = null;
        var CAT_MIN_ROWS      = 1;
        var CAT_ROW_FALLBACK  = 48; // px, used only before a real row is measured

        $self._kpiData = null;

        var ZOOM_WINDOW_NAME_NEW = 'VAS_APInvoice';
        var windowId = 0;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        /* ---- Data load ---- */
        this.intialLoad = function () {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_020_PendingInvoicesWidget/GetPendingInvoices',
                dataType: 'json',
                async: true,
                success: function (data) {
                    $self._kpiData = typeof data === 'string' ? JSON.parse(data) : data;
                    if ($self._kpiData) { renderWidget($self._kpiData); }
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        function buildShell() {
            $container = $('<div class="vas-piawdg-container" id="vas_piawdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render ---- */
        function renderWidget(data) {
            $container.empty();

            _curSym  = data.CurSymbol || '';
            _curIso  = data.CurIso    || '';
            _curPrec = (data.StdPrecision != null ? data.StdPrecision : (VIS.Env.getCtx().getStdPrecision() || 2));
            var tot  = data.TotalPending || 0;

            /* Header */
            var html =
                '<div class="vas-piawdg-header">' +
                    '<div class="vas-piawdg-title">' +
                        '<svg class="vas-piawdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
                            '<polyline points="14 2 14 8 20 8"/>' +
                            '<line x1="9" y1="15" x2="15" y2="15"/>' +
                        '</svg>' +
                        piEsc(msg('VAS_020_PendingInvoices', 'Pending Invoices')) +
                    '</div>' +
                    (tot > 0
                        ? '<span class="vas-piawdg-badge">' + tot + ' ' + (msg('VAS_020_Pending', 'pending')) + '</span>'
                        : '') +
                '</div>' +

                /* KPI 2×2 grid */
                '<div class="vas-piawdg-kpi-grid">' +
                    piKpiBox('approval',
                        msg('VAS_020_NeedsAttention', 'Needs Attention'),
                        data.AwaitingApprovalCount, data.AwaitingApprovalAmt, 'vas-piawdg-kpi-val--aa') +
                    piKpiBox('grn',
                        msg('VAS_020_GRNMismatch', 'GRN Mismatch'),
                        data.GrnMismatchCount, data.GrnMismatchAmt, 'vas-piawdg-kpi-val--grn') +
                    piKpiBox('po',
                        msg('VAS_020_PONotRaised', 'PO Not Raised'),
                        data.PoNotRaisedCount, data.PoNotRaisedAmt, 'vas-piawdg-kpi-val--pnr') +
                    piKpiBox('ready',
                        msg('VAS_020_ReadyToPay', 'Ready to Pay'),
                        data.ReadyToPayCount, data.ReadyToPayAmt, 'vas-piawdg-kpi-val--rtp') +
                '</div>' +

                '<div class="vas-piawdg-divider"></div>' +

                /* Upcoming due section (server-side paged, footer pager) */
                '<div class="vas-piawdg-due-header">' +
                    (msg('VAS_020_UpcomingPaymentsDue', 'Upcoming Payments Due')) +
                '</div>' +
                '<div class="vas-piawdg-due-list"></div>' +
                '<div class="vas-piawdg-due-pager"></div>';

            $container.html(html);

            $container.find('.vas-piawdg-kpi-click').on('click', function () {
                openCategoryPopup($(this).attr('data-cat'), $(this).attr('data-label'));
            });
            $container.find('.vas-piawdg-kpi-click').on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    openCategoryPopup($(this).attr('data-cat'), $(this).attr('data-label'));
                }
            });

            /* Seed due paging from the KPI payload's first page, then adapt the page size to
               the card's available height (VAS_021 pattern). */
            _dueItems     = data.DueItems || [];
            _duePage      = 1;
            _dueTotal     = data.DueTotalCount != null ? data.DueTotalCount : _dueItems.length;
            _duePageSize  = data.DuePageSize || _duePageSize;
            _dueNeedsSync = true;
            paintDue();
            observeDue();
        }

        /* ---- Upcoming due list (one server page + footer pager) ---- */
        function paintDue() {
            if (!$container) { return; }
            var $list = $container.find('.vas-piawdg-due-list');
            var totalPages = _duePageSize > 0 ? Math.ceil(_dueTotal / _duePageSize) : 0;

            if (!_dueItems || _dueItems.length === 0) {
                $list.html('<div class="vas-piawdg-due-empty">' + (msg('VAS_020_NoDuePayments', 'No payments due in the next 14 days')) + '</div>');
                $container.find('.vas-piawdg-due-pager').empty();
                return;
            }

            var rows = '';
            for (var i = 0; i < _dueItems.length; i++) {
                var item      = _dueItems[i];
                var urgentCls = item.DaysUntilDue <= 3 ? 'vas-piawdg-urgent' : 'vas-piawdg-warning';
                rows +=
                    '<div class="vas-piawdg-due-item">' +
                        '<span class="vas-piawdg-due-dot ' + urgentCls + '"></span>' +
                        '<div class="vas-piawdg-due-info">' +
                            '<div class="vas-piawdg-due-name">' + piEsc(item.VendorName) + '</div>' +
                            '<div class="vas-piawdg-due-date">Due ' + piEsc(item.DueDateStr) + '</div>' +
                        '</div>' +
                        '<div class="vas-piawdg-due-amt ' + urgentCls + '">' +
                            piMetric(item.OpenAmt) +
                        '</div>' +
                    '</div>';
            }
            $list.html(rows);

            var from = (_duePage - 1) * _duePageSize + 1;
            var to   = Math.min(_duePage * _duePageSize, _dueTotal);
            $container.find('.vas-piawdg-due-pager').html(
                pagerHtml(_duePage, totalPages, from, to, _dueTotal, 'vas-piawdg-due')
            );
            $container.find('.vas-piawdg-due-pager .vas-piawdg-pg-prev').on('click', function () {
                if (!_dueLoading && _duePage > 1) { fetchDuePage(_duePage - 1); }
            });
            $container.find('.vas-piawdg-due-pager .vas-piawdg-pg-next').on('click', function () {
                if (!_dueLoading && _duePage < totalPages) { fetchDuePage(_duePage + 1); }
            });

            // Adapt the page size to the list height ONLY on the first paint / after a resize —
            // never on manual navigation (that would flip pageSize and re-clamp the page).
            if (_dueNeedsSync) { scheduleDueSync(); }
        }

        /* ---- Adaptive due-row capacity (mirrors VAS_021) ---- */
        function scheduleDueSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncDueCapacity(); });
        }

        function syncDueCapacity() {
            if (_dueLoading || !$container) { return; }
            var el = $container.find('.vas-piawdg-due-list')[0];
            if (!el) { return; }
            if (_dueTotal <= 0) { return; }
            var avail = el.clientHeight;
            if (avail <= 0) {
                if (_dueNeedsSync) { scheduleDueSync(); }   // layout not settled — retry
                return;
            }
            // Size off the tallest rendered row so a wrapped vendor name never clips.
            var rows = el.querySelectorAll('.vas-piawdg-due-item');
            var maxH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
            }
            if (maxH > 0) { _dueRowH = maxH; }
            var rowH = _dueRowH > 0 ? _dueRowH : DUE_ROW_FALLBACK;

            _dueNeedsSync = false;
            var capacity = Math.max(DUE_MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== _duePageSize) {
                _duePageSize = capacity;
                fetchDuePage(_duePage);   // server re-clamps the page to the new total
            }
        }

        function observeDue() {
            if (typeof ResizeObserver === 'undefined' || !$container) { return; }
            var el = $container.find('.vas-piawdg-due-list')[0];
            if (!el) { return; }
            if (_dueObserver) { _dueObserver.disconnect(); }
            _dueObserver = new ResizeObserver(function () {
                if (!_dueLoading) { syncDueCapacity(); }
            });
            _dueObserver.observe(el);
        }

        function fetchDuePage(pageNo) {
            if (_dueLoading) { return; }
            _dueLoading = true;
            showDueBusy();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_020_PendingInvoicesWidget/GetDuePayments',
                data: { pageNo: pageNo, pageSize: _duePageSize },
                dataType: 'json',
                async: true,
                success: function (res) {
                    var d = null;
                    try { d = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    _dueLoading = false;
                    if (d) {
                        _dueItems    = d.DueItems || [];
                        _duePage     = d.PageNo || pageNo;
                        _dueTotal    = d.TotalCount != null ? d.TotalCount : _dueTotal;
                        _duePageSize = d.PageSize || _duePageSize;
                    }
                    paintDue();
                },
                error: function () { _dueLoading = false; paintDue(); }
            });
        }

        // Localised busy indicator inside the due list while a page is fetched
        // (uses the framework loader, like the widget's own busy overlay).
        function showDueBusy() {
            if (!$container) { return; }
            $container.find('.vas-piawdg-due-list').html('<div class="vas-piawdg-due-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
        }

        /* ---- Category drill-down popup (invoice headers behind a clicked tile) ---- */
        function openCategoryPopup(cat, label) {
            if ($catDialog) { return; }
            _catCat       = cat;
            _catPage      = 1;
            _catTotal     = 0;
            _catTotalPgs  = 0;
            _catLoading   = false;
            _catNeedsSync = true;

            $catDialog = $('<div class="vas-piawdg-cat-dialog">');
            $('body').append($catDialog);

            $catDialog.dialog({
                autoOpen: false,
                modal: true,
                resizable: false,
                title: label,
                width: Math.min(780, Math.max(320, $(window).width() - 40)),
                // Fixed height so the popup size never changes with the number of rows;
                // rows are server-side paged (footer pager) rather than scrolled.
                height: Math.min(520, Math.max(320, $(window).height() - 80)),
                dialogClass: 'vas-piawdg-dialog-shell',
                close: function () {
                    if (_catObserver) { _catObserver.disconnect(); _catObserver = null; }
                    $catDialog.dialog('destroy');
                    $catDialog.remove();
                    $catDialog = null;
                }
            });

            // Custom close button (in-place control we fully style, like the other popups).
            var $widget = $catDialog.dialog('widget');
            $widget.find('.ui-dialog-titlebar-close').remove();
            var $close = $('<button type="button" class="vas-piawdg-dialog-close" aria-label="' + piEsc(msg('VAS_020_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $close.on('click', function () { if ($catDialog) { $catDialog.dialog('close'); } });
            $widget.find('.ui-dialog-titlebar').append($close);

            $catDialog.dialog('open');
            $catDialog.dialog('option', 'position', { my: 'center', at: 'center', of: window });

            loadCatPage(1);
        }

        // Fetch one server page of invoices for the current category and render it.
        function loadCatPage(pageNo) {
            if (!$catDialog || _catLoading) { return; }
            _catLoading = true;
            var atCat = _catCat;

            $catDialog.html('<div class="vas-piawdg-pop-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_020_PendingInvoicesWidget/GetCategoryInvoices',
                data: { category: _catCat, pageNo: pageNo, pageSize: _catPageSize },
                dataType: 'json',
                async: true,
                success: function (res) {
                    if (!$catDialog || atCat !== _catCat) { _catLoading = false; return; }
                    var d = null;
                    try { d = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    _catLoading = false;
                    renderCatPage(d || {});
                },
                error: function () {
                    _catLoading = false;
                    if ($catDialog) {
                        $catDialog.html('<div class="vas-piawdg-pop-state vas-piawdg-pop-error">' + piEsc(msg('VAS_020_LoadError', 'Unable to load invoices.')) + '</div>');
                    }
                }
            });
        }

        function renderCatPage(data) {
            if (!$catDialog) { return; }

            var items = data.Items || [];
            _catTotal    = data.TotalCount != null ? data.TotalCount : items.length;
            _catPage     = data.PageNo || 1;
            _catPageSize = data.PageSize || _catPageSize;
            _catTotalPgs = data.TotalPages || (_catPageSize > 0 ? Math.ceil(_catTotal / _catPageSize) : 0);

            if (_catTotal === 0) {
                $catDialog.html('<div class="vas-piawdg-pop-state">' + piEsc(msg('VAS_020_NoInvoices', 'No data found.')) + '</div>');
                return;
            }

            var rows = '';
            for (var i = 0; i < items.length; i++) { rows += buildCatRow(items[i]); }

            var from = (_catPage - 1) * _catPageSize + 1;
            var to   = Math.min(_catPage * _catPageSize, _catTotal);

            var html = '<div class="vas-piawdg-pop">' +
                '<div class="vas-piawdg-pop-list"><div class="vas-piawdg-pop-rows">' + rows + '</div></div>' +
                '<div class="vas-piawdg-pop-pager">' +
                    pagerHtml(_catPage, _catTotalPgs, from, to, _catTotal, 'vas-piawdg-pop') +
                '</div>' +
            '</div>';
            $catDialog.html(html);

            $catDialog.find('.vas-piawdg-pop-pager .vas-piawdg-pg-prev').on('click', function () {
                if (!_catLoading && _catPage > 1) { loadCatPage(_catPage - 1); }
            });
            $catDialog.find('.vas-piawdg-pop-pager .vas-piawdg-pg-next').on('click', function () {
                if (!_catLoading && _catPage < _catTotalPgs) { loadCatPage(_catPage + 1); }
            });

            // DocumentNo hyperlink -> zoom to the invoice record in its window.
            $catDialog.find('.vas-piawdg-pop-docno-link').on('click', function (e) {
                e.preventDefault();
                piZoomInvoice(parseInt($(this).attr('data-invid'), 10) || 0, $(this).attr('data-sotrx') === '1');
            });

            // Adapt the page size to the popup list height on first render (and on resize) —
            // not on manual navigation, which would flip pageSize and re-clamp the page.
            observeCatList();
            if (_catNeedsSync) { scheduleCatSync(); }
        }

        /* ---- Adaptive modal-row capacity (mirrors VAS_021 / the due list) ---- */
        function scheduleCatSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCatCapacity(); });
        }

        function syncCatCapacity() {
            if (_catLoading || !$catDialog) { return; }
            var el = $catDialog.find('.vas-piawdg-pop-list')[0];
            if (!el) { return; }
            if (_catTotal <= 0) { return; }
            var avail = el.clientHeight;
            if (avail <= 0) {
                if (_catNeedsSync) { scheduleCatSync(); }   // layout not settled — retry
                return;
            }
            var rows = el.querySelectorAll('.vas-piawdg-pop-row');
            var maxH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
            }
            if (maxH > 0) { _catRowH = maxH; }
            var rowH = _catRowH > 0 ? _catRowH : CAT_ROW_FALLBACK;

            _catNeedsSync = false;
            var capacity = Math.max(CAT_MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== _catPageSize) {
                _catPageSize = capacity;
                loadCatPage(_catPage);   // server re-clamps the page to the new total
            }
        }

        function observeCatList() {
            if (typeof ResizeObserver === 'undefined' || !$catDialog) { return; }
            var el = $catDialog.find('.vas-piawdg-pop-list')[0];
            if (!el) { return; }
            if (_catObserver) { _catObserver.disconnect(); }
            _catObserver = new ResizeObserver(function () {
                if (!_catLoading) { syncCatCapacity(); }
            });
            _catObserver.observe(el);
        }

        // Row layout matches the AP / GL search popup: kind chip, then DocNo + status
        // pill over the vendor, then amount (transaction currency) over the date.
        function buildCatRow(it) {
            var st = piStatusMeta(it.DocStatus);
            return '<div class="vas-piawdg-pop-row">' +
                '<span class="vas-piawdg-pop-chip">' + piEsc(msg('VAS_020_Kind', 'Invoice')) + '</span>' +
                '<div class="vas-piawdg-pop-main">' +
                    '<div class="vas-piawdg-pop-docline">' +
                        '<a href="#" class="vas-piawdg-pop-docno vas-piawdg-pop-docno-link" role="link"' +
                            ' data-invid="' + (Number(it.C_Invoice_ID) || 0) + '" data-sotrx="' + (it.IsSOTrx ? '1' : '0') + '"' +
                            ' title="' + piEsc(msg('VAS_020_OpenRecord', 'Open record')) + '">' + piEsc(it.DocumentNo || '') + '</a>' +
                        '<span class="vas-piawdg-pop-status vas-piawdg-pop-status-' + st.tone + '">' + piEsc(st.label) + '</span>' +
                    '</div>' +
                    '<div class="vas-piawdg-pop-title">' + piEsc(it.VendorName || '') + '</div>' +
                '</div>' +
                '<div class="vas-piawdg-pop-meta">' +
                    '<div class="vas-piawdg-pop-amount">' + piEsc(piCatAmt(it.Amount, it.CurCode)) + '</div>' +
                    '<div class="vas-piawdg-pop-date">' + piEsc(piDate(it.DocDate)) + '</div>' +
                '</div>' +
            '</div>';
        }

        /* Zoom to the invoice record in its window (AP / AR resolved from IsSOTrx),
           mirroring the framework zoom used elsewhere (VIS.ZoomTarget.getZoomAD_Window_ID
           + VIS.viewManager.startWindow). The drill-down dialog is closed first so the
           opened window is not left behind the modal. */
        function piZoomInvoice(invoiceId, isSOTrx) {
            if (!invoiceId) { return; }
            // Close the drill-down dialog first so it isn't left over the opened window.
            if ($catDialog) { try { $catDialog.dialog('close'); } catch (e) { } }
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Invoice.C_Invoice_ID=" + invoiceId,
                    "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                    "TabIndex": "0"
                });
            }
            else {
                VAS.ZoomUtil.zoomToRecord("C_Invoice_ID", invoiceId, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
             compact prev / "n of m" / next control right. Hidden on a single page. ---- */
        function pagerHtml(pageNo, totalPages, from, to, total, cls) {
            if (totalPages <= 1) { return ''; }
            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = pageNo >= totalPages ? ' disabled' : '';
            var showing = msg('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + msg('VAS_020_Of', 'of') + ' ' + total;
            return '<div class="vas-piawdg-pager ' + cls + '-pager-row">' +
                '<span class="vas-piawdg-pager-info">' + piEsc(showing) + '</span>' +
                '<div class="vas-piawdg-pager-nav">' +
                    '<button type="button" class="vas-piawdg-pgbtn vas-piawdg-pg-prev" aria-label="' + piEsc(msg('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-piawdg-pager-label">' + pageNo + ' ' + msg('VAS_020_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-piawdg-pgbtn vas-piawdg-pg-next" aria-label="' + piEsc(msg('VAS_020_Next', 'Next')) + '"' + nextDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>' +
                '</div>' +
            '</div>';
        }

        function piDate(iso) {
            if (!iso) { return ''; }
            var p = String(iso).split('-');
            if (p.length !== 3) { return iso; }
            var d = new Date(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        // Full (thousand-separated) transaction-currency amount for the drill-down rows.
        function piCatAmt(amount, curCode) {
            var n = (typeof amount === 'number') ? amount : parseFloat(amount);
            if (isNaN(n)) { n = 0; }
            var prec = _curPrec || 2;
            var formatted = n.toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return (curCode ? curCode + ' ' : '') + formatted;
        }

        function piStatusMeta(code) {
            var map = {
                CO: ['Completed', 'ok'], CL: ['Closed', 'ok'], AP: ['Approved', 'info'],
                DR: ['Draft', 'muted'], IP: ['In Process', 'warn'], WC: ['Waiting Confirm', 'warn'],
                WP: ['Waiting Payment', 'warn'], NA: ['Not Approved', 'err'], IN: ['Invalid', 'err'],
                VO: ['Voided', 'err'], RE: ['Reversed', 'err']
            };
            var c = String(code || '').toUpperCase();
            var m = map[c] || [code || '', 'muted'];
            return { label: m[0], tone: m[1] };
        }

        /* ---- Build a KPI box (clickable: opens the invoice drill-down popup) ---- */
        function piKpiBox(cat, label, count, amount, colorClass) {
            return (
                '<div class="vas-piawdg-kpi-box vas-piawdg-kpi-click" data-cat="' + cat + '" data-label="' + piEsc(label) + '" role="button" tabindex="0">' +
                    '<div class="vas-piawdg-kpi-lbl">' + piEsc(label) + '</div>' +
                    '<div class="vas-piawdg-kpi-val ' + colorClass + '">' + (count || 0) + '</div>' +
                    '<div class="vas-piawdg-kpi-sub">' + piMetric(amount || 0) + ' ' + (msg('VAS_020_Value', 'value')) + '</div>' +
                '</div>'
            );
        }

        /* ---- Helpers ---- */
        function piEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        /* Compact base-currency amount via the shared CurrencyFormat util
           (Indian vs international scale per the base-currency ISO). Sign, then
           symbol, then the compact magnitude. */
        function piMetric(amount) {
            var v = Number(amount || 0);
            var sign = v < 0 ? '-' : '';
            return sign + _curSym + VIS.Util.formatCompactAmount(v, _curIso, _curPrec);
        }

        /* ---- Busy indicator ---- */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        /* ---- Refresh ---- */
        this.refreshWidget = function () {
            if ($catDialog) { try { $catDialog.dialog('close'); } catch (e) { } }
            $self._kpiData = null;
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this._teardown = function () {
            if ($catDialog) { try { $catDialog.dialog('close'); } catch (e) { } }
            if (_dueObserver) { _dueObserver.disconnect(); _dueObserver = null; }
        };

        this.getRoot = function () { return $root; };
    };

    /* Relay a fired value (e.g. open-in-new-mode params) to the registered widget host. */
    VAS.VAS_020_PendingInvoicesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (Scenario 1). */
    VAS.VAS_020_PendingInvoicesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    /* ---- Prototype ---- */
    VAS.VAS_020_PendingInvoicesWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_020_PendingInvoicesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_020_PendingInvoicesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_020_PendingInvoicesWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
