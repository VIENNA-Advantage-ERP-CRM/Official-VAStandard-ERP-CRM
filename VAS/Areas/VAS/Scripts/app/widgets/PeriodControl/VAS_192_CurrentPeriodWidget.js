/**
 * VAS_192_CurrentPeriodWidget
 * 2x1 KPI tile for the Period Control dashboard.
 *
 * Read-only. Shows the accounting period that contains the current server date
 * for the logged-in tenant, resolved from AD_ClientInfo.C_Calendar_ID ->
 * C_Year -> C_Period, with one summarised badge derived from every active
 * C_PeriodControl row of that period. Nothing is hard-coded: month, year,
 * period number and status all come from the accounting calendar.
 *
 * Layout:
 *   line 1  Current Period                       (widget title)
 *   line 2  <C_Period.Name>            [ badge ] (KPI value + status pill)
 *   line 3  <C_Calendar.Name> · <C_Year.FiscalYear> · Period <NN>
 *
 * The widget offers no edit controls and no period open/close actions.
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Current Period                  | VAS_192_CurrentPeriod
 *  2 | Open                            | VAS_192_Open
 *  3 | Partially Open                  | VAS_192_PartiallyOpen
 *  4 | Closed                          | VAS_192_Closed
 *  5 | Permanently Closed              | VAS_192_PermanentlyClosed
 *  6 | Never Opened                    | VAS_192_NeverOpened
 *  7 | Not Configured                  | VAS_192_NotConfigured
 *  8 | No Period Found                 | VAS_192_NoPeriodFound
 *  9 | Check accounting calendar setup | VAS_192_CheckCalendarSetup
 * 10 | Period                          | VAS_192_Period
 * 11 | Couldn't load                   | VAS_192_CouldntLoad
 * 12 | Overlapping periods configured  | VAS_192_OverlappingPeriods
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

    /* Status token -> message key + badge tone. Tones follow the shared dashboard
       semantics (ok / warn / plain / dark / muted); no new global colour system is
       introduced for this widget. The tokens are produced by
       VASLogic.Models.VAS_192_CurrentPeriodModel - keep both sides in lock-step. */
    var STATUS_MAP = {
        'OPEN': { key: 'VAS_192_Open', text: 'Open', tone: 'ok' },
        'PARTIAL': { key: 'VAS_192_PartiallyOpen', text: 'Partially Open', tone: 'warn' },
        'CLOSED': { key: 'VAS_192_Closed', text: 'Closed', tone: 'plain' },
        'PERMCLOSED': { key: 'VAS_192_PermanentlyClosed', text: 'Permanently Closed', tone: 'dark' },
        'NEVER': { key: 'VAS_192_NeverOpened', text: 'Never Opened', tone: 'muted' },
        'NOTCONFIG': { key: 'VAS_192_NotConfigured', text: 'Not Configured', tone: 'warn' },
        'NOPERIOD': { key: 'VAS_192_NotConfigured', text: 'Not Configured', tone: 'warn' }
    };

    VAS.VAS_192_CurrentPeriodWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-192-root">');
        var $card;
        var $valueEl;
        var $pillEl;
        var $metaEl;
        var $busy;

        function label(key, fallback) {
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
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-192-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadPeriod();
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

        function loadPeriod() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_192_CurrentPeriodWidget/GetCurrentPeriod',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    renderPeriod(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        /* Applies the badge tone by swapping a single modifier class so the pill
           never accumulates stale tones across refreshes. */
        function setPill(statusCode) {
            var status = STATUS_MAP[statusCode] || STATUS_MAP['NOTCONFIG'];
            var text = label(status.key, status.text);

            $pillEl
                .removeClass('vas-192-pill-ok vas-192-pill-warn vas-192-pill-plain vas-192-pill-dark vas-192-pill-muted')
                .addClass('vas-192-pill-' + status.tone)
                .text(text)
                .attr('title', text);
        }

        function renderPeriod(data) {
            if (!data.Found) {
                /* Current date sits outside every active period of the calendar.
                   Say so plainly - do not substitute the latest period, the previous
                   period, the first open period, or the calendar month. */
                var noPeriod = label("VAS_192_NoPeriodFound", "No Period Found");
                $valueEl.text(noPeriod).attr('title', noPeriod);
                setPill('NOPERIOD');

                var hint = label("VAS_192_CheckCalendarSetup", "Check accounting calendar setup");
                $metaEl.text(hint).attr('title', hint);
                return;
            }

            var periodName = data.PeriodName || '';
            $valueEl.text(periodName).attr('title', periodName);
            setPill(data.StatusCode);

            /* Calendar Name · Year Name (C_Year.FiscalYear) · Period NN.
               PeriodNoDisplay is already zero-padded server side for display only;
               the stored PeriodNo is untouched. */
            var parts = [];
            if (data.CalendarName) { parts.push(data.CalendarName); }
            if (data.FiscalYear) { parts.push(data.FiscalYear); }
            if (data.PeriodNoDisplay) {
                parts.push(label("VAS_192_Period", "Period") + ' ' + data.PeriodNoDisplay);
            }

            var meta = parts.join(' · ');
            $metaEl.text(meta).attr('title', meta);

            /* An overlapping-period configuration is a data problem, not something
               to hide behind a silently chosen record. */
            $card.toggleClass('vas-192-warn-overlap', !!data.HasOverlap);
            if (data.HasOverlap) {
                var warn = label("VAS_192_OverlappingPeriods", "Overlapping periods configured");
                $metaEl.attr('title', meta + ' · ' + warn);
            }
        }

        function setError() {
            var dash = '—';
            $valueEl.text(dash).removeAttr('title');
            setPill('NOTCONFIG');

            var msg = label("VAS_192_CouldntLoad", "Couldn't load");
            $metaEl.text(msg).attr('title', msg);
            $card.removeClass('vas-192-warn-overlap');
        }

        function createWidget() {
            var title = label("VAS_192_CurrentPeriod", "Current Period");

            /* Three-part stack, same shape as VAS_018: header / value row / meta,
               distributed by the card's space-between. */
            $card = $(
                '<div class="vas-192-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-192-header">' +
                '<div class="vas-192-label">' + escapeHtml(title) + '</div>' +
                '</div>' +
                '<div class="vas-192-valrow">' +
                '<span class="vas-192-value">—</span>' +
                '<span class="vas-192-pill vas-192-pill-muted"></span>' +
                '</div>' +
                '<div class="vas-192-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-192-value');
            $pillEl = $card.find('.vas-192-pill');
            $metaEl = $card.find('.vas-192-meta');

            $root.append($card);

            $busy = $('<div class="vas-192-busy vas-192-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadPeriod();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_192_CurrentPeriodWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_192_CurrentPeriodWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_192_CurrentPeriodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_192_CurrentPeriodWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_192_CurrentPeriodWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
