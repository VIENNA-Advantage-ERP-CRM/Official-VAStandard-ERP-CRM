/************************************************************
 * Module Name    : VAS
 * Purpose        : Quick-action dashboard widget that opens a
 *                  C_Payment record (IsReceipt = 'Y') in new-record
 *                  mode on the host Receipt screen via
 *                  widgetFirevalueChanged.
 *
 *                  Receipt-only — the widget always represents a
 *                  customer receipt. The host AR Receipt window is
 *                  what reacts to the fired event.
 *
 *                  Design: Onfinity Quick Action Widget shell —
 *                    2px dashed #9ED1FF border
 *                    pale blue-to-white gradient surface
 *                    14px radius, 16px padding
 *                    40px blue icon well with white "+" glyph
 *                  All CSS uses em units only (CLAUDE.md rule).
 *
 * Chronological development:
 *   VIS_045        Created  Date 2026-05-28
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_CreateNewReceipt.css.
       All classes namespaced `vas-cnr-` so they never collide with
       sibling dashboard widgets. */

    VAS.VAS_CreateNewReceipt = function () {
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
            $root = $('<div class="vas-cnr-root" id="vas-cnr-root-' + widgetID + '"></div>');

            var title = getMsg("VAS_NewReceipt", "New Receipt");
            var copy = getMsg("VAS_RecordCustomerPayment", "Record a customer payment");

            /* User-plus glyph — matches the supplied "New Lead" mock. The
               icon-well CSS already sets color:#FFFFFF on the wrapper, so
               stroke="currentColor" keeps the glyph white on the blue
               square. Inline width/height are omitted because
               .vas-cnr-icon svg already sizes the SVG in em. */
            var iconSvg =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round"' +
                ' aria-hidden="true" focusable="false">' +
                '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"></path>' +
                '<circle cx="9" cy="7" r="4"></circle>' +
                '<line x1="19" y1="8" x2="19" y2="14"></line>' +
                '<line x1="22" y1="11" x2="16" y2="11"></line>' +
                '</svg>';

            $btn = $(
                '<button type="button" class="vas-cnr-card" aria-label="' + escapeAttr(title) + '">' +
                '<span class="vas-cnr-icon">' + iconSvg + '</span>' +
                '<span class="vas-cnr-title"></span>' +
                '<span class="vas-cnr-copy"></span>' +
                '</button>'
            );
            $btn.find(".vas-cnr-title").text(title);
            $btn.find(".vas-cnr-copy").text(copy);

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
                /* Standard new-mode descriptor used across VAS quick-action
                   widgets. The host frame registers via addChangeListener
                   and opens the AR Receipt window in new-record mode. */
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0",
                    "IsReceipt": "Y"
                };
                $self.widgetFirevalueChanged(windowParam);
            }
            catch (err) {
                if (window.console) {
                    console.error("VAS_CreateNewReceipt firevalue failed", err);
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
    VAS.VAS_CreateNewReceipt.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_CreateNewReceipt.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Fired on click. The host frame registers itself as the listener
       via addChangeListener() and opens the AR Receipt window in
       new-record mode using the passed windowParam descriptor. */
    VAS.VAS_CreateNewReceipt.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === "function") {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_CreateNewReceipt.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_CreateNewReceipt.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_CreateNewReceipt.prototype.dispose = function () {
        if (this.frame && typeof this.frame.dispose === "function") {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
