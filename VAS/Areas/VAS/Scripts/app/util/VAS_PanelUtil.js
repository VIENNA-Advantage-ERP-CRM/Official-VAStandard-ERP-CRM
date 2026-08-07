/************************************************************
 * Module Name    : VAS_PanelUtil
 * Purpose        : Shared utility layer for ERP bottom-panel JavaScript components.
 *                  Provides re-usable helpers for callout resolution, context priming,
 *                  logic evaluation, field validation, modal dialogs, button state, and
 *                  notifications - all extracted from individual panel files so every
 *                  new panel can delegate instead of re-implementing.
 * Chronological development:
 *   VAI154   31-Jul-2026  Created
 ************************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    var PU = VAS.PanelUtil = VAS.PanelUtil || {};

    /* ================================================================
       SECTION 1 – CALLOUT EXECUTION
       ================================================================ */

    /**
     * Parses a semicolon-delimited callout string into an array of resolved
     * `{ inst, fn }` pairs sourced from the `VIS.Model` registry.
     *
     * The callout string format follows the AD_Column.Callout convention:
     *   "Namespace.ClassName.methodName;Namespace2.ClassName2.methodName2"
     *
     * The method name comparison is case-insensitive so that DB values like
     * "product" and "Product" both resolve to the same handler.
     *
     * @param {string} calloutStr  Semicolon-separated "Ns.Class.Method" entries.
     * @returns {Array<{inst: object, fn: Function}>}  Resolved pairs; empty when
     *   nothing resolves (missing class, bad format, instantiation error, etc.).
     *
     * @example
     * var chain = VAS.PanelUtil.resolveCallouts("org.compiere.model.CalloutOrder.product");
     * // chain[0] === { inst: <CalloutOrder instance>, fn: <bound product fn> }
     */
    PU.resolveCallouts = function (calloutStr) {
        var out = [];
        var parts = String(calloutStr).split(";");
        for (var i = 0; i < parts.length; i++) {
            var token = parts[i].trim();
            if (!token) continue;
            var seg = token.split(".");
            if (seg.length < 2) continue;
            var method = seg.pop();
            var cls    = seg.pop();
            var ctor   = (window.VIS && VIS.Model) ? VIS.Model[cls] : null;
            if (typeof ctor !== "function") continue;
            var inst;
            try { inst = new ctor(); } catch (e) { continue; }
            var fn = inst[method];
            if (typeof fn !== "function") {
                for (var k in inst) {
                    if (typeof inst[k] === "function" && k.toLowerCase() === method.toLowerCase()) {
                        fn = inst[k];
                        break;
                    }
                }
            }
            if (typeof fn === "function") out.push({ inst: inst, fn: fn });
        }
        return out;
    };

    /**
     * Executes a resolved callout chain, calling each entry in sequence.
     *
     * Each entry's `fn` is invoked with the standard Vienna callout signature:
     *   fn.call(inst, ctxShim, windowNo, mTab, mField, value, null)
     *
     * Per-entry errors are caught and logged to `console.log` so a single
     * failing callout does not abort the rest of the chain.
     *
     * @param {Array<{inst: object, fn: Function}>} chain    Resolved chain from `resolveCallouts`.
     * @param {number}  windowNo   Window number passed as second callout argument.
     * @param {object}  mTab       GridTab shim (see `makeMTabShim`).
     * @param {object}  mField     GridField shim (see `makeFieldShim`).
     * @param {object}  ctxShim    Ctx shim exposing `getContext` and `setContext`.
     * @param {*}       value      The new column value that triggered the callout.
     * @returns {void}
     *
     * @example
     * VAS.PanelUtil.executeCalloutChain(chain, windowNo, mTab, mField, ctx, newValue);
     */
    PU.executeCalloutChain = function (chain, windowNo, mTab, mField, ctxShim, value) {
        for (var i = 0; i < chain.length; i++) {
            try {
                chain[i].fn.call(chain[i].inst, ctxShim, windowNo, mTab, mField, value, null);
            } catch (e) {
                if (window.console) console.log("VAS.PanelUtil callout error (chain[" + i + "])", e);
            }
        }
    };

    /**
     * Creates a minimal GridField shim for a single column, satisfying the interface
     * expected by Vienna framework callout classes.
     *
     * The shim exposes `value`, `getValue`, `setValue`, `getColumnName`,
     * `getDisplayType`, `getAD_Reference_ID`, `isMandatory`, `isReadOnly`, and
     * no-op stubs for all UI mutator methods (`setError`, `setMandatory`,
     * `setReadOnly`, `setDisplayed`, `setDisplayLength`, `setBackgroundColor`,
     * `setInputMandatory`) so callouts that call them do not throw.
     *
     * @param {object}   cfg           Configuration object.
     * @param {Function} cfg.getVal    fn(col: string) → value — reads the current column value.
     * @param {Function} cfg.setVal    fn(col: string, val) — writes a column value.
     * @param {string}   cfg.col       The column name this shim represents.
     * @param {object}   [cfg.meta]    AD_Column metadata record for the column
     *                                 (with `AD_Reference_ID`, `IsMandatory`, `IsUpdateable`).
     * @returns {object}  GridField-compatible shim.
     *
     * @example
     * var mField = VAS.PanelUtil.makeFieldShim({
     *     getVal: function (c) { return line.values[c]; },
     *     setVal: function (c, v) { line.values[c] = v; },
     *     col:    "M_Product_ID",
     *     meta:   columnMeta["M_Product_ID"]
     * });
     */
    PU.makeFieldShim = function (cfg) {
        var m = cfg.meta || null;
        var shim = {
            value:              cfg.getVal(cfg.col),
            getValue:           function ()  { return cfg.getVal(cfg.col); },
            setValue:           function (v) { cfg.setVal(cfg.col, v); shim.value = v; },
            getColumnName:      function ()  { return cfg.col; },
            getDisplayType:     function ()  { return (m && m.AD_Reference_ID) || 0; },
            getAD_Reference_ID: function ()  { return (m && m.AD_Reference_ID) || 0; },
            isMandatory:        function ()  { return !!(m && m.IsMandatory); },
            isReadOnly:         function ()  { return m ? !m.IsUpdateable : false; },
            setError:           function () {},
            setMandatory:       function () {},
            setReadOnly:        function () {},
            setDisplayed:       function () {},
            setDisplayLength:   function () {},
            setBackgroundColor: function () {},
            setInputMandatory:  function () {}
        };
        return shim;
    };

    /**
     * Creates a minimal GridTab shim over a line's value bag, satisfying the interface
     * expected by Vienna framework callout classes.
     *
     * The shim exposes `getValue`, `setValue`, `SetValue` (framework typo alias for
     * `setValue`), `findColumn`, `getField`, and `getKeyColumnName`.
     *
     * @param {object}   cfg                Configuration object.
     * @param {Function} cfg.getVal         fn(col: string) → value — case-insensitive column read.
     * @param {Function} cfg.setVal         fn(col: string, val) — column write.
     * @param {Function} cfg.findColumn     fn(col: string) → 0 (found) | -1 (not found).
     * @param {Function} cfg.getField       fn(col: string) → GridField shim | null.
     * @param {string}   cfg.keyColumnName  Primary-key column name of this tab (e.g. "C_OrderLine_ID").
     * @returns {object}  GridTab-compatible shim.
     *
     * @example
     * var mTab = VAS.PanelUtil.makeMTabShim({
     *     getVal:        function (col)      { return line.values[col]; },
     *     setVal:        function (col, val) { line.values[col] = val; },
     *     findColumn:    function (col)      { return columnMeta[col] ? 0 : -1; },
     *     getField:      function (col)      { return makeFieldShim(line, col); },
     *     keyColumnName: "C_OrderLine_ID"
     * });
     */
    PU.makeMTabShim = function (cfg) {
        return {
            getValue:         function (col)      { return cfg.getVal(col); },
            setValue:         function (col, val) { cfg.setVal(col, val); },
            SetValue:         function (col, val) { cfg.setVal(col, val); },
            findColumn:       function (col)      { return cfg.findColumn(col); },
            getField:         function (col)      { return cfg.getField(col); },
            getKeyColumnName: function ()         { return cfg.keyColumnName; }
        };
    };

    /* ================================================================
       SECTION 2 – CONTEXT HANDLING
       ================================================================ */

    /**
     * Stages header and line values into the Vienna window context so that
     * AD_Val_Rule predicates containing `@TOKEN@` tokens resolve correctly
     * before FK lookups or modal dialogs are opened.
     *
     * Header values are written first; line values are staged second so the
     * current-row value always wins on conflict.  A lowercase-keyed bag is
     * used to collapse DB-cased and dictionary-cased variants of the same
     * column, preventing "same key already added" collisions on the server.
     *
     * Login/accounting-element tokens (`@#...@` / `@$...@`) from `loginContext`
     * are written via the single-argument `ctx.setContext(key, value)` form
     * (no `windowNo`) so they go into the global context namespace.
     *
     * @param {object}   opts                     Configuration object.
     * @param {number}   opts.windowNo             Vienna window number.
     * @param {object}   opts.headerValues         Header column-value pairs to stage first.
     * @param {object}   opts.lineValues           Line column-value pairs to stage second.
     * @param {object}   [opts.loginContext]       `@#` / `@$` global tokens map.
     * @param {object}   [opts.columnNameByLc]     Lowercase-to-canonical column name map
     *                                             (e.g. `{ "c_order_id": "C_Order_ID" }`).
     * @returns {void}
     *
     * @example
     * VAS.PanelUtil.primeContext({
     *     windowNo:       $self.windowNo || 0,
     *     headerValues:   { C_Order_ID: 1001, M_PriceList_ID: 102 },
     *     lineValues:     line.values,
     *     loginContext:   parent.LoginContext,
     *     columnNameByLc: columnNameByLc
     * });
     */
    PU.primeContext = function (opts) {
        if (!window.VIS) return;
        var ctx = VIS.context || (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx());
        if (!ctx || typeof ctx.setContext !== "function") return;

        var wn             = opts.windowNo || 0;
        var headerValues   = opts.headerValues   || {};
        var lineValues     = opts.lineValues     || {};
        var loginContext   = opts.loginContext   || {};
        var columnNameByLc = opts.columnNameByLc || {};

        function coerce(val) {
            if (val === true || val === false) return val ? "Y" : "N";
            if (val instanceof Date) {
                var y  = val.getFullYear();
                var mo = ("0" + (val.getMonth() + 1)).slice(-2);
                var d  = ("0" + val.getDate()).slice(-2);
                return y + "-" + mo + "-" + d;
            }
            if (val == null) return "";
            return String(val);
        }

        var bag = {};

        function stage(col, val) {
            if (col == null) return;
            var lc   = String(col).toLowerCase();
            var name = columnNameByLc[lc] || col;
            bag[lc]  = { name: name, value: coerce(val) };
        }

        for (var hk in headerValues) {
            if (headerValues.hasOwnProperty(hk)) stage(hk, headerValues[hk]);
        }
        for (var lk in lineValues) {
            if (lineValues.hasOwnProperty(lk)) stage(lk, lineValues[lk]);
        }

        for (var key in bag) {
            if (!bag.hasOwnProperty(key)) continue;
            try { ctx.setContext(wn, bag[key].name, bag[key].value); } catch (e) {}
        }

        var glc = loginContext;
        for (var gk in glc) {
            if (!glc.hasOwnProperty(gk)) continue;
            try { ctx.setContext(String(gk), String(glc[gk])); } catch (e) {}
        }
    };

    /**
     * Case-insensitive read of a column value from a plain values object.
     *
     * Tries a direct property lookup first (`values[col]`), then falls back to a
     * linear scan comparing lowercase keys.  Returns `undefined` when absent so
     * callers can distinguish between a missing column and a column whose value is
     * `null` or `0`.
     *
     * @param {object} values  The column-value bag (e.g. `line.values`).
     * @param {string} col     Column name — any casing accepted.
     * @returns {*}  The column value, or `undefined` if not present.
     *
     * @example
     * var qty = VAS.PanelUtil.lineVal(line.values, "QtyOrdered");
     */
    PU.lineVal = function (values, col) {
        if (values[col] !== undefined) return values[col];
        var lc = String(col).toLowerCase();
        for (var k in values) {
            if (values.hasOwnProperty(k) && k.toLowerCase() === lc) return values[k];
        }
        return undefined;
    };

    /**
     * Case-insensitive write of a column value into a plain values object.
     *
     * When an existing key (in any casing) is found it is updated in-place,
     * preventing stale duplicate keys that could shadow the new value on save.
     * When no matching key exists the value is written under `col` as-is.
     *
     * @param {object} values  The column-value bag (e.g. `line.values`).
     * @param {string} col     Column name — any casing accepted.
     * @param {*}      value   The value to assign.
     * @returns {void}
     *
     * @example
     * VAS.PanelUtil.setLineVal(line.values, "QtyOrdered", 5);
     */
    PU.setLineVal = function (values, col, value) {
        if (values[col] === undefined) {
            var lc = String(col).toLowerCase();
            for (var k in values) {
                if (values.hasOwnProperty(k) && k.toLowerCase() === lc) {
                    values[k] = value;
                    return;
                }
            }
        }
        values[col] = value;
    };

    /* ================================================================
       SECTION 3 – LOGIC EVALUATION
       ================================================================ */

    /**
     * Evaluates a Vienna DisplayLogic / ReadOnlyLogic expression against a
     * caller-supplied token resolver.
     *
     * Grammar: tuples joined by `&` (AND) or `|` (OR).  Each tuple is
     * `lhs op rhs` where `op` is one of `=`, `!`, `^` (alias for `!`),
     * `<`, `>`.  Both sides may contain `@Token@` placeholders that are
     * substituted via `resolveToken`.  An unresolved token (still contains
     * `@` after substitution) causes the tuple to evaluate to `dflt` so
     * DisplayLogic defaults to visible and ReadOnlyLogic defaults to editable.
     *
     * When both sides are non-empty and parse as floats the comparison is
     * numeric; otherwise it is string-based.
     *
     * @param {Function} resolveToken  fn(key: string) → string; called for
     *                                 each `@key@` token found in the expression.
     * @param {string}   logic         The logic expression string.
     * @param {boolean}  [dflt=false]  Value returned when the expression is
     *                                 empty or contains unresolvable tokens.
     * @returns {boolean}
     *
     * @example
     * var visible = VAS.PanelUtil.evalLogic(
     *     function (key) { return logicCtxVal(line, key); },
     *     col.DisplayLogic,
     *     false
     * );
     */
    PU.evalLogic = function (resolveToken, logic, dflt) {
        if (dflt === undefined) dflt = false;

        function substitute(s) {
            return String(s).replace(/@([#$\w]+)@/g, function (whole, k) {
                var val = resolveToken(k);
                return (val === null || val === undefined) ? whole : val;
            });
        }

        function evalTuple(tuple) {
            tuple = (tuple || "").trim();
            if (!tuple) return true;
            var m = tuple.match(/^(.*?)(=|!|\^|<|>)(.*)$/);
            if (!m) return true;
            var lv = substitute(m[1]).trim().replace(/^['"]|['"]$/g, "");
            var rv = substitute(m[3]).trim().replace(/^['"]|['"]$/g, "");
            if (lv.indexOf("@") >= 0 || rv.indexOf("@") >= 0) return dflt;
            var ln = parseFloat(lv), rn = parseFloat(rv);
            var numeric = lv !== "" && rv !== "" && !isNaN(ln) && !isNaN(rn);
            switch (m[2]) {
                case "=":           return numeric ? ln === rn : lv === rv;
                case "!": case "^": return numeric ? ln !== rn : lv !== rv;
                case "<":           return numeric ? ln < rn   : lv < rv;
                case ">":           return numeric ? ln > rn   : lv > rv;
            }
            return true;
        }

        var parts = String(logic).match(/[^&|]+|[&|]/g);
        if (!parts) return dflt;
        var result = null, op = null;
        for (var i = 0; i < parts.length; i++) {
            var t = parts[i].trim();
            if (t === "&") { op = "&"; continue; }
            if (t === "|") { op = "|"; continue; }
            if (!t) continue;
            var val = evalTuple(t);
            if (result === null) result = val;
            else if (op === "|") result = result || val;
            else                  result = result && val;
        }
        return result === null ? dflt : result;
    };

    /* ================================================================
       SECTION 4 – VALIDATION
       ================================================================ */

    /**
     * Applies a declarative array of field validation rules against a values
     * object and returns the first violation found.
     *
     * Each rule's `check` function receives the entire `values` bag; it must
     * return `true` when the field IS invalid (fail-fast semantic).  Errors
     * thrown inside `check` are caught, logged, and treated as non-violations
     * so a buggy rule does not block a save entirely.
     *
     * @param {object}  values   Column-value bag to validate (e.g. `line.values`).
     * @param {Array<{col: string, msg: string, check: Function}>} rules
     *   Ordered list of rules.  Each rule:
     *   - `col`   {string}   Column name (returned in the violation object).
     *   - `msg`   {string}   User-facing message (returned in the violation object).
     *   - `check` {Function} fn(values) → boolean; true = INVALID.
     * @returns {{col: string, msg: string}|null}  First violation or `null` when valid.
     *
     * @example
     * var err = VAS.PanelUtil.validateFields(line.values, [
     *     { col: "QtyOrdered", msg: "Qty required", check: function (v) { return !(v.QtyOrdered > 0); } }
     * ]);
     * if (err) { ... }
     */
    PU.validateFields = function (values, rules) {
        for (var i = 0; i < rules.length; i++) {
            var rule = rules[i];
            var invalid = false;
            try { invalid = rule.check(values); } catch (e) {
                if (window.console) console.log("VAS.PanelUtil.validateFields rule error (col=" + rule.col + ")", e);
            }
            if (invalid) return { col: rule.col, msg: rule.msg };
        }
        return null;
    };

    /* ================================================================
       SECTION 5 – MODAL
       ================================================================ */

    /**
     * Creates and opens a reusable modal dialog appended to `<body>`.
     *
     * The dialog structure uses the caller-supplied `cssPrefix` for all class
     * names so the modal's appearance is driven entirely by the panel's own
     * stylesheet with no global style dependencies from this utility.
     *
     * The Done button can be activated by click or by pressing Enter / Space;
     * keydown uses `preventDefault` + `stopPropagation` so the framework global
     * handler does not intercept Enter before the Done handler fires.
     *
     * `opts.onClose` is called both when the user clicks Done (after `onDone`)
     * and when `close()` is called programmatically.  The DOM is removed before
     * `onClose` fires.  `opts.onDone` fires first so commit logic can read DOM
     * values before they are removed.
     *
     * @param {object}   opts               Configuration object.
     * @param {string}   opts.id            ID applied to the backdrop element.
     *                                      The body element gets id `opts.id + "Body"`.
     * @param {string}   opts.cssPrefix     CSS class prefix (e.g. `"vas-obl"`).
     * @param {string}   opts.title         Dialog title text (rendered as-is; escape before
     *                                      passing when sourced from user data).
     * @param {Function} opts.buildBody     fn($body: jQuery) — called synchronously after
     *                                      the dialog is mounted; populate body content here.
     * @param {string}   opts.doneLabel     Label for the Done / primary button.
     * @param {Function} [opts.onDone]      Callback invoked when the Done button is activated
     *                                      (before the dialog closes).
     * @param {Function} [opts.onClose]     Callback invoked after the dialog DOM is removed.
     * @returns {jQuery}  The backdrop jQuery element.
     *
     * @example
     * VAS.PanelUtil.openModal({
     *     id:        "vasOblMore",
     *     cssPrefix: "vas-obl",
     *     title:     "Additional Info",
     *     doneLabel: "Done",
     *     buildBody: function ($body) { $body.html("<p>Content here</p>"); },
     *     onDone:    function () { commitMorePopover(); },
     *     onClose:   function () { morePopoverFor = null; }
     * });
     */
    PU.openModal = function (opts) {
        var p       = opts.cssPrefix;
        var id      = opts.id;
        var bodyId  = id + "Body";

        var $backdrop = $('<div class="' + p + '-dialog-backdrop" id="' + id + '"></div>');
        var $dialog   = $('<div class="' + p + '-dialog"></div>');

        $dialog.html(
            '<header class="' + p + '-dialog__header"><div class="' + p + '-dialog__header-row">' +
            '<h3 class="' + p + '-dialog__title">' + opts.title + '</h3>' +
            '</div></header>' +
            '<div class="' + p + '-dialog__body ' + p + '-more-body ' + p + '-more-grid" id="' + bodyId + '"></div>' +
            '<footer class="' + p + '-dialog__footer ' + p + '-dialog__footer--end">' +
            '<button type="button" class="' + p + '-btn ' + p + '-btn--primary" data-act="modal-done">' + opts.doneLabel + '</button>' +
            '</footer>'
        );

        $backdrop.append($dialog);
        $("body").append($backdrop);

        var $body = $dialog.find("#" + bodyId);

        function close() {
            $backdrop.remove();
            if (typeof opts.onClose === "function") opts.onClose();
        }

        $dialog.on("click", "[data-act=modal-done]", function () {
            if (typeof opts.onDone === "function") opts.onDone();
            close();
        });

        $dialog.on("keydown", "[data-act=modal-done]", function (e) {
            if (e.key === "Enter" || e.key === " " || e.key === "Spacebar" ||
                    e.keyCode === 13 || e.keyCode === 32) {
                e.preventDefault();
                e.stopPropagation();
                if (typeof opts.onDone === "function") opts.onDone();
                close();
            }
        });

        if (typeof opts.buildBody === "function") opts.buildBody($body);

        return $backdrop;
    };

    /* ================================================================
       SECTION 6 – CONTROL / BUTTON STATE
       ================================================================ */

    /**
     * Applies enabled/disabled and visible/hidden state to an array of buttons
     * (or any jQuery-wrapped elements) in a single pass.
     *
     * Uses the native `disabled` property (blocks mousedown/click) together with
     * a CSS class for visual feedback, and a separate CSS class for visibility.
     *
     * Default CSS classes match the VAS order bottom-panel stylesheet:
     *   - disabled: `"vas-obl-is-disabled"`
     *   - hidden:   `"vas-obl-is-hidden"`
     *
     * @param {Array<{$el: jQuery, disabled: boolean, hidden: boolean, disabledCls?: string, hiddenCls?: string}>} buttons
     *   Array of button descriptors.
     *   - `$el`          {jQuery}   The element(s) to update.
     *   - `disabled`     {boolean}  When truthy, sets `disabled` prop and adds `disabledCls`.
     *   - `hidden`       {boolean}  When truthy, adds `hiddenCls`.
     *   - `disabledCls`  {string}   Optional override for the disabled CSS class.
     *   - `hiddenCls`    {string}   Optional override for the hidden CSS class.
     * @returns {void}
     *
     * @example
     * VAS.PanelUtil.applyButtonState([
     *     { $el: $addBtn,  disabled: locked,          disabledCls: "vas-obl-is-disabled" },
     *     { $el: $saveBtn, disabled: locked || n === 0 }
     * ]);
     */
    PU.applyButtonState = function (buttons) {
        var DEFAULT_DISABLED_CLS = "vas-obl-is-disabled";
        var DEFAULT_HIDDEN_CLS   = "vas-obl-is-hidden";
        for (var i = 0; i < buttons.length; i++) {
            var b           = buttons[i];
            var disabledCls = b.disabledCls || DEFAULT_DISABLED_CLS;
            var hiddenCls   = b.hiddenCls   || DEFAULT_HIDDEN_CLS;
            b.$el.prop("disabled", !!b.disabled).toggleClass(disabledCls, !!b.disabled);
            b.$el.toggleClass(hiddenCls, !!b.hidden);
        }
    };

    /* ================================================================
       SECTION 7 – NOTIFICATION
       ================================================================ */

    /**
     * Displays a brief notification message to the user.
     *
     * Delegates to the Vienna framework notification helper
     * `VIS.ADD.Notification(msg)` when available.  When the helper is not
     * present (e.g. during unit testing or on pages that do not load the
     * full framework shell), falls back to a CSS-animated toast element
     * appended to `<body>` with the class `"vas-obl-toast"`.
     *
     * Toast lifecycle:
     *   - After 10 ms  → add `"vas-obl-toast--show"` (triggers CSS transition in).
     *   - After 2600 ms → remove `"vas-obl-toast--show"` (triggers transition out).
     *   - After 300 ms more → remove the element from the DOM.
     *
     * @param {string} msg  The notification text to display.
     * @returns {void}
     *
     * @example
     * VAS.PanelUtil.showToast(VIS.Msg.getMsg("VAS_107_SaveOK") || "Saved");
     */
    PU.showToast = function (msg) {
        if (window.VIS && VIS.ADD && typeof VIS.ADD.Notification === "function") {
            VIS.ADD.Notification(msg);
            return;
        }
        var $t = $('<div class="vas-obl-toast"></div>').text(msg);
        $("body").append($t);
        setTimeout(function () { $t.addClass("vas-obl-toast--show"); }, 10);
        setTimeout(function () {
            $t.removeClass("vas-obl-toast--show");
            setTimeout(function () { $t.remove(); }, 300);
        }, 2600);
    };

})(VAS, jQuery);
