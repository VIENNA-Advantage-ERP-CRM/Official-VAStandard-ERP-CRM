/**
 * VAS_193_PreviousPeriodWidget
 * 2x1 KPI tile for the Period Control dashboard.
 *
 * Read-only. Shows the accounting period that comes immediately BEFORE the
 * period containing the current server date for the logged-in tenant. The
 * server resolves the current period first (AD_ClientInfo.C_Calendar_ID ->
 * C_Year -> C_Period) and then steps back to the previous C_Period row of the
 * same calendar - which may belong to the current fiscal year or to the
 * previous one. Nothing is hard-coded: month, year, period number and status
 * all come from the accounting calendar.
 *
 * Design and markup are identical to VAS_192_CurrentPeriodWidget; only the
 * namespace (vas-193-*), the title and the endpoint differ.
 *
 * Layout:
 *   line 1  Previous Period                       (widget title)
 *   line 2  <C_Period.Name>            [ badge ]  (KPI value + status pill)
 *   line 3  <C_Calendar.Name> · <C_Year.FiscalYear> · Period <NN>
 *
 * The widget offers no edit controls and no period open/close actions.
 *
 * Summary Message Table
 * Only row 1 is new; every other string already exists in the project as a
 * VAS_192_* key and is reused rather than duplicated.
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Previous Period                 | VAS_193_PreviousPeriod        (new)
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
       VASLogic.Models.VAS_193_PreviousPeriodModel through the shared
       VAS_192_CurrentPeriodModel status rule - keep all three sides in lock-step. */
    var STATUS_MAP = {
        'OPEN': { key: 'VAS_192_Open', text: 'Open', tone: 'ok' },
        'PARTIAL': { key: 'VAS_192_PartiallyOpen', text: 'Partially Open', tone: 'warn' },
        'CLOSED': { key: 'VAS_192_Closed', text: 'Closed', tone: 'plain' },
        'PERMCLOSED': { key: 'VAS_192_PermanentlyClosed', text: 'Permanently Closed', tone: 'dark' },
        'NEVER': { key: 'VAS_192_NeverOpened', text: 'Never Opened', tone: 'muted' },
        'NOTCONFIG': { key: 'VAS_192_NotConfigured', text: 'Not Configured', tone: 'warn' },
        'NOPERIOD': { key: 'VAS_192_NotConfigured', text: 'Not Configured', tone: 'warn' }
    };

    VAS.VAS_193_PreviousPeriodWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-193-root">');
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
            $busy.toggleClass('vas-193-hidden', !show);
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
                url: VIS.Application.contextUrl + 'VAS_193_PreviousPeriodWidget/GetPreviousPeriod',
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
                .removeClass('vas-193-pill-ok vas-193-pill-warn vas-193-pill-plain vas-193-pill-dark vas-193-pill-muted')
                .addClass('vas-193-pill-' + status.tone)
                .text(text)
                .attr('title', text);
        }

        function renderPeriod(data) {
            if (!data.Found) {
                /* Either the current date sits outside every active period, or the
                   current period is the first period of the calendar and nothing
                   precedes it. Say so plainly - do not substitute the current
                   period, the latest closed period or the previous calendar month. */
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

            /* Calendar Name · Year Name (C_Year.FiscalYear) · Period NN. The fiscal
               year shown is the previous period's own year, so a period that rolled
               back across the year boundary reads correctly.
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
            $card.toggleClass('vas-193-warn-overlap', !!data.HasOverlap);
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
            $card.removeClass('vas-193-warn-overlap');
        }

        function createWidget() {
            var title = label("VAS_193_PreviousPeriod", "Previous Period");

            /* Three-part stack, same shape as VAS_018 / VAS_192: header / value row /
               meta, distributed by the card's space-between. */
            $card = $(
                '<div class="vas-193-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-193-header">' +
                '<div class="vas-193-label">' + escapeHtml(title) + '</div>' +
                '</div>' +
                '<div class="vas-193-valrow">' +
                '<span class="vas-193-value">—</span>' +
                '<span class="vas-193-pill vas-193-pill-muted"></span>' +
                '</div>' +
                '<div class="vas-193-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-193-value');
            $pillEl = $card.find('.vas-193-pill');
            $metaEl = $card.find('.vas-193-meta');

            $root.append($card);

            $busy = $('<div class="vas-193-busy vas-193-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_193_PreviousPeriodWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_193_PreviousPeriodWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_193_PreviousPeriodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_193_PreviousPeriodWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_193_PreviousPeriodWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
