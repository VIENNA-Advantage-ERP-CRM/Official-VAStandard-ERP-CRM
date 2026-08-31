/**
 * VAS_219_ActiveSetupsWidget
 * 2x1 KPI tile for the Recurring dashboard.
 *
 * Read-only. Answers "how many recurring setups are active and still have
 * remaining runs?" - a single COUNT over C_Recurring produced by
 * VASLogic.Models.VAS_219_ActiveSetupsModel. No SQL and no DB call is made from
 * the client; the widget only fetches the KPI over an asynchronous AJAX call.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   line 1  [icon]  Active Setups        (icon well + widget title)
 *   line 2  248                          (KPI value)
 *
 * States:
 *   loading  busy overlay over the card, value keeps its last rendered text
 *   empty    renders 0 - zero active setups is a real answer, not a takeover
 *   error    value falls back to an em dash and the meta line carries the reason
 *
 * Summary Message Table
 *  # | Current Text  | Message Key
 * ---+---------------+-----------------------------
 *  1 | Active Setups | VAS_219_ActiveSetups
 *  2 | Couldn't load | VAS_219_CouldntLoad
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

    /* Recurrence glyph (inline SVG, not an icon-font class - the host shell does not
       always load an icon font and a missing glyph leaves an empty box). */
    var ICON_RECURRING =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<path d="M17 2l4 4-4 4"></path>' +
        '<path d="M3 11v-1a4 4 0 0 1 4-4h14"></path>' +
        '<path d="M7 22l-4-4 4-4"></path>' +
        '<path d="M21 13v1a4 4 0 0 1-4 4H3"></path>' +
        '</svg>';

    VAS.VAS_219_ActiveSetupsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-219-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;

        var resizeObserver = null;
        var activeRequest = null;

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

        /* The endpoint returns a JSON string inside a JSON response, so the payload
           can arrive double-encoded depending on the host serializer. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-219-hidden', !show);
        }

        /* Group separators follow the user's browser locale; the count is a whole
           number so no decimal separator is involved. */
        function formatCount(value) {
            var n = Number(value);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadActiveSetups();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                resizeObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                resizeObserver.observe($root[0]);
            } catch (e) { }
        }

        function createWidget() {
            var title = label("VAS_219_ActiveSetups", "Active Setups");

            /* Two-part stack distributed by the card's space-between: header row
               (icon well + title) on top, KPI value below. No subtitle - the meta
               line stays empty and collapsed unless the load fails. */
            $card = $(
                '<div class="vas-219-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-219-top">' +
                '<span class="vas-219-icon">' + ICON_RECURRING + '</span>' +
                '<span class="vas-219-label">' + escapeHtml(title) + '</span>' +
                '</div>' +
                '<div class="vas-219-value"><span class="vas-219-value-text">—</span></div>' +
                '<div class="vas-219-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-219-value-text');
            $metaEl = $card.find('.vas-219-meta');

            $root.append($card);

            $busy = $('<div class="vas-219-busy vas-219-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function loadActiveSetups() {
            /* A refresh fired while an earlier request is still open would otherwise
               let the stale response win the race and overwrite the newer count. */
            if (activeRequest) {
                activeRequest.abort();
                activeRequest = null;
            }

            showBusy(true);

            activeRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_219_ActiveSetupsWidget/GetActiveSetups',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data;
                    try {
                        data = parseResponse(res);
                    } catch (e) {
                        setError();
                        return;
                    }

                    if (data.error || !data.Loaded) { setError(); return; }
                    renderCount(data.ActiveSetups);
                },
                error: function (xhr, status) {
                    /* An aborted request is this widget superseding itself, not a
                       failure - the newer request owns the card. */
                    if (status === 'abort') { return; }
                    setError();
                },
                complete: function () {
                    activeRequest = null;
                    showBusy(false);
                }
            });
        }

        /* Zero active setups is a valid result and renders as 0. Only a failed load
           takes the card into its error state. */
        function renderCount(count) {
            var text = formatCount(count);
            $valueEl.text(text).attr('title', text);
            $card.removeClass('vas-219-card-error');
            $metaEl.text('').removeAttr('title');
        }

        function setError() {
            $valueEl.text('—').removeAttr('title');

            var msg = label("VAS_219_CouldntLoad", "Couldn't load");
            $metaEl.text(msg).attr('title', msg);
            $card.addClass('vas-219-card-error');
        }

        /* Called by the platform Refresh button and whenever the host dashboard
           re-broadcasts a record change on the Recurring window. */
        this.refreshWidget = function () {
            loadActiveSetups();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (activeRequest) {
                activeRequest.abort();
                activeRequest = null;
            }
            if (resizeObserver) {
                try { resizeObserver.disconnect(); } catch (e) { }
                resizeObserver = null;
            }
            $root.remove();
        };
    };

    VAS.VAS_219_ActiveSetupsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_219_ActiveSetupsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_219_ActiveSetupsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_219_ActiveSetupsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_219_ActiveSetupsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
