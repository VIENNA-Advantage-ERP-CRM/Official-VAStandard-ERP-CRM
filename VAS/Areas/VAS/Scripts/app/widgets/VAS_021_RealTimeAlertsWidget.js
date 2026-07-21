/************************************************************
 * Module Name    : VAS
 * Purpose        : Real-Time Alerts Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_021_RealTimeAlerts => "Real-time Alerts"
 *   VAS_021_NoAlerts       => "No alerts - all clear"
 *   VAS_021_Dismiss        => "Dismiss"
 *   VAS_Previous           => "Previous"
 *   VAS_Next               => "Next"
 *   VAS_Of                 => "of"
 *   VAS_Showing            => "Showing"
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
       visible width, not the viewport. A single document-level ResizeObserver serves
       every widget; without a marked container — or without ResizeObserver — the CSS
       falls back to 100vw. (Mirrors VIS.OverdueWidget / VAS_018.) */
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

    VAS.VAS_021_RealTimeAlertsWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-rtawdg-root">');
        var $container;
        var $list;         // stable alert-row region (measured for capacity)
        var $footer;       // stable footer band
        var $footHelper;   // "Showing a–b of N"
        var $pageText;     // "p of total"
        var $prevBtn;
        var $nextBtn;
        var widgetID = null;

        /* Adaptive server-side paging state — the client measures how many alert rows
           fit the list region and sends that as pageSize; the server clamps + echoes.
           A ResizeObserver re-measures and re-pages on resize (No-Inner-Scrollbars). */
        var pageNo = 1;
        var pageSize = 5;        // initial guess; replaced by the adaptive measure + server echo
        var totalPages = 0;
        var totalCount = 0;
        var measuredRowH = 0;    // last measured alert-row height (px)
        var loading = false;
        var bodyObserver = null;
        var needsCapacitySync = true; // measure capacity on first load only; resize is handled by the observer

        var MIN_ROWS = 1;        // never force more rows than physically fit (no clipping)
        var ROW_H_FALLBACK = 90; // px, used only before a real row is measured (2-line alert row)

        $self._kpiData = null;

        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        this.intialLoad = function (requestedPage) {
            if (requestedPage < 1) { requestedPage = 1; }
            loading = true;
            $bsyDiv[0].style.visibility = 'visible';
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_021_RealTimeAlertsWidget/GetAlerts',
                data: { pageNo: requestedPage || 1, pageSize: pageSize },
                dataType: 'json',
                async: true,
                success: function (res) {
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    $self._kpiData = data;
                    renderPage(data);
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $self._kpiData = null;
                    renderPage(null);
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                complete: function () { loading = false; }
            });
        };

        /* Page change re-fetches only that page's slice from the controller. */
        function gotoPage(target) {
            if (loading || target < 1 || (totalPages > 0 && target > totalPages) || target === pageNo) { return; }
            $self.intialLoad(target);
        }

        /* Static shell built once so the list region (the ResizeObserver target) and
           the footer controls survive every re-render; only their contents change. */
        function buildShell() {
            $container = $('<div class="vas-rtawdg-container" id="vas_rtawdg_cont_' + widgetID + '">');
            var title = msg('VAS_021_RealTimeAlerts', 'Real-time Alerts');
            $container.html(
                '<div class="vas-rtawdg-header">' +
                    '<div class="vas-rtawdg-title">' +
                        '<svg class="vas-rtawdg-title-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"></circle>' +
                            '<line x1="12" y1="8" x2="12" y2="12"></line>' +
                            '<line x1="12" y1="16" x2="12.01" y2="16"></line>' +
                        '</svg>' +
                        rtaEsc(title) +
                    '</div>' +
                '</div>' +
                '<div class="vas-rtawdg-list"></div>' +
                '<div class="vas-rtawdg-footer">' +
                    '<span class="vas-rtawdg-foot-helper"></span>' +
                    '<div class="vas-rtawdg-pager">' +
                        '<button type="button" class="vas-rtawdg-page-btn vas-rtawdg-page-prev" aria-label="' + rtaEsc(msg('VAS_Previous', 'Previous')) + '">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                        '</button>' +
                        '<span class="vas-rtawdg-page-text"></span>' +
                        '<button type="button" class="vas-rtawdg-page-btn vas-rtawdg-page-next" aria-label="' + rtaEsc(msg('VAS_Next', 'Next')) + '">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                        '</button>' +
                    '</div>' +
                '</div>'
            );

            $list       = $container.find('.vas-rtawdg-list');
            $footer     = $container.find('.vas-rtawdg-footer');
            $footHelper = $container.find('.vas-rtawdg-foot-helper');
            $pageText   = $container.find('.vas-rtawdg-page-text');
            $prevBtn    = $container.find('.vas-rtawdg-page-prev');
            $nextBtn    = $container.find('.vas-rtawdg-page-next');
            $footer.css('visibility', 'hidden');

            $prevBtn.on('click', function () { gotoPage(pageNo - 1); });
            $nextBtn.on('click', function () { gotoPage(pageNo + 1); });

            $root.append($container);
            observeBody();
        }

        function renderPage(data) {
            pageNo     = (data && data.PageNo) ? data.PageNo : 1;
            totalPages = (data && data.TotalPages) ? data.TotalPages : 0;
            totalCount = (data && data.TotalCount != null) ? data.TotalCount : 0;
            if (data && data.PageSize) { pageSize = data.PageSize; }  // adopt the server-clamped size

            renderRows(data);
            renderPager();

            // Measure capacity ONLY on the first load (adapt from the initial guess of 5).
            // Do NOT re-measure on manual navigation — alert rows are variable-height, so
            // re-measuring per page would flip pageSize and re-clamp pageNo mid-navigation.
            // Genuine size changes are handled separately by the ResizeObserver. The flag is
            // cleared inside syncCapacity once it actually measures (not here), so a pre-layout
            // avail<=0 retry still runs.
            if (needsCapacitySync) {
                scheduleCapacitySync();
            }
        }

        function renderRows(data) {
            var alerts = (data && data.Alerts) ? data.Alerts : [];
            if (alerts.length === 0) {
                $list.html(
                    '<div class="vas-rtawdg-empty">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                            '<path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/>' +
                        '</svg>' +
                        rtaEsc(msg('VAS_021_NoAlerts', 'No alerts - all clear')) +
                    '</div>'
                );
                return;
            }
            var html = '';
            for (var i = 0; i < alerts.length; i++) {
                html += rtaBuildRow(alerts[i], i, data);
            }
            $list.html(html);
        }

        function renderPager() {
            if (totalPages <= 1) {
                $footer.css('visibility', 'hidden');
                return;
            }
            $footer.css('visibility', 'visible');

            var from = (pageNo - 1) * pageSize + 1;
            var to   = Math.min(pageNo * pageSize, totalCount);
            $footHelper.text(msg('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + msg('VAS_Of', 'of') + ' ' + totalCount);
            $pageText.text(pageNo + ' ' + msg('VAS_Of', 'of') + ' ' + totalPages);

            $prevBtn.prop('disabled', pageNo <= 1).toggleClass('vas-rtawdg-page-dis', pageNo <= 1);
            $nextBtn.prop('disabled', pageNo >= totalPages).toggleClass('vas-rtawdg-page-dis', pageNo >= totalPages);
        }

        /* ---- Adaptive row count (No-Inner-Scrollbars rule) ---- */

        function scheduleCapacitySync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        /* Measure how many alert rows physically fit the list region and, when that
           differs from the page size we last requested, adopt it and reload the
           current page. Converges in one step (re-render yields the same fit). */
        function syncCapacity() {
            if (loading || !$list || !$list[0]) { return; }
            if (totalCount <= 0) { return; }  // empty-state row is not a real data row to size from

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                // Layout not settled yet — retry next frame while an initial sync is pending.
                if (needsCapacitySync) { scheduleCapacitySync(); }
                return;
            }

            // Alert rows are variable-height (titles wrap to 1–2 lines), so size capacity
            // off the TALLEST rendered row — sizing off the first/shortest row would let a
            // two-line row overflow and clip. Guarantees the whole page fits, never scrolls.
            var rows = $list[0].querySelectorAll('.vas-rtawdg-alert');
            var maxH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
            }
            if (maxH > 0) { measuredRowH = maxH; }
            var rowH = measuredRowH > 0 ? measuredRowH : ROW_H_FALLBACK;

            needsCapacitySync = false;  // measurement succeeded — stop the initial one-shot
            var capacity = Math.max(MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== pageSize) {
                pageSize = capacity;
                $self.intialLoad(pageNo);  // server re-clamps the page to the new total
            }
        }

        function observeBody() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            bodyObserver = new ResizeObserver(function () {
                if (!loading) { syncCapacity(); }
            });
            bodyObserver.observe($list[0]);
        }

        /* Compact currency string for an alert amount using the shared CurrencyFormat
           util (Indian vs international scale per the base-currency ISO). Sign is
           prepended by the caller convention; symbol precedes the magnitude. */
        function rtaFormatAmount(alert, data) {
            var sym = (data && data.CurSymbol) || '';
            var iso = (data && data.CurIso) || '';
            var precision = (data && data.StdPrecision != null) ? data.StdPrecision : 2;
            var value = Number(alert.Amount || 0);
            var sign = value < 0 ? '-' : '';
            return sign + sym + VIS.Util.formatCompactAmount(value, iso, precision);
        }

        function rtaBuildRow(alert, idx, data) {
            var typeClass = 'vas-rtawdg-al-' + (alert.Type || 'info');
            var sub = alert.Subtitle || '';
            if (alert.HasAmount) {
                sub = sub.replace('{AMT}', rtaFormatAmount(alert, data));
            }
            return (
                '<div class="vas-rtawdg-alert ' + typeClass + '" data-idx="' + idx + '">' +
                    '<div class="vas-rtawdg-al-icon">' + (alert.Icon || '!') + '</div>' +
                    '<div class="vas-rtawdg-al-body">' +
                        '<div class="vas-rtawdg-al-title">' + rtaEsc(alert.Title || '') + '</div>' +
                        '<div class="vas-rtawdg-al-sub">' + rtaEsc(sub) + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        function rtaEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        this.refreshWidget = function () {
            $self._kpiData = null;
            pageNo = 1;
            $bsyDiv[0].style.visibility = 'visible';
            $self.intialLoad(1);
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (bodyObserver) { bodyObserver.disconnect(); bodyObserver = null; }
        };
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        // Self-wire the dashboard-width CSS variable (--dash-inline-size) the clamps read.
        ensureDashInlineSizeVar(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
