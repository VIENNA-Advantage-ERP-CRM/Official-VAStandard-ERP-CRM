/************************************************************
 * Module Name    : VAS
 * Purpose        : Shared watchdog for body-mounted overlay layers
 *                  (search dropdowns, filter popovers).
 * chronological  : Development
 * Created Date   : 14 August 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * Why this exists
 * ---------------
 * The search widgets mount their results dropdown and their filter popover on <body> so the
 * dashboard cell's overflow:hidden cannot clip them. That also means the layers do NOT go away
 * when the widget does: switching to another window hides the dashboard, but an open popover
 * keeps floating over whatever the framework draws next.
 *
 * There is no framework event for "my window went away", so this watches the layer's own anchor
 * instead. A hidden window collapses the anchor to a 0x0 rect (display:none) or drops it from the
 * document entirely - either is conclusive, whichever way the framework hides it, and it costs one
 * getBoundingClientRect every 300 ms and only while a layer is actually open.
 *
 * Scroll re-anchoring is NOT this object's job - each widget keeps its own capture-phase scroll /
 * resize handler for that.
 *
 * Usage:
 *   var watch = VAS.OverlayWatch({
 *       anchor:   function () { return $bar[0]; },                  // element the layers hang off
 *       isOpen:   function () { return panelOpen() || filtersOpen(); },
 *       onHidden: function () { closePanel(); closeFilters(); }     // fired once, then it stops
 *   });
 *   watch.start();   // whenever a layer opens (idempotent)
 *   watch.stop();    // widget teardown
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.OverlayWatch = function (options) {
        var opts = options || {};
        var timer = null;
        var INTERVAL = 300;

        /* True once the anchor can no longer be seen by the user: detached from the document, or
           laid out as nothing (which is what display:none on any ancestor produces). */
        function anchorGone() {
            var el = opts.anchor ? opts.anchor() : null;
            if (!el || !document.body || !document.body.contains(el)) { return true; }
            var r = el.getBoundingClientRect();
            return r.width === 0 && r.height === 0;
        }

        function tick() {
            /* Nothing open -> nothing to guard; the next open() restarts the timer. */
            if (opts.isOpen && !opts.isOpen()) { stop(); return; }
            if (!anchorGone()) { return; }
            stop();
            if (opts.onHidden) { opts.onHidden(); }
        }

        function start() {
            if (timer) { return; }
            timer = window.setInterval(tick, INTERVAL);
        }

        function stop() {
            if (!timer) { return; }
            window.clearInterval(timer);
            timer = null;
        }

        return { start: start, stop: stop };
    };

})(VAS, jQuery);
