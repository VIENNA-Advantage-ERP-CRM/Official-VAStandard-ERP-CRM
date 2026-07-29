/************************************************************
 * Module Name    : VAS
 * Purpose        : Quick-action dashboard widget that opens a
 *                  C_Cash record (cash journal) in new-record mode
 *                  on the host Cash Journal screen via
 *                  widgetFirevalueChanged.
 *
 *                  A cash journal carries no IsSoTrx flag, so the
 *                  fired descriptor only asks the host window for a
 *                  new record on its first tab.
 *
 *                  Design: dashboard-widgets.md §"Quick Action Widget" —
 *                    2px #9ED1FF border
 *                    pale blue-to-white gradient surface
 *                    14px radius, 0.85em padding
 *                    solid-blue icon well (top-left) with white "+" glyph
 *                    Regular title pinned to the bottom
 *                  Internal type/spacing in em; borders/radii/shadows in px.
 *                  Mirrors VAS_064_CreateARInvoice.
 *
 * Chronological development:
 *   VIS_045        Created  Date 2026-05-22
 *   VIS_145        Rebuilt on the VAS_064 quick-action pattern  Date 2026-07-28
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_NewCashJournalWidget.css.
       All classes namespaced `vas-ncj-` so they never collide with
       sibling dashboard widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    VAS.VAS_NewCashJournalWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $btn;
        var widgetID = 0;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }

            buildSkeleton();
        };

        /* No data to refresh — keep the hook so the host framework's
           contract is satisfied. */
        this.refreshWidget = function () { /* no-op */ };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-ncj-root" id="vas-ncj-root-' + widgetID + '"></div>');

            var title = getMsg("VAS_NewCashJournal", "New Cash Journal");

            /* Plus glyph — white "+" on the blue icon well. The icon-well CSS
               already sets color:#FFFFFF on the wrapper, so stroke="currentColor"
               keeps the glyph white on the blue square. Inline width/height are
               omitted because .vas-ncj-icon svg already sizes the SVG in em. */
            var iconSvg =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"' +
                ' aria-hidden="true" focusable="false">' +
                '<line x1="12" y1="5" x2="12" y2="19"></line>' +
                '<line x1="5" y1="12" x2="19" y2="12"></line>' +
                '</svg>';

            /* A div (not a button) by request — role/tabindex keep it operable
               and focusable, and the keydown handler restores the Enter / Space
               activation a native button would have given for free. */
            $btn = $(
                '<div class="vas-ncj-card" role="button" tabindex="0" aria-label="' + escapeAttr(title) + '">' +
                '<span class="vas-ncj-icon">' + iconSvg + '</span>' +
                '<span class="vas-ncj-title"></span>' +
                '</div>'
            );
            $btn.find(".vas-ncj-title").text(title);

            $btn.on("click", onTileClick);
            $btn.on("keydown", function (e) {
                if (e.key === "Enter" || e.key === " " || e.key === "Spacebar") {
                    onTileClick(e);
                }
            });
            $root.append($btn);
        }

        /* ------------------------------------------------------------ */
        /* Click — fire new-record event                                */
        /* ------------------------------------------------------------ */
        function onTileClick(e) {
            e.preventDefault();
            e.stopPropagation();
            try {
                /* Standard new-mode descriptor used across VAS quick-action
                   widgets. The host frame registers via addChangeListener and
                   opens the Cash Journal window in new-record mode. A cash
                   journal carries no IsSoTrx flag. */
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
            }
            catch (err) {
                if (window.console) {
                    console.error("VAS_NewCashJournalWidget firevalue failed", err);
                }
            }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */
        function escapeAttr(s) {
            var v = (s === null || s === undefined) ? "" : String(s);
            return v.replace(/&/g, "&amp;").replace(/"/g, "&quot;")
                .replace(/</g, "&lt;").replace(/>/g, "&gt;");
        }

        function getMsg(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === "function") {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== "[") { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        this.getRoot = function () { return $root; };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_NewCashJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_NewCashJournalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Fired on click. The host frame registers itself as the listener
       via addChangeListener() and opens the Cash Journal window in
       new-record mode using the passed windowParam descriptor. */
    VAS.VAS_NewCashJournalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === "function") {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_NewCashJournalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_NewCashJournalWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_NewCashJournalWidget.prototype.dispose = function () {
        if (this.frame && typeof this.frame.dispose === "function") {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
