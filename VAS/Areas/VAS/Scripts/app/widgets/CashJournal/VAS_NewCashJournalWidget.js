/************************************************************
 * Module Name    : VAS
 * Purpose        : Onfinity Cash Journal dashboard quick-action widget
 *                  "New Cash Journal".
 *                  Footprint : 2x1 (col-span-2 row-span-1).
 *                  Single button-tile. Every click opens the Cash
 *                  Journal window in new-record mode via the standard
 *                  widget firevalue-change event - no data fetch, no
 *                  popup. The host frame (listener) decides which
 *                  window to render based on its widget binding.
 *                  Spec ref: PROMPT.md / widget.html supplied with
 *                  the Cash Journal module dashboard.
 * Chronological development:
 *   VIS_045        Created  Date 2026-05-22
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Stylesheet for this widget lives in VAS/Areas/VAS/Content/style.css
     * (block: "Onfinity New Cash Journal quick-action widget"). All
     * variables and classes are namespaced `vas-cj-` so they cannot
     * collide with the app shell or sibling dashboard widgets. */

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

        /* The tile has no data to refresh - keep the hook so the host
         * widget framework's contract is satisfied. */
        this.refreshWidget = function () { /* no-op */ };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-cj-qa-root" id="vas-cj-root-' + widgetID + '"></div>');

            // Plus glyph SVG (Onfinity quick-action style).
            var iconSvg =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<line x1="12" y1="5" x2="12" y2="19"></line>' +
                '<line x1="5" y1="12" x2="19" y2="12"></line>' +
                '</svg>';

            var title = getMsg("VAS_NewCashJournal", "New Cash Journal");
            var copy = getMsg("VAS_NewCashJournalCopy",
                "Open a cash journal or add cash-in / cash-out lines.");

            $btn = $(
                '<button type="button" class="vas-cj-qa-btn"' +
                ' aria-label="' + escapeAttr(title) + '">' +
                '<span class="vas-cj-qa-icon">' + iconSvg + '</span>' +
                '<span class="vas-cj-qa-text">' +
                '<span class="vas-cj-qa-title"></span>' +
                '<span class="vas-cj-qa-copy"></span>' +
                '</span>' +
                '</button>'
            );
            $btn.find(".vas-cj-qa-title").text(title);
            $btn.find(".vas-cj-qa-copy").text(copy);

            $btn.on("click", onTileClick);
            $root.append($btn);
        }

        /* ------------------------------------------------------------ */
        /* Click - fire new-record event                                */
        /* ------------------------------------------------------------ */
        function onTileClick(e) {
            e.preventDefault();
            e.stopPropagation();
            try {
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
            } catch (err) {
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
                    if (v && v !== key) { return v; }
                }
            } catch (e) { /* ignore */ }
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
    };

    VAS.VAS_NewCashJournalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Fired by the widget on click. The host frame registers itself as
     * the listener and reacts by opening the Cash Journal window in
     * new-record mode using the passed windowParam descriptor. */
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
