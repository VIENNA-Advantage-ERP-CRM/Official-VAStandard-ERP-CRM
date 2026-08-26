// VAS_218_CreateOppLines.js
// Module  : VAS_218 – Create Opp Lines Bottom Panel
// Purpose : Bottom-panel tab for managing opportunity lines on the CRM Opportunity window.
//           Adapted from VAS_107_CreateOrderBottomPanel.js
// Author  : NT
// Date    : 21-Aug-2026
// Chronological development:
//   NT   21-Aug-2026  Created (adapted from VAS_107)

;VAS = window.VAS || {};
(function (VAS, $) {
    "use strict";

    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────
    var LINE_PAGE_SIZE = 20;

    /** Columns that live in the panel model (used for payload filtering). */
    var PANEL_COLS = {
        M_Product_ID: 1, C_Charge_ID: 1, M_AttributeSetInstance_ID: 1,
        PlannedQty: 1, C_UOM_ID: 1, PlannedPrice: 1, PlannedAmt: 1,
        Description: 1, VAS_Opportunity_ID: 1
    };

    /** Columns whose values are surrogate IDs (need separate display text). */
    var ID_COLS = {
        M_Product_ID: 1, C_Charge_ID: 1, C_UOM_ID: 1,
        M_AttributeSetInstance_ID: 1, C_Currency_ID: 1
    };

    /** Maps logical field roles to actual DB column names. */
    var FIELD_COL = {
        uom: "C_UOM_ID",
        quantity: "PlannedQty",
        price: "PlannedPrice",
        description: "Description"
    };

    /** Tab-key order among editable cells in each row. */
    var TAB_ORDER = ["primary", "description", "quantity", "uom", "price", "more"];

    /** Maps TAB_ORDER role names to editing.field values. */
    var ROLE_TO_FIELD = {
        primary:     "product",
        description: "description",
        quantity:    "quantity",
        uom:         "uom",
        price:       "price"
    };

    /** Additional Info modal field list (no tax-related fields). */
    var ADDITIONAL_INFO_FIELDS = [
        { col: "AD_OrgTrx_ID" },
        { col: "C_Project_ID" },
        { col: "C_Campaign_ID" },
        { col: "C_Activity_ID" }
    ];

    /** Groups shown in the More popover (no Discount / Notes). */
    var MORE_FIELD_GROUPS = [
        { anchor: "AD_OrgTrx_ID", key: "VAS_218_GrpDimension", def: "Dimension", collapsed: false }
    ];

    /** Callout map – VAS_218 has no default callouts. */
    var DEFAULT_CALLOUTS = {};

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────
    /**
     * @constructor
     * @param {object} params - Initialisation parameters passed by the tab panel framework.
     */
    VAS.VAS_218_CreateOppLines = function (params) {
        params = params || {};
        this._params        = params;
        this._adWindowId    = params.adWindowId    || 0;
        this._adTabId       = params.adTabId       || 0;
        this._adTableId     = params.adTableId     || 0;
        this._ctx           = params.ctx           || null;
        this._isInitialized = false;
        this._isBusy        = false;
        this._container     = null;
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Prototype
    // ─────────────────────────────────────────────────────────────────────────
    VAS.VAS_218_CreateOppLines.prototype = (function () {

        // ── Shared closed-over state ─────────────────────────────────────────
        var self;               // reference to the current instance
        var $self;              // jQuery wrapper of the root DOM element
        var parent = null;      // header record context (opportunity)
        var lines  = [];        // array of line objects for the current page
        var adWindowId = 0;
        var adTabId    = 0;

        var linePage        = 1;
        var totalLineCount  = 0;
        var otherAmt        = 0;  // planned amount from other pages

        var columnMeta      = null; // AD_Column metadata array
        var colMetaByName   = {};   // column metadata indexed by ColumnName

        // Lookup caches
        var uomCache        = {};   // C_UOM_ID → name

        // Edit state
        var activeCell      = null; // { lineIdx, role }
        var editing         = null; // { rowId: string, field: string } — which cell is in edit mode
        // Catalog search state — matches VAS_107 shape for paged scroll-loading
        var catalog         = { results: [], highlight: 0, seq: 0, offset: 0, hasMore: true, loading: false, term: "", debounce: null, $pop: null, $inp: null };
        var attrState       = null; // attribute dialog state
        var scanState       = null; // barcode scan dialog state
        var morePopoverFor  = null; // line index that has the More popover open

        // DOM references
        var $grid           = null;
        var $linesBody      = null;
        var $totalsRow      = null;
        var $pager          = null;
        var $saveBtn        = null;
        var $addBtn         = null;
        var $deleteBtn      = null;
        var $refreshBtn     = null;
        var $selectAll      = null;
        var $busy           = null;   // full-panel busy overlay (vis-apanel-busy)

        // ── Full-panel busy overlay (same pattern as VAS_107) ───────────────

        /**
         * Creates the full-panel busy overlay and appends it to the document body.
         * Using position:fixed on body ensures the overlay covers the entire panel
         * regardless of scroll position or overflow clipping on ancestor elements.
         * Must be called after $self is assigned in init().
         */
        function createBusyIndicator() {
            $busy = $('<div class="vas-ol-busy-overlay" style="display:none;"></div>');
        }

        /**
         * Shows or hides the full-panel busy overlay.
         * Appends to / removes from body so it is never clipped by panel overflow.
         * @param {boolean} show
         */
        function showBusy(show) {
            if (!$busy) return;
            if (show) {
                if (!$busy.parent().length) $("body").append($busy);
                $busy.show();
            } else {
                $busy.hide();
            }
        }

        // ── Utility helpers ──────────────────────────────────────────────────

        /**
         * Translates a message key using VIS.Msg.getMsg with a fallback to the key itself.
         * @param {string} key - Message key (e.g. "VAS_218_NoLines").
         * @returns {string} Translated text.
         */
        function msg(key) {
            if (VIS && VIS.Msg && VIS.Msg.getMsg) {
                var t = VIS.Msg.getMsg(key);
                return (t && t !== key) ? t : key;
            }
            return key;
        }

        /**
         * Translates a label key; returns def when VIS.Msg is unavailable.
         * @param {string} key - Message key.
         * @param {string} def - Default/fallback text.
         * @returns {string}
         */
        function lbl(key, def) {
            if (VIS && VIS.Msg && VIS.Msg.getMsg) {
                var t = VIS.Msg.getMsg(key);
                return (t && t !== key) ? t : (def || key);
            }
            return def || key;
        }

        /**
         * HTML-escapes a value to prevent XSS when building HTML strings.
         * @param {*} v - Raw value.
         * @returns {string} Escaped string safe for HTML attribute and text contexts.
         */
        function esc(v) {
            if (v == null) return "";
            return String(v)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#39;");
        }

        /**
         * Parses a string or number to a float; returns 0 for invalid input.
         * @param {*} v
         * @returns {number}
         */
        function parseNum(v) {
            var n = parseFloat(String(v).replace(/,/g, ""));
            return isNaN(n) ? 0 : n;
        }

        /**
         * Formats a number as a money string with two decimal places.
         * @param {number} n
         * @returns {string}
         */
        function fmtMoney(n) {
            if (n == null || isNaN(n)) return "0.00";
            return parseFloat(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
        }

        /**
         * Formats a quantity value showing at least 2 decimal places (up to 6),
         * stripping trailing zeros beyond the 2nd place.
         * Examples: 8 → "8.00", 8.5 → "8.50", 8.1234 → "8.1234"
         * @param {number} n
         * @returns {string}
         */
        function fmtQty(n) {
            if (n == null || isNaN(n)) return "0.00";
            var s = parseFloat(n).toFixed(6);
            // Strip trailing zeros but always keep at least 2 decimal places
            s = s.replace(/(\.\d{2})(\d*?)0+$/, "$1$2").replace(/(\.\d{2,}?)0+$/, "$1");
            return s;
        }

        /**
         * Returns the value of a column in a line object, normalising casing.
         * @param {object} line
         * @param {string} col - Column name (Pascal case).
         * @returns {*}
         */
        function lineVal(line, col) {
            if (!line || !line.values) return null;
            var v = line.values;
            if (v.hasOwnProperty(col)) return v[col];
            // Try lowercase lookup (PostgreSQL returns lowercase column names)
            var lc = col.toLowerCase();
            if (v.hasOwnProperty(lc)) return v[lc];
            return null;
        }

        // ── Amount / totals helpers ──────────────────────────────────────────

        /**
         * Returns the PlannedAmt for a single line.
         * @param {object} line
         * @returns {number}
         */
        function lineAmount(line) {
            return +lineVal(line, "PlannedAmt") || 0;
        }

        // ── Column metadata helpers ──────────────────────────────────────────

        /**
         * Builds colMetaByName from the columnMeta array returned by the server.
         */
        function indexColumnMeta() {
            colMetaByName = {};
            if (!columnMeta) return;
            for (var i = 0; i < columnMeta.length; i++) {
                var c = columnMeta[i];
                if (c && c.ColumnName) colMetaByName[c.ColumnName] = c;
            }
        }

        /**
         * Returns the AD_Column metadata object for the given column name.
         * @param {string} col
         * @returns {object|null}
         */
        function colMeta(col) {
            return colMetaByName[col] || null;
        }

        /**
         * Returns the callout string for a column (from metadata or DEFAULT_CALLOUTS).
         * @param {string} col
         * @returns {string}
         */
        function columnCalloutStr(col) {
            var m = colMeta(col);
            if (m && m.Callout) return m.Callout;
            return DEFAULT_CALLOUTS[col] || "";
        }

        // ── Callout infrastructure ───────────────────────────────────────────

        /**
         * Builds a minimal Ctx shim for running framework callout classes client-side.
         * @param {object} line - The line object being edited.
         * @returns {object} Ctx shim.
         */
        function makeCalloutCtx(line) {
            var v = line.values || {};
            return {
                VAS_Opportunity_ID:          parent ? parent.VAS_Opportunity_ID          : 0,
                M_PriceList_Version_ID:      parent ? parent.M_PriceList_Version_ID      : 0,
                C_BPartner_ID:               parent ? parent.C_BPartner_ID               : 0,
                C_EnquiryRdate:              parent ? parent.C_EnquiryRdate              : null,
                AD_Client_ID:                parent ? parent.AD_Client_ID                : 0,
                AD_Org_ID:                   parent ? parent.AD_Org_ID                   : 0,
                M_Product_ID:                v.M_Product_ID    || 0,
                C_Charge_ID:                 v.C_Charge_ID     || 0,
                C_UOM_ID:                    v.C_UOM_ID        || 0,
                PlannedQty:                  v.PlannedQty      || 0,
                PlannedPrice:                v.PlannedPrice    || 0,
                PlannedAmt:                  v.PlannedAmt      || 0
            };
        }

        /**
         * Builds a minimal MTab shim for running framework callout classes client-side.
         * @param {object} line
         * @returns {object}
         */
        function makeMTab(line) {
            var v = line.values || {};
            return {
                keyColumnName: "VAS_OppLines_ID",
                getKeyNo: function () { return v.VAS_OppLines_ID || 0; },
                getValue: function (col) { return fieldGet(line, col); },
                setValue: function (col, val) { fieldSet(line, col, val); }
            };
        }

        /**
         * Gets a field value from a line, supporting special header-sourced fields.
         * @param {object} line
         * @param {string} col
         * @returns {*}
         */
        function fieldGet(line, col) {
            var v = line.values || {};
            switch (col) {
                case "VAS_Opportunity_ID":   return parent ? parent.VAS_Opportunity_ID        : 0;
                case "PlannedQty":           return v.PlannedQty  || 0;
                case "PlannedPrice":         return v.PlannedPrice || 0;
                case "PlannedAmt":           return v.PlannedAmt  || 0;
                case "M_Product_ID":         return v.M_Product_ID || 0;
                case "C_Charge_ID":          return v.C_Charge_ID  || 0;
                case "C_UOM_ID":             return v.C_UOM_ID     || 0;
                case "Description":          return v.Description  || "";
                default:                     return v[col] !== undefined ? v[col] : null;
            }
        }

        /**
         * Returns true when two field values are semantically equal so that
         * fieldSet can skip the dirty-mark when the user leaves a cell unchanged.
         * Numeric fields: null/undefined treated as 0; floating-point safe.
         * String fields: null/undefined treated as empty string.
         * @param {*} a
         * @param {*} b
         * @returns {boolean}
         */
        function sameVal(a, b) {
            if (a === b) return true;
            var na = (a == null) ? 0 : +a;
            var nb = (b == null) ? 0 : +b;
            if (!isNaN(na) && !isNaN(nb)) return na === nb;
            return String(a == null ? "" : a) === String(b == null ? "" : b);
        }

        /**
         * Sets a field value on a line, marking the column as touched and the
         * line as dirty. No-ops when the new value is identical to the current
         * value so that clicking a cell without editing does not dirty the line.
         * @param {object} line
         * @param {string} col
         * @param {*} val
         */
        function fieldSet(line, col, val) {
            if (!line.values) line.values = {};
            if (sameVal(line.values[col], val)) return;
            line.values[col] = val;
            if (!line.touchedCols) line.touchedCols = {};
            line.touchedCols[col] = true;
            line._dirty = true;
        }

        /**
         * Resolves callout class names from a callout string (semicolon-separated).
         * @param {string} calloutStr
         * @returns {Array} Array of callout instances.
         */
        function resolveCallouts(calloutStr) {
            if (!calloutStr) return [];
            var results = [];
            var parts = calloutStr.split(";");
            for (var i = 0; i < parts.length; i++) {
                var p = parts[i].trim();
                if (!p) continue;
                try {
                    // Attempt to resolve the class from the VIS callout registry
                    if (VIS && VIS.CalloutEngine && VIS.CalloutEngine.getCallout) {
                        var co = VIS.CalloutEngine.getCallout(p);
                        if (co) results.push(co);
                    }
                } catch (e) {
                    // Callout class not available client-side — will fall back to server
                }
            }
            return results;
        }

        /**
         * Marks a line as dirty (unsaved changes), making it eligible for Save.
         * @param {object} line
         */
        function markDirty(line) {
            line._dirty = true;
        }

        /**
         * Builds the spinning indicator HTML appended to a row while a callout
         * or save is in flight. Matches the VAS_107 pattern.
         * @param {string} label - Accessible aria-label text (e.g. "Saving…").
         * @returns {string} HTML string.
         */
        function rowSpinHtml(label) {
            return '<span class="vas-ol-row-spin" aria-label="' + esc(label || "") + '"></span>';
        }

        /**
         * Toggles the busy-spinner on the row DOM element without a full re-render,
         * so the spinner paints while a callout or save is in flight.
         * @param {object}  line  - Line to toggle.
         * @param {boolean} on    - true = show spinner, false = hide.
         * @param {string}  label - Optional aria-label shown on the spinner.
         */
        function setRowBusy(line, on, label) {
            line._busy = on;
            if (!$linesBody) return;
            var $r = $linesBody.find('[data-rowid="' + line.rowId + '"]');
            $r.toggleClass("is-busy", on);
            $r.find(".vas-ol-row-spin").remove();
            if (on) $r.append(rowSpinHtml(label || msg("VAS_218_Calculating")));
        }

        /**
         * Runs the callout for the changed column, then calls done() when finished.
         *
         * M_Product_ID / C_Charge_ID / M_AttributeSetInstance_ID always go to the
         * server so price-list lookup, UOM default and amount calculation happen in
         * one authoritative round-trip — matching the VAS_107 pattern.
         *
         * @param {object}   line - Line being edited.
         * @param {string}   col  - ColumnName that changed.
         * @param {function} done - Optional callback invoked after the callout completes.
         */
        function runCallout(line, col, done) {
            if (!parent) { if (done) done(); return; }
            var v = line.values || {};
            if (!v.M_Product_ID && !v.C_Charge_ID) {
                renderRow(lines.indexOf(line));
                if (done) done();
                return;
            }
            // Ensure a default quantity so price-list and discount-break logic has a value.
            if (!(v.PlannedQty > 0)) v.PlannedQty = 1;

            // Product / charge / ASI: always use the server path for a full round-trip.
            if (col === "M_Product_ID" || col === "C_Charge_ID" || col === "M_AttributeSetInstance_ID") {
                setRowBusy(line, true);
                runServerCallout(line, col, function () {
                    setRowBusy(line, false);
                    renderRow(lines.indexOf(line));
                    if (done) done();
                });
                return;
            }

            // Other columns: try the AD_Column.Callout chain client-side first.
            var calloutStr = columnCalloutStr(col);
            var chain = calloutStr ? resolveCallouts(calloutStr) : null;
            if (chain && chain.length) {
                setRowBusy(line, true);
                // Run on next tick so the busy indicator paints before the
                // synchronous callout blocks the thread.
                setTimeout(function () {
                    try {
                        var ctx    = makeCalloutCtx(line);
                        var mTab   = makeMTab(line);
                        var mField = { ColumnName: col, getValue: function () { return fieldGet(line, col); } };
                        for (var i = 0; i < chain.length; i++) {
                            try { chain[i].start(ctx, null, mTab, mField, null); } catch (e) {
                                if (VIS && VIS.log) VIS.log.severe("VAS_218 callout error: " + e);
                            }
                        }
                        sanitizeLine(line);
                        applyCalloutReadback(line);
                    } finally {
                        setRowBusy(line, false);
                        renderRow(lines.indexOf(line));
                        if (done) done();
                    }
                }, 0);
                return;
            }

            // Server fallback for all other columns.
            setRowBusy(line, true);
            runServerCallout(line, col, function () {
                setRowBusy(line, false);
                renderRow(lines.indexOf(line));
                if (done) done();
            });
        }

        /**
         * Issues a GET to the RunCallout controller action with individual line-value params,
         * matching the controller's method signature exactly.
         * @param {object}   line - Line being edited.
         * @param {string}   col  - TriggerColumn for the server callout.
         * @param {function} done - Called after the response is applied (or on error).
         */
        function runServerCallout(line, col, done) {
            var v = line.values || {};
            $.ajax({
                url:      VIS.Application.contextUrl + "VAS_218_CreateOppLines/RunCallout",
                type:     "GET",
                dataType: "json",
                data: {
                    VAS_Opportunity_ID:        parent ? parent.VAS_Opportunity_ID : 0,
                    TriggerColumn:             col,
                    M_Product_ID:              v.M_Product_ID              || 0,
                    C_Charge_ID:               v.C_Charge_ID               || 0,
                    M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0,
                    PlannedQty:                v.PlannedQty                || 1,
                    C_UOM_ID:                  v.C_UOM_ID                  || 0,
                    PlannedPrice:              v.PlannedPrice               || 0,
                    PriceOverride:             !!line._priceOverride
                },
                success: function (raw) {
                    try {
                        var res = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        if (res) { applyPatch(line, res); sanitizeLine(line); applyCalloutReadback(line); }
                    } catch (e) {
                        if (VIS && VIS.log) VIS.log.severe("VAS_218 server callout parse error: " + e);
                    }
                    if (done) done();
                },
                error: function () { if (done) done(); }
            });
        }

        /**
         * Applies a patch object returned by the server callout to a line's values.
         * @param {object} line
         * @param {object} patch - { Values: {}, Display: {} }
         */
        function applyPatch(line, patch) {
            if (!patch) return;
            var vals = patch.Values || {};
            for (var k in vals) {
                if (vals.hasOwnProperty(k)) {
                    fieldSet(line, k, vals[k]);
                }
            }
            var disp = patch.Display || {};
            if (!line.display) line.display = {};
            for (var dk in disp) {
                if (disp.hasOwnProperty(dk)) {
                    line.display[dk] = disp[dk];
                }
            }
        }

        /**
         * Normalises numeric fields on a line after a callout runs.
         * @param {object} line
         */
        function sanitizeLine(line) {
            if (!line || !line.values) return;
            var v = line.values;
            v.PlannedQty   = parseNum(v.PlannedQty);
            v.PlannedPrice = parseNum(v.PlannedPrice);
            v.PlannedAmt   = parseNum(v.PlannedAmt);
        }

        /**
         * Reads back callout-updated display labels from the line values.
         * @param {object} line
         */
        function applyCalloutReadback(line) {
            if (!line || !line.values) return;
            var v = line.values;
            if (!line.display) line.display = {};
            // Sync UOM name from cache if available
            if (v.C_UOM_ID && uomCache[v.C_UOM_ID]) {
                line.display.uomName = uomCache[v.C_UOM_ID];
            }
        }

        // ── Data fetching ────────────────────────────────────────────────────

        /**
         * Fetches line data for the given opportunity from the server.
         * @param {number} oppId - VAS_Opportunity_ID
         * @param {number} page  - 1-based page number
         */
        function fetchData(oppId, page) {
            if (!oppId) return;
            setGridBusy(true);
            // Server uses 0-based page; callers pass 1-based, so subtract 1.
            var reqPage = (typeof page === "number" && page >= 1) ? page - 1 : 0;
            $.ajax({
                url:      VIS.Application.contextUrl + "VAS_218_CreateOppLines/GetPanelData",
                type:     "GET",
                dataType: "json",
                data:     { VAS_Opportunity_ID: oppId, AD_Window_ID: adWindowId, page: reqPage },
                success: function (raw) {
                    setGridBusy(false);
                    try {
                        // Controller double-serialises: jQuery parses outer JSON, we parse inner.
                        var res = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        parent         = res || null;
                        var cols       = (parent && parent.Columns) || [];
                        columnMeta     = [];
                        for (var c = 0; c < cols.length; c++) columnMeta.push(cols[c]);
                        indexColumnMeta();
                        totalLineCount = (parent && parent.LinesTotal) || 0;
                        linePage       = parent ? (+parent.LinePage + 1) : 1;
                        otherAmt       = (parent && +parent.OtherPagesPlannedAmt) || 0;
                        lines          = [];
                        var rows       = (parent && parent.Lines) || [];
                        for (var i = 0; i < rows.length; i++) lines.push(fromServerRow(rows[i]));
                        renderAll();
                    } catch (e) {
                        if (VIS && VIS.log) VIS.log.severe("VAS_218 fetchData parse error: " + e);
                        showToast(msg("VAS_218_LoadError"), true);
                    }
                },
                error: function () {
                    setGridBusy(false);
                    showToast(msg("VAS_218_LoadError"), true);
                }
            });
        }

        /**
         * Maps a server-returned row object to the internal line format,
         * normalising column-name casing (PostgreSQL returns lowercase).
         * @param {object} row - Raw row from the server.
         * @returns {object} Normalised line object.
         */
        function fromServerRow(row) {
            // Build a case-normalised values map
            var vals = {};

            // Helper: read from row with case-insensitive fallback
            function pick(name) {
                if (row.hasOwnProperty(name)) return row[name];
                var lc = name.toLowerCase();
                if (row.hasOwnProperty(lc)) return row[lc];
                return null;
            }

            // Use parseInt/parseFloat directly — Util is a C# server-side helper,
            // not a JS global.
            vals.VAS_OppLines_ID            = parseInt(pick("VAS_OppLines_ID"),  10) || 0;
            vals.Line                        = parseInt(pick("Line"),              10) || 0;
            vals.M_Product_ID               = parseInt(pick("M_Product_ID"),      10) || 0;
            vals.C_Charge_ID                = parseInt(pick("C_Charge_ID"),       10) || 0;
            vals.M_AttributeSetInstance_ID  = parseInt(pick("M_AttributeSetInstance_ID"), 10) || 0;
            vals.PlannedQty                 = parseNum(pick("PlannedQty"));
            vals.C_UOM_ID                   = parseInt(pick("C_UOM_ID"),          10) || 0;
            vals.PlannedPrice               = parseNum(pick("PlannedPrice"));
            vals.PlannedAmt                 = parseNum(pick("PlannedAmt"));
            vals.Description                = pick("Description") == null ? "" : String(pick("Description"));
            // Additional info fields
            vals.AD_OrgTrx_ID               = parseInt(pick("AD_OrgTrx_ID"),     10) || 0;
            vals.C_Project_ID               = parseInt(pick("C_Project_ID"),      10) || 0;
            vals.C_Campaign_ID              = parseInt(pick("C_Campaign_ID"),     10) || 0;
            vals.C_Activity_ID              = parseInt(pick("C_Activity_ID"),     10) || 0;

            // Carry through any extra columns returned by the server
            for (var k in row) {
                if (row.hasOwnProperty(k) && !vals.hasOwnProperty(k) && !vals.hasOwnProperty(k.toLowerCase())) {
                    vals[k] = row[k];
                }
            }

            // Display labels — use plain string coercion, no C# Util dependency
            var disp = {
                productName:      pick("ProductName") || pick("productname") || "",
                chargeName:       pick("ChargeName")  || pick("chargename")  || "",
                uomName:          pick("UOMName")     || pick("UomName")     || pick("uomname") || "",
                attrName:         pick("AttrName")    || pick("attrname")    || "",
                hasAttributeSet:  (pick("HasAttributeSet") || pick("hasattributeset")) === true ||
                                  (pick("HasAttributeSet") || pick("hasattributeset")) === "Y"
            };

            // Seed UOM cache
            if (vals.C_UOM_ID && disp.uomName) uomCache[vals.C_UOM_ID] = disp.uomName;

            var line = {
                rowId:       "r" + (vals.VAS_OppLines_ID || Math.random()),
                values:      vals,
                display:     disp,
                touchedCols: {},
                _dirty:      false,
                _isNew:      false,
                _lk:         {}   // lookup sub-objects per column (e.g. UOM dropdown list)
            };
            line._saved = snapshotLine(line); // baseline for Undo after user edits
            return line;
        }

        /**
         * Deep-clones the revertible parts of a line for the Undo snapshot.
         * @param {object} line
         * @returns {object}
         */
        function snapshotLine(line) {
            return {
                values:         $.extend(true, {}, line.values),
                display:        $.extend(true, {}, line.display),
                _productType:   line._productType   || null,
                _priceOverride: line._priceOverride || false
            };
        }

        /**
         * Reverts a saved+dirty line to its last pristine snapshot (Undo Changes).
         * New lines have no snapshot — use discardNewLine to remove them instead.
         * @param {object} line
         */
        function undoLine(line) {
            if (!line || !line._saved) return;
            if (editing && editing.rowId === line.rowId) editing = null;
            closeCatalog();
            line.values         = $.extend(true, {}, line._saved.values);
            line.display        = $.extend(true, {}, line._saved.display);
            line._productType   = line._saved._productType;
            line._priceOverride = line._saved._priceOverride;
            line._dirty         = false;
            line.touchedCols    = {};
            renderAll();
            renderTotals();
        }

        /**
         * Discards a new (never-saved) line — removes it from the array without a server call.
         * Mirrors Delete for new lines: there is no snapshot to revert to.
         * @param {object} line
         */
        function discardNewLine(line) {
            if (!line) return;
            if (editing && editing.rowId === line.rowId) editing = null;
            closeCatalog();
            var i = lines.indexOf(line);
            if (i >= 0) { lines.splice(i, 1); totalLineCount = Math.max(0, totalLineCount - 1); }
            renderAll();
            renderTotals();
        }

        // ── Column seeding ───────────────────────────────────────────────────

        /**
         * Ensures all expected columns exist on a line's values map (adds zero/empty defaults).
         * Called after fromServerRow to prevent undefined-column errors in the grid.
         * @param {object} line
         */
        function seedAllColumns(line) {
            var v  = line.values;
            var defaults = {
                VAS_OppLines_ID: 0, Line: 0,
                M_Product_ID: 0, C_Charge_ID: 0, M_AttributeSetInstance_ID: 0,
                PlannedQty: 0, C_UOM_ID: 0, PlannedPrice: 0, PlannedAmt: 0,
                Description: "",
                AD_OrgTrx_ID: 0, C_Project_ID: 0, C_Campaign_ID: 0, C_Activity_ID: 0
            };
            for (var k in defaults) {
                if (defaults.hasOwnProperty(k) && v[k] === undefined) v[k] = defaults[k];
            }
        }

        // ── Grid header ──────────────────────────────────────────────────────

        /**
         * Builds the header row HTML for the line grid.
         * Columns: #, Product/Charge, Description, Planned Qty, UOM, Planned Price, Planned Amt, (actions)
         * @returns {string} HTML string.
         */
        function buildHeadRow() {
            var $row = $('<div class="vas-ol-row vas-ol-row--head" role="row"></div>');
            $selectAll = $('<input type="checkbox" aria-label="' + esc(lbl("VAS_218_SelectAll", "Select all")) + '">');
            $row.append($('<div class="vas-ol-cell vas-ol-cell--check" role="columnheader"></div>').append($selectAll));
            $selectAll.on("change", function () {
                var checked = this.checked;
                if ($linesBody) {
                    $linesBody.find('.vas-ol-row--line input[type="checkbox"]').prop("checked", checked);
                    $linesBody.find('.vas-ol-row--line').toggleClass("is-selected", checked);
                }
                updateToolbarState();
            });
            $row.append('<div class="vas-ol-cell" role="columnheader">' + esc(lbl("VAS_218_Product", "Product / Charge")) + '</div>');
            $row.append('<div class="vas-ol-cell" role="columnheader">' + esc(lbl("VAS_218_Desc", "Description")) + '</div>');
            $row.append('<div class="vas-ol-cell vas-ol-cell--right" role="columnheader">' + esc(lbl("VAS_218_PlannedQtyUOM", "Qty / UOM")) + '</div>');
            $row.append('<div class="vas-ol-cell vas-ol-cell--right" role="columnheader">' + esc(lbl("VAS_218_PlannedPrice", "Planned Price")) + '</div>');
            $row.append('<div class="vas-ol-cell vas-ol-cell--right" role="columnheader">' + esc(lbl("VAS_218_PlannedAmt", "Planned Amt")) + '</div>');
            $row.append('<div class="vas-ol-cell vas-ol-cell--more" role="columnheader"></div>');
            return $row;
        }

        // ── Totals row ───────────────────────────────────────────────────────

        /**
         * Builds and renders the totals row beneath the grid.
         * Sums PlannedAmt across all lines on the current page plus otherAmt from other pages.
         */
        function renderTotals() {
            $totalsRow.empty();
            var amt = otherAmt;
            for (var i = 0; i < lines.length; i++) amt += +lineVal(lines[i], "PlannedAmt") || 0;
            var sym = (parent && parent.CurSymbol) ? parent.CurSymbol + " " : "";
            function fmt(n) { return sym + fmtMoney(n); }
            $totalsRow.append(totalsRow(lbl("GrandTotal", "Planned Amount") + ":", fmt(amt), true));
        }

        /**
         * Creates a single totals-row entry element.
         * @param {string} label
         * @param {string} value
         * @param {boolean} isLast - Adds a separator class if true.
         * @returns {jQuery}
         */
        function totalsRow(label, value, isLast) {
            return '<div class="vas-ol-totals-row' + (isLast ? ' vas-ol-totals-row--grand' : '') + '">' +
                   '<span class="vas-ol-totals-row__label">' + esc(label) + '</span>' +
                   '<span class="vas-ol-totals-row__value">' + esc(value) + '</span>' +
                   '</div>';
        }

        // ── Pager ────────────────────────────────────────────────────────────

        /**
         * Renders the pager control for multi-page line lists.
         */
        function renderPager() {
            if (!$pager) return;
            $pager.empty().hide();
            if (totalLineCount <= LINE_PAGE_SIZE) return;

            var totalPages = Math.ceil(totalLineCount / LINE_PAGE_SIZE);
            var from       = (linePage - 1) * LINE_PAGE_SIZE + 1;
            var to         = Math.min(linePage * LINE_PAGE_SIZE, totalLineCount);
            var showing    = lbl("VAS_218_Showing", "Showing") + " " + from + "–" + to + " " + lbl("VAS_218_Of", "of") + " " + totalLineCount;
            var pageInfo   = linePage + " / " + totalPages;

            $pager.html(
                '<span class="vas-ol-linepager__showing">' + esc(showing) + '</span>' +
                '<div class="vas-ol-linepager__nav">' +
                    '<button type="button" class="vas-ol-attr-pagebtn" data-act="lp-prev"' + (linePage <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                    '<span class="vas-ol-linepager__info">' + esc(pageInfo) + '</span>' +
                    '<button type="button" class="vas-ol-attr-pagebtn" data-act="lp-next"' + (linePage >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                '</div>'
            ).show();

            $pager.find("[data-act='lp-prev']").on("click", function () {
                if (linePage <= 1) return;
                if (hasDirtyLines() && !confirm(msg("VAS_218_UnsavedChanges"))) return;
                fetchData(parent && parent.VAS_Opportunity_ID, linePage - 1);
            });
            $pager.find("[data-act='lp-next']").on("click", function () {
                if (linePage >= totalPages) return;
                if (hasDirtyLines() && !confirm(msg("VAS_218_UnsavedChanges"))) return;
                fetchData(parent && parent.VAS_Opportunity_ID, linePage + 1);
            });
        }

        // ── Toast notifications ──────────────────────────────────────────────

        /**
         * Shows a brief toast notification above the panel.
         * @param {string} message - Message text.
         * @param {boolean} isError - Renders in error styling when true.
         */
        function showToast(message, isError) {
            if (!$self || !$self.length) return;
            var $t = $('<div class="vas-ol-toast' + (isError ? ' vas-ol-toast-error' : '') + '">' +
                       esc(message) + '</div>');
            $self.append($t);
            setTimeout(function () { $t.fadeOut(300, function () { $t.remove(); }); }, 3000);
        }

        // ── Grid busy state ──────────────────────────────────────────────────

        /**
         * Toggles the grid loading overlay.
         * @param {boolean} busy
         */
        function setGridBusy(busy) {
            if (!$grid) return;
            if (busy) {
                $grid.addClass("vas-ol-busy");
            } else {
                $grid.removeClass("vas-ol-busy");
            }
        }

        /**
         * Shows or hides the panel-level busy overlay.
         * Passed as a helper to VIS.AttributeControl.open so the attribute modal
         * can toggle the same spinner used by the rest of this panel.
         * @param {boolean} show
         */
        function showBusy(show) {
            setGridBusy(!!show);
        }

        /**
         * Renders an icon span compatible with the VIS icon font.
         * Required by VIS.AttributeControl.open for its toolbar icons.
         * @param {string} name  - Icon token / data-icon value.
         * @param {string} glyph - Optional fallback glyph character.
         * @returns {string} HTML string.
         */
        function icon(name, glyph) {
            return '<span class="vas-ol-icon" data-icon="' + esc(name) + '">' + (glyph || "") + "</span>";
        }

        /**
         * Formats a Date (or date-like string) to the ISO YYYY-MM-DD format.
         * Required by VIS.AttributeControl.open for guarantee-date columns.
         * @param {Date|string|null} d
         * @returns {string}
         */
        function dateStr(d) {
            if (!d) return "";
            var dt = (d instanceof Date) ? d : new Date(d);
            if (isNaN(dt.getTime())) return String(d).slice(0, 10);
            var m   = dt.getMonth() + 1;
            var day = dt.getDate();
            return dt.getFullYear() + "-" + (m   < 10 ? "0" + m   : m)
                                   + "-" + (day < 10 ? "0" + day : day);
        }

        // ── Dirty-state helpers ──────────────────────────────────────────────

        /**
         * Returns true when any line has unsaved changes.
         * @returns {boolean}
         */
        function hasDirtyLines() {
            for (var i = 0; i < lines.length; i++) {
                if (lines[i]._dirty || lines[i]._isNew) return true;
            }
            return false;
        }

        // ── Full render ──────────────────────────────────────────────────────

        /**
         * Renders the complete panel contents (header, all rows, totals, pager).
         */
        function renderAll() {
            if (!$grid) return;
            $grid.empty();
            $grid.append(buildHeadRow());
            $linesBody = $('<div class="vas-ol-tbody"></div>');
            $grid.append($linesBody);

            if (!lines.length) {
                $linesBody.append('<div class="vas-ol-emptyrow">' + esc(msg("VAS_218_NoLines")) + '</div>');
            } else {
                for (var i = 0; i < lines.length; i++) {
                    seedAllColumns(lines[i]);
                    $linesBody.append(buildRow(i));
                }
            }

            renderTotals();
            renderPager();
            updateToolbarState();
        }

        /**
         * Re-renders a single row in place without rebuilding the entire grid.
         * @param {number} idx - Zero-based index into the lines array.
         */
        function renderRow(idx) {
            if (!$linesBody || idx < 0 || idx >= lines.length) return;
            var $existing = $linesBody.find('.vas-ol-row--line[data-idx="' + idx + '"]');
            var $newRow   = buildRow(idx);
            if ($existing.length) {
                $existing.replaceWith($newRow);
            } else {
                $linesBody.append($newRow);
            }
            renderTotals();
            updateToolbarState();
        }

        // ── dispInput helper ─────────────────────────────────────────────────

        /**
         * Creates a readonly display input that activates editing when clicked.
         * Matches the VAS_107 pattern so the CSS for .vas-ol-cell-edit__input applies.
         * @param {object} line  - Line object owning this field.
         * @param {string} field - Logical field name ("product", "description", etc.).
         * @param {string} text  - Current display text.
         * @param {object} opts  - { placeholder, align, cls, readOnly }
         * @returns {jQuery}
         */
        function dispInput(line, field, text, opts) {
            opts = opts || {};
            var $i = $('<input type="text" readonly tabindex="-1" draggable="false" class="vas-ol-cell-edit__input vas-ol-cell-disp" />');
            $i.val(text || "");
            if (opts.placeholder) $i.attr("placeholder", opts.placeholder);
            if (opts.align === "right") $i.css("text-align", "right");
            if (opts.cls) $i.addClass(opts.cls);
            $i.attr("title", text || "");
            if (!opts.readOnly) {
                $i.on("click", function () { startEdit(line, field); });
            } else {
                $i.addClass("vas-ol-cell-disp--ro").prop("disabled", true);
            }
            return $i;
        }

        /**
         * Sets the editing state to the given line+field and re-renders that row.
         * @param {object} line  - Line object.
         * @param {string} field - Field being edited.
         */
        function startEdit(line, field) {
            // Row is locked during an in-flight save or delete — reject the edit attempt.
            if (line._saving || line._busy) return;
            editing = { rowId: line.rowId, field: field };
            var idx = lines.indexOf(line);
            if (idx >= 0) renderRow(idx);
        }

        /**
         * Commits any active inline editor by triggering blur on its input/select.
         */
        function commitEditing() {
            if (!editing) return;
            if ($linesBody) {
                $linesBody.find(".vas-ol-cell-edit.is-editing input, .vas-ol-cell-edit.is-editing select").first().trigger("blur");
            }
        }

        /**
         * Advances editing focus to the next (or previous) editable cell in TAB_ORDER.
         * The caller must inline-commit the current field's value BEFORE calling this,
         * so the data is saved even though we bypass the blur handler.
         *
         * After this function sets the new `editing` state and calls renderRow, the old
         * input is removed from the DOM which fires a stale blur. Each cell's blur handler
         * guards against this by checking whether `editing` still points at itself before
         * resetting it.
         *
         * Crossing a row boundary moves to the first/last cell of the adjacent row.
         * When there is no adjacent row the editor simply closes.
         *
         * @param {object}  line     - Line currently being edited.
         * @param {string}  fromRole - TAB_ORDER role of the active cell.
         * @param {boolean} reverse  - true = Shift+Tab (move backwards).
         */
        function moveFocus(line, fromRole, reverse) {
            var order   = TAB_ORDER.filter(function (r) { return r !== "more"; });
            var cur     = order.indexOf(fromRole);
            if (cur < 0) { editing = null; renderRow(lines.indexOf(line)); return; }
            var lineIdx = lines.indexOf(line);
            var nextPos = reverse ? cur - 1 : cur + 1;

            if (nextPos >= 0 && nextPos < order.length) {
                // Stay on same row, move to next/prev field
                editing = { rowId: line.rowId, field: ROLE_TO_FIELD[order[nextPos]] || order[nextPos] };
                renderRow(lineIdx);
            } else {
                // Cross to the adjacent row's first (forward) or last (backward) field
                var targetIdx = reverse ? lineIdx - 1 : lineIdx + 1;
                if (targetIdx >= 0 && targetIdx < lines.length) {
                    var targetLine  = lines[targetIdx];
                    var targetRole  = reverse ? order[order.length - 1] : order[0];
                    editing = { rowId: targetLine.rowId, field: ROLE_TO_FIELD[targetRole] || targetRole };
                    renderRow(lineIdx);
                    renderRow(targetIdx);
                } else {
                    editing = null;
                    renderRow(lineIdx);
                }
            }
        }

        /**
         * Fills a UOM <select> element with options from the line's cached lookup list.
         * Falls back to the panel-level uomCache when the per-row list is not yet loaded.
         * @param {jQuery} $sel - The select element to populate.
         * @param {object} line - Line whose C_UOM_ID is the selected value.
         */
        function fillUomOptions($sel, line) {
            var currId = line.values.C_UOM_ID || 0;
            var opts   = (line._lk && line._lk.uom) || [];
            $sel.empty();
            if (opts.length) {
                for (var i = 0; i < opts.length; i++) {
                    var o   = opts[i];
                    var oId = o.C_UOM_ID || o.id || 0;
                    var oNm = o.UOMName  || o.Name || o.name || "";
                    $sel.append($('<option></option>').val(oId).text(oNm).prop("selected", oId === currId));
                }
            } else {
                // Fallback: current UOM from cache so the select is not empty while loading
                if (currId && uomCache[currId]) {
                    $sel.append($('<option></option>').val(currId).text(uomCache[currId]).prop("selected", true));
                }
            }
        }

        // ── Catalog (product / charge inline search) — VAS_107 pattern ─────────

        var SEARCH_DEBOUNCE  = 250;   // ms between keystrokes before firing the server call
        var CATALOG_PAGE_SIZE = 50;   // rows per page (matches controller default)
        var CATALOG_MAX_PX   = 260;   // keep in sync with .vas-ol-catalog-popover max-height in CSS

        /**
         * Debounces catalog search — clears a previous timer before scheduling the next call.
         * @param {string} term  - Current input value.
         * @param {jQuery} inner - position:relative container for the popover.
         * @param {object} line  - Line being edited.
         * @param {jQuery} $inp  - The text input.
         */
        function scheduleCatalog(term, inner, line, $inp) {
            catalog.term = term; catalog.highlight = 0;
            if (catalog.debounce) clearTimeout(catalog.debounce);
            catalog.debounce = setTimeout(function () { resetCatalog(term, inner, line, $inp); }, SEARCH_DEBOUNCE);
        }

        /**
         * Resets catalog state, creates the popover skeleton, and fires the first page load.
         * Fires immediately even for an empty term so the user sees all results on first focus.
         * @param {string} term
         * @param {jQuery} inner
         * @param {object} line
         * @param {jQuery} $inp
         */
        function resetCatalog(term, inner, line, $inp) {
            catalog.term = term || ""; catalog.offset = 0; catalog.hasMore = true;
            catalog.results = []; catalog.seq++; catalog.highlight = 0;
            catalog.$inp = $inp;
            inner.find(".vas-ol-catalog-popover").remove();
            catalog.$pop = $('<div class="vas-ol-catalog-popover"></div>');
            // Scroll-paging: load next page when the user scrolls near the bottom
            catalog.$pop.on("scroll", function () {
                var el = this;
                if (catalog.hasMore && !catalog.loading && el.scrollTop + el.clientHeight >= el.scrollHeight - 40) {
                    loadCatalogPage(inner, line, $inp, false);
                }
            });
            // Use mousedown delegation so the input's blur fires first and we can
            // check relatedTarget before closing; preventDefault prevents focus leaving $inp
            catalog.$pop.on("mousedown", ".vas-ol-catalog-popover__item", function (e) {
                e.preventDefault();
                commitCatalogItem(line, catalog.results[+$(this).attr("data-idx")]);
            });
            catalog.$pop.on("mouseenter", ".vas-ol-catalog-popover__item", function () {
                setHighlight(+$(this).attr("data-idx"));
            });
            inner.append(catalog.$pop);
            positionCatalog();
            catalog.$pop.html('<div class="vas-ol-catalog__hint">' + esc(msg("VAS_218_Loading")) + "</div>");
            loadCatalogPage(inner, line, $inp, true);
        }

        /**
         * Positions the popover below (or above when there is more room) the input,
         * clamped to the scroll-box boundary. Called on first load and whenever rows append.
         */
        function positionCatalog() {
            if (!catalog.$pop || !catalog.$pop.length) return;
            var $inp = catalog.$inp;
            if (!$inp || !$inp.length || !$inp[0].getBoundingClientRect) return;
            var r = $inp[0].getBoundingClientRect();
            var clipTop = 0, clipBottom = window.innerHeight;
            if ($self && $self.length && $self[0].getBoundingClientRect) {
                var rr = $self[0].getBoundingClientRect();
                clipTop    = Math.max(clipTop,    rr.top);
                clipBottom = Math.min(clipBottom, rr.bottom);
            }
            var GAP        = 4;
            var spaceBelow = clipBottom - r.bottom - GAP;
            var spaceAbove = r.top - clipTop - GAP;
            var natural    = (catalog.$pop[0].scrollHeight || CATALOG_MAX_PX) + 2;
            var above;
            if      (natural <= spaceBelow) above = false;
            else if (natural <= spaceAbove) above = true;
            else                            above = spaceAbove > spaceBelow;
            var avail = above ? spaceAbove : spaceBelow;
            var maxH  = Math.min(CATALOG_MAX_PX, Math.max(avail, 0));
            catalog.$pop.css("max-height", maxH > 0 ? (maxH + "px") : "");
            catalog.$pop.toggleClass("vas-ol-catalog-popover--above", above);
        }

        /**
         * Removes the popover and resets loading state immediately.
         * Called before committing a selection so the row's busy-opacity doesn't bleed through.
         */
        function closeCatalog() {
            if (catalog.debounce) { clearTimeout(catalog.debounce); catalog.debounce = null; }
            if (catalog.$pop) { catalog.$pop.remove(); catalog.$pop = null; }
            catalog.$inp = null;
            catalog.results = []; catalog.loading = false;
        }

        /**
         * Issues a paginated GET to SearchCatalog and appends the returned rows.
         * A sequence number guards against stale responses arriving out of order.
         * @param {jQuery} inner
         * @param {object} line
         * @param {jQuery} $inp
         * @param {boolean} isReset - true = first page, clears existing rows
         */
        function loadCatalogPage(inner, line, $inp, isReset) {
            if (catalog.loading || (!catalog.hasMore && !isReset)) return;
            catalog.loading = true;
            var mySeq = catalog.seq;
            var oppId = parent ? parent.VAS_Opportunity_ID : 0;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_218_CreateOppLines/SearchCatalog",
                type: "GET", dataType: "json",
                data: {
                    VAS_Opportunity_ID: oppId,
                    query:    catalog.term,
                    pageSize: CATALOG_PAGE_SIZE,
                    offset:   catalog.offset,
                    rowContext: ""
                },
                success: function (raw) {
                    if (mySeq !== catalog.seq || !catalog.$pop) { catalog.loading = false; return; }
                    var items = (typeof raw === "string") ? JSON.parse(raw) : raw;
                    items = items || [];
                    var start = catalog.results.length;
                    catalog.results  = catalog.results.concat(items);
                    catalog.offset  += items.length;
                    catalog.hasMore  = items.length === CATALOG_PAGE_SIZE;
                    catalog.loading  = false;
                    appendCatalogRows(items, start);
                    if (start === 0) setHighlight(0);
                },
                error: function () { catalog.loading = false; }
            });
        }

        /**
         * Appends rendered catalog rows to the open popover.
         * Only re-measures position on the first page (avoids scroll-jump on paging).
         * @param {Array}  items    - New rows to append.
         * @param {number} startIdx - Index of the first item in catalog.results.
         */
        function appendCatalogRows(items, startIdx) {
            if (!catalog.$pop) return;
            if (startIdx === 0 && !items.length) {
                catalog.$pop.html('<div class="vas-ol-catalog__hint">' + esc(msg("VAS_218_NoMatches")) + "</div>");
                positionCatalog();
                return;
            }
            if (startIdx === 0) catalog.$pop.empty();
            var html = "";
            for (var i = 0; i < items.length; i++) html += catalogRowHtml(items[i], startIdx + i);
            catalog.$pop.append(html);
            if (startIdx === 0) positionCatalog();
        }

        /**
         * Builds the HTML string for a single catalog row button.
         * Server fields: Kind ("P"|"C"), DisplayName, SearchKey, RecordId.
         * @param {object} it  - Catalog item from the server.
         * @param {number} idx - Index in catalog.results.
         * @returns {string} HTML string.
         */
        function catalogRowHtml(it, idx) {
            var isCharge = it.Kind === "C";
            var badge    = isCharge ? "charge" : "product";
            var blabel   = isCharge ? msg("VAS_218_Charge") : msg("VAS_218_Product");
            var tooltip  = esc(it.DisplayName + (it.SearchKey ? " (" + it.SearchKey + ")" : ""));
            return '<button type="button" class="vas-ol-catalog-popover__item" data-catalog-item="true" data-idx="' + idx + '" title="' + tooltip + '">' +
                '<span class="vas-ol-catalog-popover__name">' + esc(it.DisplayName) + "</span>" +
                '<span class="vas-ol-badge vas-ol-badge--' + badge + '">' + esc(blabel) + "</span></button>";
        }

        /**
         * Moves the keyboard highlight to the given index (clamped to list length).
         * Scrolls the highlighted row into view without rebuilding the list.
         * @param {number} idx - Target index.
         */
        function setHighlight(idx) {
            if (!catalog.$pop) return;
            var n = catalog.results.length; if (!n) return;
            idx = Math.max(0, Math.min(idx, n - 1));
            catalog.highlight = idx;
            var $items = catalog.$pop.children(".vas-ol-catalog-popover__item");
            $items.removeClass("is-highlighted");
            var $sel = $items.eq(idx).addClass("is-highlighted");
            if ($sel.length && $sel[0].scrollIntoView) $sel[0].scrollIntoView({ block: "nearest" });
        }

        /**
         * Applies the chosen catalog item to the line, closes the popover, fires the callout.
         * Server item fields: Kind ("P"|"C"), RecordId, DisplayName, HasAttributeSet.
         * @param {object} line - Line to update.
         * @param {object} item - Selected catalog row.
         */
        function commitCatalogItem(line, item) {
            var v = line.values;
            if (!line.display) line.display = {};
            var d = line.display;

            if (item.Kind === "C") {
                v.C_Charge_ID  = item.RecordId; v.M_Product_ID = 0; v.M_AttributeSetInstance_ID = 0;
                d.chargeName   = item.DisplayName; d.productName = ""; d.hasAttributeSet = false; d.attrName = "";
            } else {
                v.M_Product_ID = item.RecordId; v.C_Charge_ID = 0;
                d.productName  = item.DisplayName; d.chargeName = "";
                d.hasAttributeSet = !!item.HasAttributeSet; v.M_AttributeSetInstance_ID = 0; d.attrName = "";
            }
            line._productType  = (item.Kind === "C") ? "" : (item.ProductType || "");
            line._priceOverride = false;
            markDirty(line);
            editing = null;
            // Remove the dropdown NOW — before runCallout marks the row busy (opacity),
            // otherwise a still-open popover child would turn translucent until render().
            closeCatalog();
            runCallout(line, item.Kind === "C" ? "C_Charge_ID" : "M_Product_ID", function () {
                // Warm the per-row UOM list for the newly selected product / charge.
                ensureRowLookups(line);
                // Auto-open the attribute control when the product carries an attribute set,
                // matching VAS_107 behaviour (user picks product → attr dialog opens immediately).
                if (d.hasAttributeSet) {
                    openAttrDialog(line);
                } else {
                    editing = { rowId: line.rowId, field: "description" };
                    renderAll();
                }
                renderTotals();
            });
        }

        // ── Cell renderers ───────────────────────────────────────────────────

        /**
         * Builds the Product / Charge cell (with inline catalog search in edit mode).
         * @param {object} line - Line object.
         * @param {number} idx  - Row index.
         * @returns {jQuery}
         */
        function renderPrimaryCell(line, idx) {
            var disp  = line.display || {};
            var pName = disp.productName || disp.chargeName || "";
            var isEditingThis = editing && editing.rowId === line.rowId &&
                                (editing.field === "product" || editing.field === "charge");
            var cell = $('<div class="vas-ol-cell" role="cell"></div>');
            var wrap = $('<div class="vas-ol-cell-edit"></div>');
            if (isEditingThis) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditingThis) {
                var inner = $('<div style="position:relative"></div>');
                var $inp  = $('<input type="text" class="vas-ol-cell-edit__input" />');
                $inp.val(pName);
                $inp.attr("placeholder", msg("VAS_218_SearchProduct"));
                $inp.on("input", function () { scheduleCatalog($(this).val(), inner, line, $inp); });
                $inp.on("blur", function (e) {
                    // Focus moved to a catalog item — let its mousedown pick it
                    if (e.relatedTarget && $(e.relatedTarget).attr("data-catalog-item") === "true") return;
                    closeCatalog();
                    // Guard: Tab navigation already moved editing forward — don't reset it
                    if (!editing || editing.rowId !== line.rowId ||
                        (editing.field !== "product" && editing.field !== "charge")) return;
                    editing = null;
                    renderRow(idx);
                });
                $inp.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "ArrowDown") { e.preventDefault(); setHighlight(catalog.highlight + 1); return; }
                    if (e.key === "ArrowUp")   { e.preventDefault(); setHighlight(catalog.highlight - 1); return; }
                    if (e.key === "Enter") {
                        if (catalog.results.length > 0) {
                            e.preventDefault();
                            commitCatalogItem(line, catalog.results[Math.min(catalog.highlight, catalog.results.length - 1)]);
                        } else {
                            $inp.trigger("blur");
                        }
                        return;
                    }
                    if (e.key === "Tab") {
                        e.preventDefault();
                        closeCatalog();
                        moveFocus(line, "primary", e.shiftKey);
                        return;
                    }
                    if (e.key === "Escape") { closeCatalog(); editing = null; renderRow(idx); }
                });
                inner.append($inp);
                wrap.append(inner);
                // Fire immediately (empty term = all results), like VAS_107
                resetCatalog(pName, inner, line, $inp);
                setTimeout(function () { $inp.focus(); $inp.select(); }, 0);
            } else {
                wrap.append(dispInput(line, "product", pName,
                    { placeholder: msg("VAS_218_AddProductCharge") }));
                // Attribute link sub-line for products that carry an attribute set
                if (line.values.M_Product_ID > 0 && (disp.attrName || disp.hasAttributeSet)) {
                    var attrTxt = disp.attrName || msg("VAS_218_SetAttribute");
                    var $attr   = $('<span class="vas-ol-attr-link"></span>').text(attrTxt).attr("title", attrTxt);
                    if (!disp.attrName) $attr.addClass("vas-ol-attr-link--empty");
                    $attr.on("click", (function (ln) { return function (e) { e.stopPropagation(); openAttrDialog(ln); }; })(line));
                    wrap.append($attr);
                }
            }
            return cell;
        }

        /**
         * Builds the Description cell.
         * @param {object} line
         * @param {number} idx
         * @returns {jQuery}
         */
        function renderDescCell(line, idx) {
            var isEditingThis = editing && editing.rowId === line.rowId && editing.field === "description";
            var cell = $('<div class="vas-ol-cell" role="cell"></div>');
            var wrap = $('<div class="vas-ol-cell-edit"></div>');
            if (isEditingThis) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditingThis) {
                var $inp = $('<input type="text" class="vas-ol-cell-edit__input" />').val(line.values.Description || "");
                $inp.on("blur", function () {
                    fieldSet(line, "Description", $inp.val());
                    // Guard: Tab navigation already moved editing forward — don't reset it
                    if (!editing || editing.rowId !== line.rowId || editing.field !== "description") return;
                    editing = null;
                    renderRow(idx);
                });
                $inp.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Enter") { $inp.trigger("blur"); }
                    if (e.key === "Tab") {
                        e.preventDefault();
                        fieldSet(line, "Description", $inp.val());
                        moveFocus(line, "description", e.shiftKey);
                        return;
                    }
                    if (e.key === "Escape") { editing = null; renderRow(idx); }
                });
                wrap.append($inp);
                setTimeout(function () { $inp.focus(); $inp.select(); }, 0);
            } else {
                wrap.append(dispInput(line, "description", line.values.Description || "",
                    { placeholder: lbl("VAS_218_AddDescription", "Add description…") }));
            }
            return cell;
        }

        /**
         * Builds the Qty / UOM cell — two stacked editable fields.
         * Quantity: numeric input; UOM: <select> populated from the cached UOM list.
         * @param {object} line
         * @param {number} idx
         * @returns {jQuery}
         */
        function renderQtyUomCell(line, idx) {
            var v         = line.values;
            var isEditQty = editing && editing.rowId === line.rowId && editing.field === "quantity";
            var uomRO     = !line._isNew;   // UOM is read-only on saved lines
            var isEditUom = editing && editing.rowId === line.rowId && editing.field === "uom" && !uomRO;
            var cell = $('<div class="vas-ol-cell vas-ol-cell--right" role="cell"></div>');
            var wrap = $('<div class="vas-ol-cell-edit"></div>');
            if (isEditQty || isEditUom) wrap.addClass("is-editing");
            cell.append(wrap);

            // ── Quantity ──
            if (isEditQty) {
                var $q = $('<input type="text" class="vas-ol-cell-edit__input vas-ol-qtyval" inputmode="decimal" />');
                $q.val(v.PlannedQty ? fmtQty(v.PlannedQty) : "").css("text-align", "right");
                $q.on("blur", function () {
                    var n = parseNum($q.val());
                    fieldSet(line, "PlannedQty", n);
                    // Recalculate amount
                    fieldSet(line, "PlannedAmt", parseFloat((n * parseNum(line.values.PlannedPrice)).toFixed(10)));
                    // Guard: Tab navigation already moved editing forward — don't reset it
                    if (!editing || editing.rowId !== line.rowId || editing.field !== "quantity") return;
                    editing = null;
                    renderRow(idx);
                    renderTotals();
                });
                $q.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Enter") { $q.trigger("blur"); }
                    if (e.key === "Tab") {
                        e.preventDefault();
                        var n = parseNum($q.val());
                        fieldSet(line, "PlannedQty", n);
                        fieldSet(line, "PlannedAmt", parseFloat((n * parseNum(line.values.PlannedPrice)).toFixed(10)));
                        moveFocus(line, "quantity", e.shiftKey);
                        renderTotals();
                        return;
                    }
                    if (e.key === "Escape") { editing = null; renderRow(idx); }
                });
                wrap.append($q);
                setTimeout(function () { $q.focus(); $q.select(); }, 0);
            } else {
                var hasQ = v.PlannedQty !== undefined && +v.PlannedQty !== 0;
                wrap.append(dispInput(line, "quantity",
                    hasQ ? fmtQty(v.PlannedQty) : "",
                    { align: "right", placeholder: lbl("VAS_218_Qty", "Qty"), cls: "vas-ol-qtyval" }));
            }

            // ── UOM ──
            if (isEditUom) {
                var $sel = $('<select class="vas-ol-cell-edit__select"></select>');
                fillUomOptions($sel, line);
                // Refine options once the per-row list arrives from the server
                ensureRowLookups(line, function () {
                    if (editing && editing.rowId === line.rowId && editing.field === "uom" &&
                        $sel.closest("body").length) {
                        fillUomOptions($sel, line);
                    }
                });
                $sel.on("change", function () {
                    var selId   = parseInt($sel.val(), 10) || 0;
                    var selName = $sel.find("option:selected").text();
                    fieldSet(line, "C_UOM_ID", selId);
                    if (!line.display) line.display = {};
                    line.display.uomName = selName;
                    if (selId) uomCache[selId] = selName;
                });
                $sel.on("blur", function () {
                    // Guard: Tab navigation already moved editing forward — don't reset it
                    if (!editing || editing.rowId !== line.rowId || editing.field !== "uom") return;
                    editing = null;
                    renderRow(idx);
                });
                $sel.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Enter") { $sel.trigger("blur"); }
                    if (e.key === "Tab") {
                        e.preventDefault();
                        // UOM value already committed by the "change" event — just move focus
                        moveFocus(line, "uom", e.shiftKey);
                        return;
                    }
                    if (e.key === "Escape") { editing = null; renderRow(idx); }
                });
                wrap.append($sel);
                setTimeout(function () { $sel.focus(); }, 0);
            } else {
                wrap.append(dispInput(line, "uom",
                    (line.display && line.display.uomName) || "",
                    { align: "right", placeholder: lbl("VAS_218_UOM", "UOM"), cls: "vas-ol-uomsub vas-ol-cell-disp--sub", readOnly: uomRO }));
            }
            return cell;
        }

        /**
         * Builds the Planned Price cell.
         * @param {object} line
         * @param {number} idx
         * @returns {jQuery}
         */
        function renderPriceCell(line, idx) {
            var isEditingThis = editing && editing.rowId === line.rowId && editing.field === "price";
            var cell = $('<div class="vas-ol-cell vas-ol-cell--right" role="cell"></div>');
            var wrap = $('<div class="vas-ol-cell-edit"></div>');
            if (isEditingThis) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditingThis) {
                var $inp = $('<input type="text" class="vas-ol-cell-edit__input" inputmode="decimal" />');
                $inp.val(fmtMoney(line.values.PlannedPrice || 0)).css("text-align", "right");
                $inp.on("blur", function () {
                    var n = parseNum($inp.val());
                    fieldSet(line, "PlannedPrice", n);
                    fieldSet(line, "PlannedAmt", parseFloat((parseNum(line.values.PlannedQty) * n).toFixed(10)));
                    // Guard: Tab navigation already moved editing forward — don't reset it
                    if (!editing || editing.rowId !== line.rowId || editing.field !== "price") return;
                    editing = null;
                    renderRow(idx);
                    renderTotals();
                });
                $inp.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Enter") { $inp.trigger("blur"); }
                    if (e.key === "Tab") {
                        e.preventDefault();
                        var n = parseNum($inp.val());
                        fieldSet(line, "PlannedPrice", n);
                        fieldSet(line, "PlannedAmt", parseFloat((parseNum(line.values.PlannedQty) * n).toFixed(10)));
                        moveFocus(line, "price", e.shiftKey);
                        renderTotals();
                        return;
                    }
                    if (e.key === "Escape") { editing = null; renderRow(idx); }
                });
                wrap.append($inp);
                setTimeout(function () { $inp.focus(); $inp.select(); }, 0);
            } else {
                var hasP = line.values.PlannedPrice !== undefined && +line.values.PlannedPrice !== 0;
                wrap.append(dispInput(line, "price",
                    hasP ? fmtMoney(line.values.PlannedPrice) : "",
                    { align: "right", placeholder: "0.00" }));
            }
            return cell;
        }

        // ── Row builder ──────────────────────────────────────────────────────

        /**
         * Builds a complete grid row element for the line at the given index.
         * Follows VAS_107 pattern: uses dispInput readonly inputs for display and
         * state-based rendering for editing (via the `editing` variable).
         * @param {number} idx - Zero-based line index.
         * @returns {jQuery}
         */
        function buildRow(idx) {
            var line    = lines[idx];
            var v       = line.values;
            var isDirty = line._dirty || line._isNew;

            var $row = $('<div role="row" data-idx="' + idx + '" data-rowid="' + esc(line.rowId) + '"></div>');
            $row.addClass("vas-ol-row vas-ol-row--line");
            if (isDirty) $row.addClass("is-unsaved");

            // Col 1 – Checkbox
            var $cb = $('<input type="checkbox" aria-label="' + esc(lbl("VAS_218_SelectLine", "Select line")) + '">');
            $row.append($('<div class="vas-ol-cell vas-ol-cell--check" role="cell"></div>').append($cb));
            $cb.on("change", function () {
                $row.toggleClass("is-selected", this.checked);
                if ($selectAll && $linesBody) {
                    var total   = $linesBody.find('.vas-ol-row--line input[type="checkbox"]').length;
                    var checked = $linesBody.find('.vas-ol-row--line input[type="checkbox"]:checked').length;
                    $selectAll.prop("indeterminate", checked > 0 && checked < total);
                    $selectAll.prop("checked", total > 0 && checked === total);
                }
                updateToolbarState();
            });

            // Col 2 – Product / Charge
            $row.append(renderPrimaryCell(line, idx));

            // Col 3 – Description
            $row.append(renderDescCell(line, idx));

            // Col 4 – Qty / UOM
            $row.append(renderQtyUomCell(line, idx));

            // Col 5 – Planned Price
            $row.append(renderPriceCell(line, idx));

            // Col 6 – Planned Amount (read-only)
            var $amtCell = $('<div class="vas-ol-cell vas-ol-cell--right" role="cell"></div>');
            $amtCell.append('<span class="vas-ol-amt">' + esc(fmtMoney(v.PlannedAmt)) + '</span>');
            $row.append($amtCell);

            // Col 7 – Undo + More actions
            var $moreCell = $('<div class="vas-ol-cell vas-ol-cell--more" role="cell" style="position:relative"></div>');

            // Re-apply per-row spinner after a re-render so an in-flight save or callout
            // keeps its indicator even if renderRow() rebuilds the row DOM.
            if (line._saving || line._busy) {
                var spinLabel = line._saving ? msg("VAS_218_Saving") : msg("VAS_218_Calculating");
                $row.addClass("is-busy").append(rowSpinHtml(spinLabel));
            }

            // Undo button: reverts saved+dirty row to last snapshot, or discards a new row.
            var canUndoEdits  = !line._isNew && line._dirty && line._saved;
            var canDiscardNew = !!line._isNew;
            var editable      = !!(parent && parent.IsEditable);
            // Block undo while a save is in flight for this row.
            if (editable && !line._busy && !line._saving && (canUndoEdits || canDiscardNew)) {
                var undoTitle = (canDiscardNew
                    ? msg("VAS_218_UndoNewLine")
                    : msg("VAS_218_UndoChanges")) + " (Ctrl+Alt+Z)";
                var $undo = $('<button type="button" class="vas-ol-undo-btn" title="' + esc(undoTitle) + '">↺</button>');
                var undoAct = canDiscardNew ? discardNewLine : undoLine;
                // mousedown + preventDefault: keeps the focused cell from blurring+committing
                // before Undo fires (same pattern as the Save button).
                $undo.on("mousedown", function (e) { e.preventDefault(); e.stopPropagation(); undoAct(line); });
                // Keyboard activation (Enter/Space) emits click with detail 0 — no mousedown.
                $undo.on("click", function (e) { if (e.detail === 0) { e.stopPropagation(); undoAct(line); } });
                $moreCell.append($undo);
            }

            $row.append($moreCell);

            return $row;
        }

        // ── Ensure row lookups ───────────────────────────────────────────────

        /**
         * Ensures the UOM list lookup for the given line is loaded before opening a dropdown.
         * Only fetches UomList (no TaxList in VAS_218).
         * @param {object} line
         * @param {function} cb - Callback when done.
         */
        function ensureRowLookups(line, cb) {
            cb = cb || function () {};
            if (line._lk && line._lk.uom) {
                cb();
                return;
            }

            var productId = line.values.M_Product_ID || 0;
            $.post(
                VIS.Application.contextUrl + "VAS_218_CreateOppLines/GetUomList",
                { M_Product_ID: productId, AD_Window_ID: adWindowId },
                function (raw) {
                    try {
                        var res = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        if (!line._lk) line._lk = {};
                        line._lk.uom = res || [];
                    } catch (e) {
                        if (!line._lk) line._lk = {};
                        line._lk.uom = [];
                    }
                    cb();
                }
            ).fail(function () {
                if (!line._lk) line._lk = {};
                line._lk.uom = [];
                cb();
            });
        }

        // ── More popover ─────────────────────────────────────────────────────

        /**
         * Opens the More popover for the given line, showing Dimension fields.
         * VAS_218 has no Discount or Notes fields.
         * @param {number} idx
         */
        function openMorePopover(idx) {
            closeDialogs();
            morePopoverFor = idx;

            var line = lines[idx];
            if (!line) { morePopoverFor = null; return; }

            primeLineContext(line);

            var $row = $grid.find('.vas-ol-row[data-idx="' + idx + '"]');
            var $btn = $row.find(".vas-ol-btn-more");

            var html = '<div id="vasOlMore" class="vas-ol-more-popover">' +
                       '<div class="vas-ol-more-header">' +
                           '<span>' + esc(lbl("VAS_218_More", "More")) + '</span>' +
                           '<button class="vas-ol-more-close" id="vasOlMoreClose">&#x2715;</button>' +
                       '</div>' +
                       '<div class="vas-ol-more-body">';

            // Render each group
            for (var g = 0; g < MORE_FIELD_GROUPS.length; g++) {
                var grp = MORE_FIELD_GROUPS[g];
                html += '<div class="vas-ol-more-group">' +
                        '<div class="vas-ol-more-group-title">' + esc(lbl(grp.key, grp.def)) + '</div>';

                // Render fields from ADDITIONAL_INFO_FIELDS that belong to this group
                var anchor   = grp.anchor;
                var inGroup  = false;
                for (var f = 0; f < ADDITIONAL_INFO_FIELDS.length; f++) {
                    var fld = ADDITIONAL_INFO_FIELDS[f];
                    if (fld.col === anchor) inGroup = true;
                    if (inGroup) {
                        var fldVal  = lineVal(line, fld.col) || 0;
                        var fldMeta = colMeta(fld.col);
                        var fldLbl  = (fldMeta && fldMeta.Name) ? fldMeta.Name : fld.col;
                        html += '<div class="vas-ol-more-field">' +
                                '<label class="vas-ol-more-label">' + esc(fldLbl) + '</label>' +
                                '<div class="vas-ol-more-ctrl" data-col="' + esc(fld.col) + '" data-val="' + esc(fldVal) + '">' +
                                    '<span class="vas-ol-more-fk-text">' + esc(fldVal || "") + '</span>' +
                                '</div></div>';
                    }
                    // Stop at next group anchor
                    if (inGroup && f > 0 && fld.col !== anchor && g < MORE_FIELD_GROUPS.length - 1 &&
                        fld.col === MORE_FIELD_GROUPS[g + 1].anchor) {
                        break;
                    }
                }

                html += '</div>'; // close group
            }

            html += '</div></div>'; // close body and popover

            var $pop = $(html);
            $("body").append($pop);

            // Position near the More button
            var btnOff  = $btn.offset();
            var btnH    = $btn.outerHeight() || 0;
            $pop.css({
                top:  (btnOff.top + btnH) + "px",
                left: Math.max(0, btnOff.left - $pop.outerWidth() + $btn.outerWidth()) + "px"
            });

            // Render FK lookups for each field inside the popover
            renderMorePopooverFKFields($pop, line, idx);

            // Close button
            $pop.find("#vasOlMoreClose").on("click", function () {
                commitMorePopover();
                closeDialogs();
            });

            // Close on outside click
            $(document).on("mousedown.vasol-more", function (e) {
                if (!$(e.target).closest("#vasOlMore").length) {
                    commitMorePopover();
                    closeDialogs();
                }
            });
        }

        /**
         * Renders FK lookup controls inside the More popover for all ADDITIONAL_INFO_FIELDS.
         * @param {jQuery} $pop    - The popover element.
         * @param {object} line    - Current line.
         * @param {number} idx     - Line index.
         */
        function renderMorePopooverFKFields($pop, line, idx) {
            $pop.find(".vas-ol-more-ctrl").each(function () {
                var $ctrl = $(this);
                var col   = $ctrl.data("col");
                var fldMeta = colMeta(col);
                if (!fldMeta) return;

                var currId  = lineVal(line, col) || 0;
                var currTxt = ""; // Will be loaded by the FK widget

                if (VIS && VIS.FKLookup) {
                    $ctrl.empty();
                    var lookup = new VIS.FKLookup($ctrl[0], {
                        adWindowId:  adWindowId,
                        adTabId:     adTabId,
                        columnName:  col,
                        value:       currId,
                        displayText: currTxt,
                        onSelect: function (result) {
                            fieldSet(line, col, result ? result.id : 0);
                        }
                    });
                    lookup.render();
                }
            });
        }

        /**
         * Commits any pending changes from the More popover back to the line.
         * VAS_218 has no Discount or Notes fields — only FK field values are committed
         * via their own onSelect callbacks, so this is a no-op.
         */
        function commitMorePopover() {
            if (!morePopoverFor) return;
            // No Discount or Notes fields in VAS_218.
            // FK fields in the popover commit immediately via their onSelect callbacks.
        }

        // ── Attribute Set dialog ─────────────────────────────────────────────

        /**
         * Opens the attribute set instance dialog for the given line.
         * @param {number} idx
         */
        /**
         * Opens the attribute-set picker for the given line's product.
         * Accepts a line object directly (matching the VAS_107 pattern).
         * Auto-opens when called from commitCatalogItem if the product has an attribute set.
         * @param {object} line - Line whose product's attribute set is being configured.
         */
        function openAttrDialog(line) {
            if (!line) return;
            var idx   = lines.indexOf(line);
            var v     = line.values;
            var d     = line.display || (line.display = {});
            var prodId = v.M_Product_ID || 0;
            if (!prodId) return;
            // Guard: only open when we know the product carries an attribute set.
            // d.hasAttributeSet is set by commitCatalogItem from the server's HasAttributeSet flag.
            // For lines opened via the attr-link click we re-check via productHasAttributeSet.
            if (!d.hasAttributeSet) return;

            closeDialogs();
            attrState = { lineIdx: idx };

            if (VIS && VIS.AttributeControl && typeof VIS.AttributeControl.open === "function") {
                // Full VAS_107-style control (preferred when available)
                VIS.AttributeControl.open({
                    M_Product_ID:              prodId,
                    M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0,
                    productName:               d.productName || "",
                    // Opportunities are always sales-side: open the existing-instance list
                    // first (matching the mockup) so users select from stock.
                    // newAttribute: false hides the "jump straight to new form" behaviour.
                    IsSOTrx:                   true,
                    newAttribute:              false,
                    showAll:                   false,
                    lbl:       msg,
                    esc:       esc,
                    icon:      icon,
                    showBusy:  showBusy,
                    showToast: showToast,
                    dateStr:   dateStr,
                    fmtMoney:  fmtMoney,
                    parseNum:  parseNum,
                    onApply: function (res) {
                        var asi = (res && res.M_AttributeSetInstance_ID) || 0;
                        fieldSet(line, "M_AttributeSetInstance_ID", asi);
                        d.attrName        = (res && res.description) || "";
                        d.hasAttributeSet = true;
                        markDirty(line);
                        attrState = null;
                        editing = { rowId: line.rowId, field: "description" };
                        if (asi > 0) runCallout(line, "M_AttributeSetInstance_ID", function () { renderAll(); });
                        else renderAll();
                    },
                    onClose: function () {
                        attrState = null;
                        editing = { rowId: line.rowId, field: "description" };
                        renderAll();
                    }
                });
            } else if (VIS && VIS.AttrDialog && typeof VIS.AttrDialog.show === "function") {
                // Fallback: older AttrDialog API
                VIS.AttrDialog.show({
                    M_Product_ID:              prodId,
                    M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0,
                    IsSOTrx:                   false,
                    newAttribute:              true,
                    onOk: function (result) {
                        var asi = (result && result.M_AttributeSetInstance_ID) || 0;
                        fieldSet(line, "M_AttributeSetInstance_ID", asi);
                        d.attrName        = (result && result.Description) || "";
                        d.hasAttributeSet = true;
                        markDirty(line);
                        attrState = null;
                        editing = { rowId: line.rowId, field: "description" };
                        if (asi > 0) runCallout(line, "M_AttributeSetInstance_ID", function () { renderAll(); });
                        else renderAll();
                    },
                    onCancel: function () {
                        attrState = null;
                        editing = { rowId: line.rowId, field: "description" };
                        renderAll();
                    }
                });
            } else {
                attrState = null;
            }
        }

        /**
         * Checks whether a product has an attribute set defined, using a cached server call.
         * @param {number} prodId
         * @param {function} cb - Callback with boolean result.
         */
        function productHasAttributeSet(prodId, cb) {
            var cacheKey = "VAS218DISP_HasAttrSet_" + prodId;
            if (sessionStorage) {
                var cached = sessionStorage.getItem(cacheKey);
                if (cached !== null) { cb(cached === "1"); return; }
            }

            $.post(
                VIS.Application.contextUrl + "VAS_218_CreateOppLines/HasAttributeSet",
                { M_Product_ID: prodId },
                function (raw) {
                    try {
                        var res  = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        var has  = !!(res && res.HasAttributeSet);
                        if (sessionStorage) sessionStorage.setItem(cacheKey, has ? "1" : "0");
                        cb(has);
                    } catch (e) {
                        cb(false);
                    }
                }
            ).fail(function () { cb(false); });
        }

        // ── Barcode scan dialog ──────────────────────────────────────────────

        /**
         * Opens the barcode scan dialog.
         * @param {number} idx - Line index to receive the scanned product.
         */
        function openScanDialog(idx) {
            var line = lines[idx];
            if (!line) return;

            closeDialogs();
            scanState = { lineIdx: idx };

            if (VIS && VIS.BarcodeScanner) {
                VIS.BarcodeScanner.show({
                    containerId: "vasOlScan",
                    onOk: function (result) {
                        if (result && result.M_Product_ID) {
                            setProduct(idx, {
                                columnName: "M_Product_ID",
                                id:   result.M_Product_ID,
                                name: result.ProductName || ""
                            });
                        }
                        scanState = null;
                    },
                    onCancel: function () {
                        scanState = null;
                    }
                });
            } else {
                scanState = null;
            }
        }

        // ── Blocking dialog detection ────────────────────────────────────────

        /**
         * Returns true when a modal dialog is open that should prevent other interactions.
         * @returns {boolean}
         */
        function hasBlockingDialog() {
            return !!(attrState || scanState || morePopoverFor ||
                      document.getElementById("vasOlAttr") ||
                      document.getElementById("vasOlScan"));
        }

        // ── Close all dialogs ────────────────────────────────────────────────

        /**
         * Closes all open dialogs and popovers.
         */
        function closeDialogs() {
            $("#vasOlAttr, #vasOlScan, #vasOlMore").remove();
            $(document).off("mousedown.vasol-more");
            if (morePopoverFor !== null) {
                commitMorePopover();
                morePopoverFor = null;
            }
        }

        // ── Context priming ──────────────────────────────────────────────────

        /**
         * Writes the opportunity header values and line column values into VIS.context
         * so that AD_Val_Rule predicates resolve correctly when FK lookups are opened.
         * @param {object} line - The line being edited.
         */
        function primeLineContext(line) {
            if (!VIS || !VIS.context) return;
            var ctx = VIS.context;

            // Header context
            if (parent) {
                ctx.setContext(String(adWindowId), "VAS_Opportunity_ID", String(parent.VAS_Opportunity_ID || 0));
                ctx.setContext(String(adWindowId), "M_PriceList_Version_ID", String(parent.M_PriceList_Version_ID || 0));
                ctx.setContext(String(adWindowId), "C_EnquiryRdate",        String(parent.C_EnquiryRdate || ""));
                ctx.setContext(String(adWindowId), "C_BPartner_ID",         String(parent.C_BPartner_ID  || 0));
                ctx.setContext(String(adWindowId), "AD_Client_ID",          String(parent.AD_Client_ID   || 0));
                ctx.setContext(String(adWindowId), "AD_Org_ID",             String(parent.AD_Org_ID      || 0));
            }

            // Line values
            if (line && line.values) {
                var v = line.values;
                ctx.setContext(String(adWindowId), "M_Product_ID",              String(v.M_Product_ID              || 0));
                ctx.setContext(String(adWindowId), "C_Charge_ID",               String(v.C_Charge_ID               || 0));
                ctx.setContext(String(adWindowId), "M_AttributeSetInstance_ID", String(v.M_AttributeSetInstance_ID || 0));
                ctx.setContext(String(adWindowId), "PlannedQty",                String(v.PlannedQty                || 0));
                ctx.setContext(String(adWindowId), "C_UOM_ID",                  String(v.C_UOM_ID                  || 0));
                ctx.setContext(String(adWindowId), "PlannedPrice",              String(v.PlannedPrice              || 0));
                ctx.setContext(String(adWindowId), "PlannedAmt",                String(v.PlannedAmt                || 0));
                ctx.setContext(String(adWindowId), "Description",               String(v.Description               || ""));
            }

            // Login tokens
            if (parent && parent.loginCtx) {
                var lc = parent.loginCtx;
                for (var k in lc) {
                    if (lc.hasOwnProperty(k)) {
                        ctx.setContext("#global", k, String(lc[k]));
                    }
                }
            }
        }

        // ── Logic context resolver ───────────────────────────────────────────

        /**
         * Resolves a @TOKEN@ value for DisplayLogic / ReadOnlyLogic evaluation.
         * @param {string} token - Token name (without @ delimiters).
         * @returns {string}
         */
        function logicCtxVal(token) {
            if (!parent) return "";
            switch (token) {
                case "VAS_Opportunity_ID":      return String(parent.VAS_Opportunity_ID      || 0);
                case "M_PriceList_Version_ID":  return String(parent.M_PriceList_Version_ID  || 0);
                case "C_BPartner_ID":           return String(parent.C_BPartner_ID           || 0);
                case "AD_Client_ID":            return String(parent.AD_Client_ID            || 0);
                case "AD_Org_ID":               return String(parent.AD_Org_ID               || 0);
                default:
                    if (parent.loginCtx && parent.loginCtx[token] !== undefined) {
                        return String(parent.loginCtx[token]);
                    }
                    return "";
            }
        }

        // ── Row payload builder ──────────────────────────────────────────────

        /**
         * Builds the save payload for a single line.
         * @param {object} l - Line object.
         * @returns {object}
         */
        function buildRowPayload(l) {
            var v = l.values || {};
            return {
                VAS_OppLines_ID:            v.VAS_OppLines_ID            || 0,
                RowKey:                     l.rowId,
                Line:                       v.Line                        || 0,
                M_Product_ID:               v.M_Product_ID               || 0,
                C_Charge_ID:                v.C_Charge_ID                || 0,
                M_AttributeSetInstance_ID:  v.M_AttributeSetInstance_ID  || 0,
                PlannedQty:                 v.PlannedQty                 || 0,
                C_UOM_ID:                   v.C_UOM_ID                   || 0,
                PlannedPrice:               v.PlannedPrice               || 0,
                PlannedAmt:                 v.PlannedAmt                 || 0,
                Description:                v.Description                || "",
                Values:                     v,
                // TouchedCols must be an array of column names (List<string> on server)
                TouchedCols:                l.touchedCols ? Object.keys(l.touchedCols) : []
            };
        }

        // ── Validate line ────────────────────────────────────────────────────

        /**
         * Validates a line before saving; returns null if valid or an error message string.
         * @param {object} line
         * @returns {string|null}
         */
        function validateLine(line) {
            var v = line.values || {};
            if (!v.M_Product_ID && !v.C_Charge_ID) {
                return msg("VAS_218_ProductRequired");
            }
            if (!(parseNum(v.PlannedQty) > 0)) {
                return msg("VAS_218_QtyRequired");
            }
            return null;
        }

        // ── Add line ─────────────────────────────────────────────────────────

        /**
         * Appends a new blank line to the grid.
         */
        function addLine() {
            var maxLine = 0;
            for (var i = 0; i < lines.length; i++) {
                var ln = lineVal(lines[i], "Line") || 0;
                if (ln > maxLine) maxLine = ln;
            }

            var newLine = {
                rowId:       "new_" + Date.now(),
                values: {
                    VAS_OppLines_ID:           0,
                    Line:                      maxLine + 10,
                    M_Product_ID:              0,
                    C_Charge_ID:               0,
                    M_AttributeSetInstance_ID: 0,
                    PlannedQty:                0,
                    C_UOM_ID:                  0,
                    PlannedPrice:              0,
                    PlannedAmt:                0,
                    Description:               ""
                },
                display: {
                    productName:      "",
                    chargeName:       "",
                    uomName:          "",
                    attrName:         "",
                    hasAttributeSet:  false
                },
                touchedCols: {},
                _dirty:  true,
                _isNew:  true,
                _lk:     {}
            };

            lines.push(newLine);
            totalLineCount++;
            renderAll();

            // Set editing state to auto-open product editor on the new row, then re-render
            var newLine  = lines[lines.length - 1];
            editing = { rowId: newLine.rowId, field: "product" };
            renderAll();
        }

        // ── Delete selected lines ────────────────────────────────────────────

        /**
         * Deletes all selected lines. New (unsaved) lines are removed immediately;
         * saved lines are deleted via a server call.
         */
        function deleteSelected() {
            var toDelete    = [];   // line objects with a saved DB ID
            var toDeleteNew = [];   // indexes of new (never-saved) lines to remove immediately

            for (var i = 0; i < lines.length; i++) {
                var $r = $grid.find('.vas-ol-row--line[data-idx="' + i + '"]');
                if (!$r.hasClass("is-selected")) continue;

                if (lines[i].values.VAS_OppLines_ID) {
                    toDelete.push(lines[i]);
                } else {
                    toDeleteNew.push(i);
                }
            }

            // New lines have no DB record — remove them immediately without a server call
            toDeleteNew.reverse();
            for (var j = 0; j < toDeleteNew.length; j++) {
                lines.splice(toDeleteNew[j], 1);
                totalLineCount = Math.max(0, totalLineCount - 1);
            }

            if (!toDelete.length) {
                renderAll();
                return;
            }

            var toDeleteIds = [];
            for (var k = 0; k < toDelete.length; k++) {
                toDeleteIds.push(toDelete[k].values.VAS_OppLines_ID);
            }

            // Show the full-panel busy overlay while the server delete is in flight
            showBusy(true);

            $.post(
                VIS.Application.contextUrl + "VAS_218_CreateOppLines/DeleteLines",
                {
                    payload: JSON.stringify({
                        VAS_Opportunity_ID: parent ? parent.VAS_Opportunity_ID : 0,
                        AD_Window_ID:       adWindowId,
                        LineIds:            toDeleteIds,
                        Page:               linePage - 1
                    })
                },
                function (raw) {
                    showBusy(false);
                    try {
                        var res = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        if (res && res.ErrorKey) {
                            showToast(res.ErrorDetail || msg(res.ErrorKey) || msg("VAS_218_DeleteError"), true);
                            renderAll();
                        } else if (res && res.Success) {
                            // Replace local lines with the server-confirmed page so counts
                            // and dirty state are authoritative after the delete.
                            totalLineCount = res.LinesTotal || 0;
                            linePage       = (+res.LinePage + 1) || 1;
                            otherAmt       = parseNum(res.OtherPagesPlannedAmt);
                            lines = [];
                            var rows = res.Lines || [];
                            for (var m = 0; m < rows.length; m++) lines.push(fromServerRow(rows[m]));
                            renderAll();
                        } else {
                            showToast(msg("VAS_218_DeleteError"), true);
                            renderAll();
                        }
                    } catch (e) {
                        showToast(msg("VAS_218_DeleteError"), true);
                        renderAll();
                    }
                }
            ).fail(function () {
                showBusy(false);
                showToast(msg("VAS_218_DeleteError"), true);
                renderAll();
            });
        }

        // ── Toolbar state ────────────────────────────────────────────────────

        /**
         * Updates toolbar button states and labels after any data or selection change.
         * Mirrors VAS_107's renderHeaderButtons pattern:
         *  - Save uses a CSS disabled class (not HTML disabled) so mousedown still fires
         *    when a field is being edited — avoiding the "two clicks needed" UX issue.
         *  - Save label shows the dirty-line count: "Save (2)".
         *  - Delete label shows the selected-row count: "Delete (1)".
         *  - Refresh is always enabled.
         */
        function updateToolbarState() {
            if (!$deleteBtn || !$saveBtn) return;

            var locked     = !(parent && parent.IsEditable);
            var dirtyCount = 0;
            for (var i = 0; i < lines.length; i++) {
                if (lines[i]._dirty || lines[i]._isNew) dirtyCount++;
            }
            var selCount = $grid ? $grid.find(".vas-ol-row--line.is-selected").length : 0;

            // Save button: visual-disabled via CSS class only so mousedown still fires
            // while a cell editor is active (HTML disabled swallows the event).
            var savePlural  = dirtyCount > 1 ? lbl("VAS_218_PluralS", "s") : "";
            var saveCount   = dirtyCount > 0 ? " (" + dirtyCount + ")" : "";
            $saveBtn.html(icon("hard-drive", "💾") + "<span>" +
                esc(lbl("VAS_218_Save", "Save")) + savePlural + saveCount + "</span>");
            $saveBtn.prop("disabled", locked)
                    .toggleClass("vas-ol-is-disabled", locked || dirtyCount === 0);

            // Add button
            $addBtn.prop("disabled", locked)
                   .toggleClass("vas-ol-is-disabled", locked);

            // Delete button: selection count badge
            $deleteBtn.find(".vas-ol-sel-count").text(selCount > 0 ? "(" + selCount + ")" : "");
            $deleteBtn.prop("disabled", selCount === 0 || locked)
                      .toggleClass("vas-ol-is-disabled", selCount === 0 || locked);

            // Select-all checkbox
            if ($selectAll) {
                var totalRows = lines.length;
                $selectAll.prop("disabled", locked || totalRows === 0);
                if (totalRows > 0) {
                    $selectAll.prop("indeterminate", selCount > 0 && selCount < totalRows);
                    $selectAll.prop("checked", selCount === totalRows);
                }
            }
        }

        // ── Save rows ────────────────────────────────────────────────────────

        /**
         * Saves all dirty lines to the server.
         */
        function saveRows() {
            if (!parent || !parent.VAS_Opportunity_ID) {
                showToast(msg("VAS_218_NoOpportunity"), true);
                return;
            }

            commitEditing(); // commit any active editor

            // Collect dirty lines
            var dirtyLines = [];
            for (var i = 0; i < lines.length; i++) {
                if (lines[i]._dirty || lines[i]._isNew) {
                    var errMsg = validateLine(lines[i]);
                    if (errMsg) {
                        showToast(errMsg, true);
                        return;
                    }
                    dirtyLines.push(lines[i]);
                }
            }

            if (!dirtyLines.length) return;

            var batch = [];
            for (var j = 0; j < dirtyLines.length; j++) {
                batch.push(buildRowPayload(dirtyLines[j]));
            }

            // Lock each dirty row with a "Saving…" spinner before the round-trip starts,
            // matching the VAS_107 pattern so the user sees per-row feedback immediately.
            var savingLabel = msg("VAS_218_Saving");
            for (var k = 0; k < dirtyLines.length; k++) {
                dirtyLines[k]._saving = true;
                setRowBusy(dirtyLines[k], true, savingLabel);
            }

            // Controller expects a single "payload" JSON string (OppSaveLinesRequest)
            $.post(
                VIS.Application.contextUrl + "VAS_218_CreateOppLines/SaveLines",
                {
                    payload: JSON.stringify({
                        VAS_Opportunity_ID: parent.VAS_Opportunity_ID,
                        AD_Window_ID:       adWindowId,
                        Lines:              batch,
                        Page:               linePage - 1
                    })
                },
                function (raw) {
                    // Clear the per-row saving flags before any render.
                    for (var k = 0; k < dirtyLines.length; k++) {
                        dirtyLines[k]._saving = false;
                        dirtyLines[k]._busy   = false;
                    }
                    try {
                        var res = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        if (res && res.ErrorKey) {
                            renderAll();
                            showToast(res.ErrorDetail || msg(res.ErrorKey) || msg("VAS_218_SaveError"), true);
                        } else if (res && res.Success) {
                            // Replace local lines with the server-confirmed page — this
                            // clears all dirty / _isNew flags because fromServerRow() creates
                            // clean line objects. Also updates paging and other-page totals.
                            totalLineCount = res.LinesTotal || 0;
                            linePage       = (+res.LinePage + 1) || 1;
                            otherAmt       = parseNum(res.OtherPagesPlannedAmt);
                            lines = [];
                            var rows = res.Lines || [];
                            for (var i = 0; i < rows.length; i++) lines.push(fromServerRow(rows[i]));
                            renderAll();
                            showToast(msg("VAS_218_SaveSuccess"), false);
                        } else {
                            renderAll();
                            showToast(msg("VAS_218_SaveError"), true);
                        }
                    } catch (e) {
                        renderAll();
                        showToast(msg("VAS_218_SaveError"), true);
                    }
                }
            ).fail(function () {
                for (var k = 0; k < dirtyLines.length; k++) {
                    dirtyLines[k]._saving = false;
                    dirtyLines[k]._busy   = false;
                }
                showToast(msg("VAS_218_SaveError"), true);
                renderAll();
            });
        }


        // ── Merge saved lines ────────────────────────────────────────────────

        /**
         * Merges server-returned saved-line data back into the local lines array.
         * Matches by RowKey (rowId) first, then falls back to VAS_OppLines_ID.
         * @param {object} res - Server response containing SavedLines array.
         */
        function mergeSavedLines(res) {
            if (!res || !res.SavedLines) return;
            var saved = res.SavedLines;

            for (var s = 0; s < saved.length; s++) {
                var sr   = saved[s];
                var line = findBatchLine(sr.RowKey, sr.VAS_OppLines_ID);
                if (!line) continue;

                // Overwrite with server-confirmed values
                var v = line.values;
                v.VAS_OppLines_ID = parseInt(sr.VAS_OppLines_ID, 10) || 0;
                v.Line             = parseInt(sr.Line, 10) || 0;
                v.PlannedQty       = parseNum(sr.PlannedQty);
                v.PlannedPrice     = parseNum(sr.PlannedPrice);
                v.PlannedAmt       = parseNum(sr.PlannedAmt);

                // Copy any other returned fields
                for (var k in sr) {
                    if (sr.hasOwnProperty(k) &&
                        k !== "RowKey" && k !== "VAS_OppLines_ID") {
                        v[k] = sr[k];
                    }
                }

                // Clear dirty flags; take a fresh snapshot so Undo can revert
                // any subsequent edits back to this server-confirmed state.
                line._dirty      = false;
                line._isNew      = false;
                line.rowId       = "r" + v.VAS_OppLines_ID;
                line.touchedCols = {};
                line._saved      = snapshotLine(line);
            }
        }

        /**
         * Finds a line in the local lines array by RowKey or VAS_OppLines_ID.
         * @param {string} rowKey
         * @param {number} lineId
         * @returns {object|null}
         */
        /**
         * Returns the line object whose rowId matches, or null.
         * @param {string} rowId
         * @returns {object|null}
         */
        function lineById(rowId) {
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].rowId === rowId) return lines[i];
            }
            return null;
        }

        function findBatchLine(rowKey, lineId) {
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].rowId === rowKey) return lines[i];
            }
            if (lineId) {
                for (var j = 0; j < lines.length; j++) {
                    if (lines[j].values.VAS_OppLines_ID === lineId) return lines[j];
                }
            }
            return null;
        }

        // ── Apply paging data ────────────────────────────────────────────────

        /**
         * Applies server-returned paging summary values (totals from other pages, count).
         * @param {object} res - Server response.
         */
        function applyLinePaging(res) {
            if (!res) return;
            // OppSaveResult uses LinesTotal / LinePage to match GetPanelData's OppPanelData shape.
            if (res.LinesTotal  !== undefined) totalLineCount = res.LinesTotal;
            if (res.LinePage    !== undefined) linePage       = res.LinePage + 1;

            // Other-pages planned amount (no tax fields in VAS_218)
            otherAmt = parseNum(res.OtherPagesPlannedAmt);
        }

        // ── Reload keeping unsaved changes ───────────────────────────────────

        /**
         * Reloads lines from the server, merging them into the existing array so that
         * locally-added new lines (VAS_OppLines_ID === 0) and unsaved edits are preserved.
         * @param {number} oppId - VAS_Opportunity_ID
         * @param {number} page  - Page number
         */
        function reloadLinesKeepingUnsaved(oppId, page) {
            if (!oppId) return;
            var reqPage = (typeof page === "number" && page >= 1) ? page - 1 : 0;
            $.ajax({
                url:      VIS.Application.contextUrl + "VAS_218_CreateOppLines/GetPanelData",
                type:     "GET",
                dataType: "json",
                data:     { VAS_Opportunity_ID: oppId, AD_Window_ID: adWindowId, page: reqPage },
                success: function (raw) {
                    try {
                        var res    = (typeof raw === "string") ? JSON.parse(raw) : raw;
                        totalLineCount = (res && res.LinesTotal) || 0;
                        linePage       = res ? (+res.LinePage + 1) : 1;
                        otherAmt       = parseNum(res && res.OtherPagesPlannedAmt);

                        var serverRows = res.Lines || [];
                        var newLines   = [];

                        // Map existing saved lines by VAS_OppLines_ID
                        var existingById = {};
                        for (var i = 0; i < lines.length; i++) {
                            var lid = lines[i].values.VAS_OppLines_ID;
                            if (lid) existingById[lid] = lines[i];
                        }

                        // Rebuild from server, preserving dirty local edits
                        for (var s = 0; s < serverRows.length; s++) {
                            var sr  = serverRows[s];
                            var sid = parseInt(sr.VAS_OppLines_ID || sr.vas_opplines_id, 10) || 0;
                            if (sid && existingById[sid] && existingById[sid]._dirty) {
                                // Keep the locally-edited version
                                newLines.push(existingById[sid]);
                            } else {
                                newLines.push(fromServerRow(sr));
                            }
                        }

                        // Append new (unsaved) lines that do not yet have an ID
                        for (var j = 0; j < lines.length; j++) {
                            if (!lines[j].values.VAS_OppLines_ID) {
                                newLines.push(lines[j]);
                            }
                        }

                        lines = newLines;
                        renderAll();
                    } catch (e) {
                        if (VIS && VIS.log) VIS.log.severe("VAS_218 reloadLinesKeepingUnsaved error: " + e);
                    }
                },
                error: function () {
                    if (VIS && VIS.log) VIS.log.warning("VAS_218 reloadLinesKeepingUnsaved failed");
                }
            });
        }

        // ── Additional Info modal ────────────────────────────────────────────

        /**
         * Opens the Additional Info modal for the given line, showing dimension fields.
         * @param {number} idx
         */
        function openAdditionalInfo(idx) {
            var line = lines[idx];
            if (!line) return;

            primeLineContext(line);
            closeDialogs();

            var html = '<div class="vas-ol-modal-overlay" id="vasOlAI">' +
                       '<div class="vas-ol-modal">' +
                       '<div class="vas-ol-modal-header">' +
                           '<span>' + esc(lbl("VAS_218_AdditionalInfo", "Additional Info")) + '</span>' +
                           '<button class="vas-ol-modal-close" id="vasOlAIClose">&#x2715;</button>' +
                       '</div>' +
                       '<div class="vas-ol-modal-body" id="vasOlAIBody">';

            for (var f = 0; f < ADDITIONAL_INFO_FIELDS.length; f++) {
                var fld     = ADDITIONAL_INFO_FIELDS[f];
                var fldMeta = colMeta(fld.col);
                var fldLbl  = (fldMeta && fldMeta.Name) ? fldMeta.Name : fld.col;
                var fldVal  = lineVal(line, fld.col) || 0;

                html += '<div class="vas-ol-ai-field">' +
                        '<label class="vas-ol-ai-label">' + esc(fldLbl) + '</label>' +
                        '<div class="vas-ol-ai-ctrl" data-col="' + esc(fld.col) + '" data-val="' + esc(fldVal) + '">' +
                        '</div></div>';
            }

            html += '</div>' + // modal-body
                    '<div class="vas-ol-modal-footer">' +
                        '<button class="vas-ol-btn-ok" id="vasOlAIOk">'     + esc(lbl("VAS_218_Ok",     "OK"))     + '</button>' +
                        '<button class="vas-ol-btn-cancel" id="vasOlAICancel">' + esc(lbl("VAS_218_Cancel", "Cancel")) + '</button>' +
                    '</div>' +
                    '</div></div>'; // modal, overlay

            var $modal = $(html);
            $("body").append($modal);

            // Render FK lookups
            $modal.find(".vas-ol-ai-ctrl").each(function () {
                var $ctrl  = $(this);
                var col    = $ctrl.data("col");
                var currId = lineVal(line, col) || 0;

                if (VIS && VIS.FKLookup) {
                    var lookup = new VIS.FKLookup($ctrl[0], {
                        adWindowId: adWindowId,
                        adTabId:    adTabId,
                        columnName: col,
                        value:      currId,
                        onSelect:   function (result) {
                            // Captured by closure per field
                            fieldSet(line, col, result ? result.id : 0);
                        }
                    });
                    lookup.render();
                }
            });

            $modal.find("#vasOlAIOk").on("click", function () {
                // Values are committed by onSelect handlers
                renderRow(idx);
                $modal.remove();
            });

            $modal.find("#vasOlAIClose, #vasOlAICancel").on("click", function () {
                $modal.remove();
            });
        }

        // ── Keyboard navigation ──────────────────────────────────────────────

        /**
         * Binds keyboard event handlers for the grid.
         * Escape cancels the current editor.
         */
        function bindKeyboard() {
            $(document).on("keydown.vasol", function (e) {
                if (e.keyCode === 27) { // Escape — cancel active editor
                    if (editing) {
                        editing = null;
                        renderAll();
                    }
                }
            });

            // Alt+Ctrl+N/S/D/Z/Q shortcuts — registered on the capture phase so they
            // fire even when a cell editor has focus and has called stopPropagation.
            self._shortcuts = VAS.PanelShortcuts.register({
                /** Panel is active when it is visible and an opportunity is loaded. */
                isActive: function () {
                    return !!(parent && parent.VAS_Opportunity_ID && $self && $self.is(":visible"));
                },
                /** Suppress shortcuts while the attribute picker or scan dialog is open. */
                hasBlockingDialog: function () {
                    return !!(attrState || scanState ||
                              document.getElementById("vasOlAttr") ||
                              document.getElementById("vasOlScan"));
                },
                /** Alt+Ctrl+N — add a new line (same as the Add button). */
                onNew: function () { addLine(); },
                /** Alt+Ctrl+S — save all unsaved lines (same as the Save button). */
                onSave: function () { saveRows(); },
                /**
                 * Alt+Ctrl+D — delete selected lines.
                 * Shows a toast when nothing is selected.
                 */
                onDelete: function () {
                    if (!$grid || !$grid.find(".is-selected").length) {
                        showToast(msg("VAS_218_SelectRowToDelete"));
                        return;
                    }
                    if (!confirm(msg("VAS_218_ConfirmDelete"))) return;
                    deleteSelected();
                },
                /**
                 * Alt+Ctrl+Z — discard the focused or first new/dirty line.
                 * Since saved rows have no undo snapshot, only new (unsaved) lines
                 * can be reverted; shows a toast when there is nothing to revert.
                 */
                onUndo: function () {
                    if (!parent || !parent.IsEditable) return;
                    // Priority 1: the row currently being edited
                    var target = (editing && lineById(editing.rowId)) || null;
                    if (target && !target._isNew && !target._dirty) target = null;
                    // Priority 2: first selected new/dirty row
                    if (!target) {
                        var sel = $grid ? $grid.find(".vas-ol-row--line.is-selected") : $();
                        sel.each(function () {
                            var rid = $(this).data("rowid");
                            var l   = lineById(rid);
                            if (l && !l._saving && (l._isNew || l._dirty)) { target = l; return false; }
                        });
                    }
                    // Priority 3: first new/dirty line on the page
                    if (!target) {
                        for (var i = 0; i < lines.length; i++) {
                            if (!lines[i]._saving && (lines[i]._isNew || lines[i]._dirty)) { target = lines[i]; break; }
                        }
                    }
                    if (!target) { showToast(msg("VAS_218_NothingToUndo")); return; }
                    if (target._isNew) { discardNewLine(target); } else { undoLine(target); }
                },
                /** Alt+Ctrl+Q — refresh the current page (same as the Refresh button). */
                onRefresh: function () {
                    if (parent && parent.VAS_Opportunity_ID) fetchData(parent.VAS_Opportunity_ID, linePage);
                }
            });
        }

        /**
         * Closes the catalog popover when the user clicks outside the editing row.
         * Editors commit themselves on blur; this only cleans up the popover.
         */
        function bindOutsideClick() {
            $(document).on("mousedown.vasol", function (e) {
                if (!$(e.target).closest(".vas-ol-catalog-popover, .vas-ol-cell-edit.is-editing").length) {
                    $(".vas-ol-catalog-popover").remove();
                }
            });
        }

        // ── Public render method (initialises the panel UI) ──────────────────

        /**
         * Entry point called by the tab-panel framework to initialise the panel.
         * Builds the toolbar, grid container, totals row, and pager, then fetches data.
         * @param {object} parentRecord - The opportunity header record.
         * @param {jQuery} $container   - The DOM element to render into.
         * @param {object} opts         - Additional options from the framework.
         */
        function render(parentRecord, $container, opts) {
            opts     = opts || {};
            parent   = parentRecord || null;
            $self    = $container;
            self     = opts.instance || self;
            adWindowId = opts.adWindowId || adWindowId;
            adTabId    = opts.adTabId    || adTabId;

            if (!parent || !parent.VAS_Opportunity_ID) {
                $container.empty();
                return;
            }

            // Build panel skeleton
            var $panel   = $('<div class="vas-ol-panel"></div>');
            var $header  = $('<header class="vas-ol-panel__header"></header>');
            var $title   = $('<div><h2 class="vas-ol-panel__title">' + esc(lbl("VAS_218_OppLinesSummary", "Opportunity Lines")) + '</h2></div>');
            var $actions = $('<div class="vas-ol-panel__actions"></div>');
            $addBtn    = $('<button type="button" class="vas-ol-btn vas-ol-btn--outline" title="' + esc(lbl("VAS_218_Add", "Add")) + ' (Ctrl+Alt+N)">' + icon("plus", "+") + '<span>' + esc(lbl("VAS_218_Add", "Add")) + '</span></button>');
            $saveBtn   = $('<button type="button" class="vas-ol-btn vas-ol-btn--save vas-ol-is-disabled" title="' + esc(lbl("VAS_218_Save", "Save")) + ' (Ctrl+Alt+S)"></button>');
            $deleteBtn = $('<button type="button" class="vas-ol-btn vas-ol-btn--danger vas-ol-is-disabled" title="' + esc(lbl("VAS_218_Delete", "Delete")) + ' (Ctrl+Alt+D)" disabled>' + icon("trash", "🗑") + '<span>' + esc(lbl("VAS_218_Delete", "Delete")) + ' <span class="vas-ol-sel-count"></span></span></button>');
            $refreshBtn = $('<button type="button" class="vas-ol-btn vas-ol-btn--outline" title="' + esc(lbl("VAS_218_Refresh", "Refresh")) + ' (Ctrl+Alt+Q)">' + icon("refresh-cw", "↺") + '<span>' + esc(lbl("VAS_218_Refresh", "Refresh")) + '</span></button>');
            $actions.append($addBtn, $saveBtn, $deleteBtn, $refreshBtn);
            $header.append($title, $actions);

            var $table = $('<div class="vas-ol-table"></div>');
            $table.append(buildHeadRow());
            $linesBody = $('<div class="vas-ol-tbody"></div>');
            $table.append($linesBody);
            $grid      = $table;
            $totalsRow = $('<div class="vas-ol-totals-block"></div>');
            $pager     = $('<div class="vas-ol-linepager" style="display:none;"></div>');
            $panel.append($header, $table, $totalsRow, $pager);
            $container.empty().append($panel);
            createBusyIndicator();

            // Toolbar events
            $addBtn.on("click", function () { addLine(); });

            // mousedown fires before blur so the active editor commits its value first
            $saveBtn.on("mousedown", function (e) {
                e.preventDefault();
                if ($saveBtn.hasClass("vas-ol-is-disabled")) return;
                saveRows();
            });
            // Keyboard activation (Enter / Space) — detail 0 means keyboard, not mouse
            $saveBtn.on("click", function (e) {
                if (e.detail === 0) {
                    if ($saveBtn.hasClass("vas-ol-is-disabled")) return;
                    saveRows();
                }
            });

            $deleteBtn.on("click", function () {
                if (!$grid.find(".is-selected").length) return;
                if (!confirm(msg("VAS_218_ConfirmDelete"))) return;
                deleteSelected();
            });

            $refreshBtn.on("click", function () {
                if (parent && parent.VAS_Opportunity_ID) fetchData(parent.VAS_Opportunity_ID, linePage);
            });

            // Keyboard and outside-click bindings
            bindKeyboard();
            bindOutsideClick();

            // Initial data load
            fetchData(parent.VAS_Opportunity_ID, 1);
        }

        // ── onRefresh (called by framework when the parent record changes) ───

        /**
         * Called by the tab-panel framework when the parent opportunity record changes
         * or the panel is refreshed. Reloads data for the new record.
         * @param {object} newParent - Updated opportunity header record.
         */
        function onRefresh(newParent) {
            parent = newParent || null;
            lines  = [];
            linePage       = 1;
            totalLineCount = 0;
            otherAmt       = 0;
            activeCell     = null;
            editing        = null;
            closeCatalog();
            closeDialogs();

            if (parent && parent.VAS_Opportunity_ID) {
                self.fetchData(parent.VAS_Opportunity_ID, linePage);
            } else {
                if ($grid)      $grid.empty();
                if ($totalsRow) $totalsRow.empty();
                if ($pager)     $pager.empty();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Return the prototype object
        // ════════════════════════════════════════════════════════════════════
        return {

            // ── Public: initialise / render ──────────────────────────────────

            /**
             * Renders the panel for the given parent opportunity record.
             * @param {object} parentRecord - Opportunity header data.
             * @param {jQuery} $container   - Target container element.
             * @param {object} opts         - Options from the framework.
             */
            render: function (parentRecord, $container, opts) {
                self = this;
                render(parentRecord, $container, opts);
            },

            // ── Public: fetch data (also called internally) ──────────────────

            /**
             * Fetches opportunity line data from the server for the given ID and page.
             * @param {number} oppId - VAS_Opportunity_ID.
             * @param {number} page  - 1-based page number.
             */
            fetchData: function (oppId, page) {
                fetchData(oppId, page);
            },

            // ── Public: onRefresh ────────────────────────────────────────────

            /**
             * Called by the framework when the parent record changes.
             * @param {object} newParent
             */
            onRefresh: function (newParent) {
                self = this;
                onRefresh(newParent);
            },

            // ── Public: onParentSave ─────────────────────────────────────────

            /**
             * Called by the framework after the parent (opportunity header) record is saved.
             * Refreshes lines to reflect any header-level changes.
             * @param {object} savedParent
             */
            onParentSave: function (savedParent) {
                if (savedParent) parent = savedParent;
                if (parent && parent.VAS_Opportunity_ID) {
                    reloadLinesKeepingUnsaved(parent.VAS_Opportunity_ID, linePage);
                }
            },

            // ── Public: getIsDirty ───────────────────────────────────────────

            /**
             * Returns true when there are unsaved changes in the panel.
             * Used by the framework to prompt before navigating away.
             * @returns {boolean}
             */
            getIsDirty: function () {
                return hasDirtyLines();
            },

            // ── Public: save ─────────────────────────────────────────────────

            /**
             * Programmatic save trigger (called by parent form Save button if required).
             */
            save: function () {
                saveRows();
            },

            // ── Public: dispose ──────────────────────────────────────────────

            /**
             * Cleans up event bindings, dialogs, and DOM elements created by this panel.
             * Called by the framework when the tab is closed or the component is destroyed.
             */
            dispose: function () {
                // Remove capture-phase shortcut listener registered during init
                if (self._shortcuts) { self._shortcuts.dispose(); self._shortcuts = null; }

                // Remove keyboard and outside-click handlers
                $(document).off("mousedown.vasol").off("keydown.vasol");

                // Remove any open dialogs and the busy overlay
                if ($busy) { $busy.remove(); $busy = null; }
                $("#vasOlAttr, #vasOlScan, .vas-ol-toast").remove();
                closeDialogs();

                // Clear state
                lines          = [];
                parent         = null;
                activeCell     = null;
                editing        = null;
                closeCatalog();
                attrState      = null;
                scanState      = null;
                morePopoverFor = null;
                columnMeta     = null;
                colMetaByName  = {};
                uomCache       = {};

                // Clear DOM references
                if ($self) { $self.empty(); $self = null; }
                $grid      = null;
                $linesBody = null;
                $selectAll = null;
                $totalsRow = null;
                $pager     = null;
                $addBtn     = null;
                $saveBtn    = null;
                $deleteBtn  = null;
                $refreshBtn = null;
                $busy       = null;
            },

            // ── Framework-required prototype methods ─────────────────────────

            /**
             * Builds the panel DOM shell without loading data.
             * Called by startPanel; safe to call multiple times (rebuilds shell).
             */
            init: function () {
                self  = this;
                $self = $('<div class="vas-ol-root"></div>');

                var $panel   = $('<div class="vas-ol-panel"></div>');
                var $header  = $('<header class="vas-ol-panel__header"></header>');
                var $title   = $('<div><h2 class="vas-ol-panel__title">' + esc(lbl("VAS_218_OppLinesSummary", "Opportunity Lines")) + '</h2></div>');
                var $actions = $('<div class="vas-ol-panel__actions"></div>');
                $addBtn    = $('<button type="button" class="vas-ol-btn vas-ol-btn--outline" title="' + esc(lbl("VAS_218_Add", "Add")) + ' (Ctrl+Alt+N)">' + icon("plus", "+") + '<span>' + esc(lbl("VAS_218_Add", "Add")) + '</span></button>');
                $saveBtn   = $('<button type="button" class="vas-ol-btn vas-ol-btn--save vas-ol-is-disabled" title="' + esc(lbl("VAS_218_Save", "Save")) + ' (Ctrl+Alt+S)"></button>');
                $deleteBtn = $('<button type="button" class="vas-ol-btn vas-ol-btn--danger vas-ol-is-disabled" title="' + esc(lbl("VAS_218_Delete", "Delete")) + ' (Ctrl+Alt+D)" disabled>' + icon("trash", "🗑") + '<span>' + esc(lbl("VAS_218_Delete", "Delete")) + ' <span class="vas-ol-sel-count"></span></span></button>');
                $refreshBtn = $('<button type="button" class="vas-ol-btn vas-ol-btn--outline" title="' + esc(lbl("VAS_218_Refresh", "Refresh")) + ' (Ctrl+Alt+Q)">' + icon("refresh-cw", "↺") + '<span>' + esc(lbl("VAS_218_Refresh", "Refresh")) + '</span></button>');
                $actions.append($addBtn, $saveBtn, $deleteBtn, $refreshBtn);
                $header.append($title, $actions);

                var $table = $('<div class="vas-ol-table"></div>');
                $table.append(buildHeadRow());
                $linesBody = $('<div class="vas-ol-tbody"></div>');
                $table.append($linesBody);
                $grid      = $table;
                $totalsRow = $('<div class="vas-ol-totals-block"></div>');
                $pager     = $('<div class="vas-ol-linepager" style="display:none;"></div>');

                $panel.append($header, $table, $totalsRow, $pager);
                $self.append($panel);
                createBusyIndicator();

                $addBtn.on("click", function () { addLine(); });
                $saveBtn.on("mousedown", function (e) {
                    e.preventDefault();
                    if ($saveBtn.hasClass("vas-ol-is-disabled")) return;
                    saveRows();
                });
                $saveBtn.on("click", function (e) {
                    if (e.detail === 0) {
                        if ($saveBtn.hasClass("vas-ol-is-disabled")) return;
                        saveRows();
                    }
                });
                $deleteBtn.on("click", function () {
                    if (!$grid.find(".is-selected").length) return;
                    if (!confirm(msg("VAS_218_ConfirmDelete"))) return;
                    deleteSelected();
                });
                $refreshBtn.on("click", function () {
                    if (parent && parent.VAS_Opportunity_ID) fetchData(parent.VAS_Opportunity_ID, linePage);
                });
                bindKeyboard();
                bindOutsideClick();
            },

            /** Returns the root jQuery element; the framework appends it to the tab container. */
            getRoot: function () { return $self; },

            /**
             * Called by the VIS framework when the tab panel is first attached to a window.
             * Stores the window context then builds the panel DOM shell.
             */
            startPanel: function (windowNo, curTab) {
                self = this;
                if (curTab && typeof curTab.getAD_Window_ID === "function")
                    adWindowId = curTab.getAD_Window_ID();
                this.init();
            },

            /**
             * Called by the VIS framework each time the selected parent record changes.
             * Triggers a server round-trip to load lines for the new record.
             */
            refreshPanelData: function (recordID, selectedRow) {
                if (selectedRow === undefined || recordID <= 0) { this.clear(selectedRow !== undefined && recordID <= 0); return; }
                self = this;
                fetchData(recordID, 1);
            },

            /** Called by the framework when the panel container is resized. */
            sizeChanged: function () { /* panel uses CSS fluid width */ },

            /** Clears all state and empties the grid without destroying the DOM shell. */
            clear: function (isNewRecord) {
                lines = []; parent = null; activeCell = null; editing = null;
                closeCatalog();
                attrState = null; scanState = null; morePopoverFor = null;
                columnMeta = []; colMetaByName = {};
                if ($grid)      $grid.empty();
                if ($totalsRow) $totalsRow.empty();
                if ($pager)     $pager.empty().hide();
                $linesBody = null;
                $selectAll = null;
            }

        }; // end return (prototype)

    }()); // end IIFE


}(VAS, jQuery));
// End of VAS_218_CreateOppLines.js
