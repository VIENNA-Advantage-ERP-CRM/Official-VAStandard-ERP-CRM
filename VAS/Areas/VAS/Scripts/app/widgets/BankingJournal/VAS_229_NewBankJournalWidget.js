/************************************************************
 * Module Name    : VAS
 * Purpose        : Quick-action dashboard widget that opens a
 *                  C_BankStatement record (bank journal) in new-record mode
 *                  on the Banking Journal screen (VAS_BankJournal).
 *
 *                  Structure and styling mirror VAS_226_NewRecurring —
 *                  the quick-action tile pattern shared across the VAS
 *                  dashboards. This widget carries no data of its own.
 *
 *                  Two navigation paths, the same split VAS_226 uses:
 *                    - hosted on its own screen (windowNo >= 0) — fire
 *                      widgetFirevalueChanged so the CURRENT window opens a
 *                      new record in its own grid, with no second window;
 *                    - hosted anywhere else — VAS.ZoomUtil opens the
 *                      Banking Journal window itself, resolving AD_Window_ID
 *                      from the window NAME (never a hardcoded id, which
 *                      differs per environment) and caching it for later
 *                      clicks.
 *
 *                  Design: dashboard-widgets.md §"Quick Action Widget", taken
 *                  from VAS_226_NewRecurringWidget value for value so the
 *                  quick-action tiles read as ONE family —
 *                    2px #9ED1FF border
 *                    pale blue-to-white gradient surface
 *                    14px radius, 0.85em padding
 *                    solid-blue icon well (top-left) with white "+" glyph
 *                    Title pinned to the bottom
 *                  Internal type/spacing in em; borders/radii/shadows in px.
 *                  The stylesheet is a mirror of VAS_226's: change both together
 *                  or neither, or the two tiles stop matching on a shared row.
 *
 *                  Summary Message Table
 *                    # | Current Text           | Message Key
 *                   ---+------------------------+----------------------------
 *                    1 | Create Banking Journal | VAS_229_CreateBankingJournal
 *
 * Chronological development:
 *   VAI145         Created  Date 2026-09-02
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_229_NewBankJournalWidget.css.
       All classes namespaced `vas-229-` so they never collide with sibling
       dashboard widgets. */

    /* The window this tile creates a record on, addressed by NAME - never by
       AD_Window_ID, which differs per environment. VAS.ZoomUtil resolves the id from
       the new name, falling back to the classic one and then to
       VAS_ZoomScreenConfig, and caches the result for the page. */
    var ZOOM_TABLE = "C_BankStatement";
    var ZOOM_WINDOW_NAME_NEW = "VAS_BankStatement";
    var ZOOM_WINDOW_NAME_OLD = "Bank Statement";

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

    VAS.VAS_229_NewBankJournalWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $btn;
        var widgetID = 0;

        /* Resolved AD_Window_ID, kept after the first lookup so a second click
           skips the round trip. 0 until VAS.ZoomUtil resolves it. */
        var windowId = 0;

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
            $root = $('<div class="vas-229-root" id="vas-229-root-' + widgetID + '"></div>');

            var title = getMsg("VAS_229_CreateBankingJournal", "Create Banking Journal");

            /* Plus glyph — white "+" on the blue icon well. The icon-well CSS
               already sets color:#FFFFFF on the wrapper, so stroke="currentColor"
               keeps the glyph white. Inline width/height are omitted because
               .vas-229-icon svg already sizes the SVG in em. */
            var iconSvg =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"' +
                ' aria-hidden="true" focusable="false">' +
                '<line x1="12" y1="5" x2="12" y2="19"></line>' +
                '<line x1="5" y1="12" x2="19" y2="12"></line>' +
                '</svg>';

            $btn = $(
                '<button type="button" class="vas-229-card" aria-label="' + escapeAttr(title) + '">' +
                '<span class="vas-229-icon">' + iconSvg + '</span>' +
                '<span class="vas-229-title"></span>' +
                '</button>'
            );
            $btn.find(".vas-229-title").text(title);

            $btn.on("click", onTileClick);
            $root.append($btn);
        }

        /* ------------------------------------------------------------ */
        /* Click — fire new-record event                                */
        /* ------------------------------------------------------------ */
        function onTileClick(e) {
            e.preventDefault();
            e.stopPropagation();
            try {
                if ($self.windowNo >= 0) {
                    // Open a new record on the CURRENT window's grid (no new window).
                    $self.widgetFirevalueChanged({
                        "IsTabInNewMode": "true",
                        "TabIndex": "0"
                    });
                }
                else {
                    /* Placed outside its own screen - open the Banking Journal
                       window itself. Record_ID 0 opens it without positioning on a
                       record, which is what a "new record" action needs. */
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", 0, windowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { windowId = id; }
                        });
                }
            } catch (e) { /* zoom is best-effort */ }
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
    VAS.VAS_229_NewBankJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_229_NewBankJournalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Fired on click. The host frame registers itself as the listener via
       addChangeListener() and opens the Banking Journal window in new-record
       mode using the passed windowParam descriptor. */
    VAS.VAS_229_NewBankJournalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === "function") {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_229_NewBankJournalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_229_NewBankJournalWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_229_NewBankJournalWidget.prototype.dispose = function () {
        if (this.frame && typeof this.frame.dispose === "function") {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
