/************************************************************
 * Module Name    : VAS_PanelShortcuts
 * Purpose        : Reusable keyboard shortcut handler for ERP bottom-panel
 *                  components. Registers Alt+Ctrl+N/S/D/Z/Q on the document
 *                  capture phase and delegates to caller-supplied callbacks.
 *                  Any panel can call VAS.PanelShortcuts.register(opts) during
 *                  its init and store the returned disposer to remove the
 *                  listener when the panel is destroyed.
 * Chronological development:
 *   VAI154   12-Aug-2026  Created
 ************************************************************/
; VAS = window.VAS || {};
; (function (VAS) {

    var PS = VAS.PanelShortcuts = VAS.PanelShortcuts || {};

    /**
     * Registers Alt+Ctrl+N/S/D/Z/Q keyboard shortcuts for a panel and returns
     * a disposer that removes them.
     *
     * The listener is attached to `document` in the CAPTURE phase so it fires
     * before grid-cell editor keydown handlers that call stopPropagation() —
     * without capture, the shortcut is swallowed while any cell editor has
     * focus (e.g. immediately after adding a new line).
     *
     * Key matching is case-insensitive (e.key lowered + e.code fallback) so
     * Caps Lock and keyboard-layout differences have no effect.
     *
     * Shortcuts are silently suppressed when:
     *   - opts.isActive()          returns false  (panel hidden or no record loaded)
     *   - opts.hasBlockingDialog() returns true   (a modal/popover is open)
     *
     * Edge-case handling (no selection, read-only state, unsaved data) is the
     * responsibility of each callback — the utility does not know panel state.
     *
     * @param {object}              opts
     * @param {function():boolean}  opts.isActive            Returns true when the panel
     *                                                        is visible and a record is loaded.
     * @param {function():boolean} [opts.hasBlockingDialog]  Optional. Returns true when
     *                                                        a modal/dialog/popover is open
     *                                                        that should suppress shortcuts.
     * @param {function}           [opts.onNew]              Alt+Ctrl+N — add a new line.
     * @param {function}           [opts.onSave]             Alt+Ctrl+S — save.
     * @param {function}           [opts.onDelete]           Alt+Ctrl+D — delete. The caller
     *                                                        is responsible for any selection
     *                                                        guard and user-feedback toast.
     * @param {function}           [opts.onUndo]             Alt+Ctrl+Z — undo.
     * @param {function}           [opts.onRefresh]          Alt+Ctrl+Q — refresh.
     * @returns {{ dispose: function }}  Call dispose() inside the panel's own dispose()
     *                                   to remove the document listener and avoid leaks.
     */
    PS.register = function (opts) {
        if (!opts || typeof opts.isActive !== "function") {
            throw new Error("VAS.PanelShortcuts.register: opts.isActive is required");
        }

        function handler(e) {
            // Require Ctrl+Alt; reject Shift and Meta (Windows key / Mac Command).
            if (!e.ctrlKey || !e.altKey || e.shiftKey || e.metaKey) return;

            // Panel must be visible and have a record loaded.
            if (!opts.isActive()) return;

            // Caller-supplied guard: any open dialog/popover/modal blocks shortcuts.
            if (typeof opts.hasBlockingDialog === "function" && opts.hasBlockingDialog()) return;

            // Case-insensitive key match with e.code fallback for non-Latin keyboard layouts.
            var k = (e.key || "").toLowerCase();
            var code = e.code;
            var cb = null;

            if      (k === "n" || code === "KeyN") cb = opts.onNew;
            else if (k === "s" || code === "KeyS") cb = opts.onSave;
            else if (k === "d" || code === "KeyD") cb = opts.onDelete;
            else if (k === "z" || code === "KeyZ") cb = opts.onUndo;
            else if (k === "q" || code === "KeyQ") cb = opts.onRefresh;
            else return;   // not one of ours — let the event propagate

            if (typeof cb !== "function") return;   // callback not wired for this key

            e.preventDefault();
            e.stopPropagation();
            cb();
        }

        document.addEventListener("keydown", handler, true);

        return {
            /** Removes the document keydown listener registered by this call. */
            dispose: function () {
                document.removeEventListener("keydown", handler, true);
            }
        };
    };

})(VAS);
