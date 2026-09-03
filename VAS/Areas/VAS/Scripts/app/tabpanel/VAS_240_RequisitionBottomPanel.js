/************************************************************
 * Module Name    : CRM Extension VAS_240
 * Purpose        : Requisition Bottom Panel — client logic. The requisition
 *                  counterpart of VAS_107_CreateOrderBottomPanel: the same
 *                  inline line grid, catalog picker, attribute picker, scan
 *                  dialog and dictionary-driven Additional Info modal, over
 *                  M_Requisition / M_RequisitionLine.
 *
 *                  A requisition line has no tax (no C_Tax_ID, no TaxAmt, no
 *                  LineTotalAmt), so there is no Tax column, no Taxable Amount
 *                  column and no tax breakdown in the totals: one amount per
 *                  line (Qty x Unit Price) and one document total.
 * Employee Code  : VAI163
 * Date           : 03-Sep-2026
 ************************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_240_RequisitionBottomPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;

        var $self = this;
        var $root, $busy, $body, $emptyState, $linesBody, $totalsRow, $pager;
        var $addBtn, $saveBtn, $deleteBtn, $refreshBtn, $selectAll;

        /* parent requisition context returned by GetPanelData */
        var parent = null;
        /* server-side line paging (20/page): current 0-based page, total saved lines, size */
        var linePage = 0, linesTotal = 0, linePageSize = 20;
        /* saved total of every line NOT on the current page (document total minus this
           page). renderTotals adds the live current-page sum so the totals row reflects the
           WHOLE requisition, not just the loaded page, while still updating during edits. */
        var otherSub = 0;
        /* dropdown catalogs */
        var uomList = [];
        /* AD_Column metadata cache (callout code + validation), keyed by column */
        var columnMeta = {};
        /* lower-cased ColumnName -> canonical (dictionary-cased) ColumnName. Used to
           canonicalise a DB-cased line.values key (PostgreSQL lowercases, Oracle uppercases)
           back to the dictionary case when priming the window context - so we never emit two
           case-variant keys for the same column (the server merges window context into a
           case-INSENSITIVE dictionary, and two variants throw "same key already added"). */
        var columnNameByLc = {};
        /* Column-name prefixes that belong to core AD/ERP modules and are always present.
           Mirrors the C# _systemPrefixes set in VAS_240_RequisitionBottomPanelModel. */
        var SYSTEM_PREFIXES = {
            "AD_": 1, "C_": 1, "M_": 1, "A_": 1, "G_": 1, "K_": 1, "R_": 1, "I_": 1,
            "B_": 1, "T_": 1, "S_": 1, "W_": 1, "U_": 1,
            "VAS_": 1, "VIS_": 1, "VA_": 1, "VB_": 1,
            // Core columns whose prefix names a ROLE, not a module: Ref_OrderLine_ID
            // ("Original PO Line") yields "Ref_", which was read as an uninstalled
            // module and silently dropped the column. Link_ is the same shape.
            "REF_": 1, "LINK_": 1
        };
        /* Non-system column prefixes (e.g. "VA106_") whose columns are present in columnMeta,
           meaning the corresponding module is installed. Populated from the server column list. */
        var installedModulePrefixes = {};
        /* Monotonic counter used to discard stale GetPanelData responses when the user
           navigates to a new record before the previous AJAX call returns. Each fetchData
           increments fetchSeq and stores its own token in mySeq; the success callback
           returns immediately when mySeq !== fetchSeq (a newer load has already started).
           clear() also bumps it so a response arriving after the panel is cleared is
           silently discarded. */
        var fetchSeq = 0;
        /* Save-serialization guard: prevents a second concurrent SaveLines POST from
           racing against a still-in-flight one on the same DB transaction. When saveInFlight
           is true, the row IDs queued by the second call land in pendingSaveIds; once the
           in-flight POST completes, flushPendingSave() replays those rows automatically. */
        var saveInFlight = false, pendingSaveIds = {};
        /* Callout-completion gate: both async callout paths (setTimeout + server AJAX) increment
           calloutPending before going async and call calloutSettled() when done. afterCallouts()
           defers Save until the count reaches zero so a qty/price typed and Saved without
           tabbing out is persisted with the callout's recomputed price / amount. */
        var calloutPending = 0, calloutWaiters = [];
        var CALLOUT_WAIT_MS = 8000;
        /* client-side line rows + reactive UI state */
        var lines = [];
        var rowCounter = 0;
        var editing = null;            // { rowId, field }
        var morePopoverFor = null;     // rowId
        /* catalog popover working state (for the row currently editing a primary) */
        var catalog = { results: [], highlight: 0, seq: 0, offset: 0, hasMore: true, loading: false, term: "", debounce: null };
        /* attribute picker + scan working state */
        var attrState = null, scanState = null;

        var CATALOG_PAGE_SIZE = 50;
        var SEARCH_DEBOUNCE = 260;
        var TAB_ORDER = ["primary", "description", "quantity", "uom", "price", "more"];

        /* ---------- locale-aware amount helpers (per ERP number format) ---------- */
        var dotDecimal = (VIS.Env && typeof VIS.Env.isDecimalPoint === "function") ? VIS.Env.isDecimalPoint() : true;
        var decSep = dotDecimal ? "." : ",";
        var grpSep = dotDecimal ? "," : ".";

        function parseNum(s) {
            s = String(s == null ? "" : s).split(grpSep).join("");
            if (decSep !== ".") s = s.split(decSep).join(".");
            var n = parseFloat(s.replace(/[^0-9.\-]/g, ""));
            return isNaN(n) ? 0 : n;
        }

        function fmtAmtInput(v, prec) {
            var p = prec >= 0 ? prec : 0;
            var f = Math.pow(10, p);
            var s = (Math.round((+v || 0) * f) / f).toFixed(p);
            return (decSep !== ".") ? s.replace(".", decSep) : s;
        }

        /* Maximum numeric magnitude accepted for any amount field — Int32.MaxValue.
           Values beyond this are clamped to prevent server-side overflow on save. */
        var AMOUNT_MAX_VALUE = 2147483647;

        /* Strip non-numeric characters from a raw amount string, honour the locale decimal
           separator, cap to `prec` decimal places, clamp the magnitude to AMOUNT_MAX_VALUE,
           and optionally allow a leading minus sign for negative amounts (e.g. discount). */
        function sanitizeAmount(raw, prec, allowNeg) {
            raw = String(raw == null ? "" : raw);
            var neg = !!allowNeg && raw.charAt(0) === "-";
            var out = "", seenDec = false;
            for (var i = 0; i < raw.length; i++) {
                var c = raw.charAt(i);
                if (c >= "0" && c <= "9") out += c;
                else if (c === decSep && !seenDec) { out += decSep; seenDec = true; }
            }
            var p = prec >= 0 ? prec : precision();
            var sepIdx = out.indexOf(decSep);
            if (sepIdx >= 0) out = (p > 0) ? out.slice(0, sepIdx + 1 + p) : out.slice(0, sepIdx);
            // Clamp magnitude to Int32.MaxValue so the server never receives an overflow value.
            if (out !== "" && out !== decSep && Math.abs(parseNum(out)) > AMOUNT_MAX_VALUE) out = fmtAmtInput(AMOUNT_MAX_VALUE, p);
            return (neg ? "-" : "") + out;
        }

        /* Attach locale-aware amount input guards to $inp: keypress blocks invalid chars;
           input sanitizes the full value (with caret preservation) so paste / auto-fill are
           also cleaned. prec and allowNeg mirror sanitizeAmount's parameters. */
        function bindAmountInput($inp, prec, allowNeg) {
            $inp.attr({ inputmode: "decimal", autocomplete: "off" });
            $inp.on("keypress", function (e) {
                if (e.ctrlKey || e.metaKey || e.which === 0 || e.which === 8) return;
                var ch = String.fromCharCode(e.which);
                if (ch === decSep) { if (this.value.indexOf(decSep) !== -1) e.preventDefault(); return; }
                if (ch === grpSep) return;
                if (!allowNeg && ch === "-") { e.preventDefault(); return; }
                if (ch === "-") { if (this.selectionStart !== 0 || this.value.indexOf("-") !== -1) e.preventDefault(); return; }
                if (!/[0-9]/.test(ch)) e.preventDefault();
            });
            $inp.on("input", function () {
                var before = this.value, caret = this.selectionStart;
                var after = sanitizeAmount(before, prec, allowNeg);
                if (after !== before) {
                    this.value = after;
                    var diff = after.length - before.length;
                    try { this.setSelectionRange(Math.max(0, caret + diff), Math.max(0, caret + diff)); } catch (ex) { }
                }
            });
        }

        /* ---------- short helpers ---------- */
        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            if (t && t.charAt(0) !== "[") return t;
            return (fallback !== undefined) ? fallback : t;
        }

        function precision() { return (parent && parent.StdPrecision >= 0) ? parent.StdPrecision : 2; }

        function fmtMoney(n) {
            var p = precision();
            return (+n || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: p, maximumFractionDigits: p });
        }

        function esc(s) {
            return String(s == null ? "" : s)
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
        }

        function icon(name, glyph) { return '<span class="vas-rbl-icon" data-icon="' + name + '">' + (glyph || "") + "</span>"; }

        /* ---------- per-line amount ----------
           A requisition line has one amount: the quantity as keyed, in the line's
           selected unit, times the unit price, less any discount. There is no tax
           anywhere on M_RequisitionLine, so nothing is extracted from or added to it. */
        function lineSubtotal(line) {
            var v = line.values;
            var disc = (+v.Discount || 0) / 100;
            return (+v.QtyEntered || 0) * (+v.PriceActual || 0) * (1 - disc);
        }
        /* The amount shown on the row and summed into the total. A clean SAVED line
           reads the stored M_RequisitionLine.LineNetAmt (authoritative — it is what
           MRequisitionLine wrote and what M_Requisition.TotalLines was rolled up from);
           a new or edited line has no stored value yet, so it states the live
           quantity x price. Read case-insensitively: PostgreSQL lowercases keys. */
        function lineAmount(line) {
            if (line.status === "saved" && !line.dirty) {
                var net = lineVal(line, "LineNetAmt");
                if (net != null && net !== "") return +net || 0;
            }
            return lineSubtotal(line);
        }

        /* ---------- lifecycle ---------- */
        this.init = function () {
            $root = $('<div class="vas-rbl-root"></div>');
            $body = $('<div class="vas-rbl-body"></div>');
            $emptyState = $('<div class="vas-rbl-empty" style="display:none;"></div>');
            $emptyState.text(lbl("VAS_240_NoRequisition", "Select a record to add lines"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            buildShell();
            $(document).on("mousedown.vascil", onDocMouseDown);
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy.css({ position: "absolute", width: "100%", height: "100%", "text-align": "center", "z-index": "999" });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }
        function showBusy(show) { if ($busy && $busy[0]) $busy[0].style.visibility = show ? "visible" : "hidden"; }

        this.fetchData = function (recordID, page) {
            // Framework calls fetchData(recordID) on record load -> reset to page 0; the
            // pager calls it with an explicit page. Server returns LinePageSize (20) rows.
            var reqPage = (typeof page === "number" && page >= 0) ? page : 0;
            // Capture a monotonic token before the AJAX call so a stale response arriving
            // after the user navigated to a different record can be silently discarded in
            // the success callback (mySeq !== fetchSeq means a newer load has started).
            var mySeq = ++fetchSeq;
            var isPageChange = (typeof page === "number");
            // Tear down any open dialog FIRST. The framework calls fetchData to (re)load the
            // record - including after a save - and a still-open dialog's position:fixed
            // backdrop would otherwise be left orphaned over the page (morePopoverFor is
            // cleared below but the DOM node isn't), swallowing clicks/scroll ("scroll stops
            // working after save"). closeDialogs() removes #vasRblMore/#vasRblScan/#vasRblAttr;
            // also reset the reusable AttributeControl's own state.
            closeDialogs();
            try { if (window.VIS && VIS.AttributeControl && VIS.AttributeControl.close) VIS.AttributeControl.close(); } catch (e) { }
            // NOTE: no showBusy() here. The framework already paints its own
            // vis-apanel-busy / vis_widgetloader over the tab while it loads a record;
            // adding ours (identical markup) stacked a SECOND spinner on line load. The
            // per-panel $busy is kept for our own dialog AJAX (lot/serno/attribute/delete)
            // that the framework does not cover.
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/GetPanelData",
                type: "GET", dataType: "json", data: { M_Requisition_ID: recordID, AD_Window_ID: $self.AD_Window_ID || 0, page: reqPage },
                success: function (raw) {
                    // Discard stale responses: a newer fetchData call has already started.
                    if (mySeq !== fetchSeq) { if (isPageChange) showBusy(false); return; }
                    var data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    parent = data || null;
                    linesTotal = (parent && parent.LinesTotal) || 0;
                    linePage = (parent && +parent.LinePage) || 0;
                    linePageSize = (parent && +parent.LinePageSize) || 20;
                    otherSub = (parent && +parent.OtherPagesSubtotal) || 0;
                    uomList = (parent && parent.UomList) || [];
                    // Cache the AD_Column meta (callout code + validation) once per load.
                    columnMeta = {};
                    columnNameByLc = {};
                    installedModulePrefixes = {};
                    var cols = (parent && parent.Columns) || [];
                    for (var c = 0; c < cols.length; c++) {
                        columnMeta[cols[c].ColumnName] = cols[c];
                        columnNameByLc[String(cols[c].ColumnName).toLowerCase()] = cols[c].ColumnName;
                        // Detect non-system module prefixes (e.g. "VA106_") from the column list.
                        // The server already filtered these via Env.IsModuleInstalled; recording
                        // which prefixes are present lets the JS skip module columns whose module
                        // is not installed, matching the server-side guard exactly.
                        var _pfxIdx = String(cols[c].ColumnName).indexOf("_");
                        if (_pfxIdx > 0) {
                            var _pfx = String(cols[c].ColumnName).substring(0, _pfxIdx + 1);
                            if (!SYSTEM_PREFIXES[_pfx.toUpperCase()]) installedModulePrefixes[_pfx] = true;
                        }
                    }
                    // A validated MLookup caches its list at first load; drop the cache on a
                    // new requisition so its val rule re-resolves against the new header
                    // context instead of serving the prior document's list.
                    _mlookupCache = {};
                    lines = [];
                    if (parent && parent.Lines) for (var j = 0; j < parent.Lines.length; j++) lines.push(fromServerRow(parent.Lines[j]));
                    editing = null; morePopoverFor = null;
                    render();
                    if ($root && $root[0]) $root.scrollTop(0);
                    refreshSummary();
                },
                error: function (err) {
                    console.log(err);
                    if (isPageChange && mySeq === fetchSeq) showBusy(false);
                }
            });
        };

        this.clear = function (isNewRecord) {
            // Bump fetchSeq so any in-flight GetPanelData response is discarded — the panel
            // is being cleared (e.g. user navigated away) and rendering stale data would
            // overwrite the blank state that clear() just set.
            fetchSeq++;
            // Also tear down any open dialog so a fixed backdrop isn't orphaned over the page.
            closeDialogs();
            try { if (window.VIS && VIS.AttributeControl && VIS.AttributeControl.close) VIS.AttributeControl.close(); } catch (e) { }
            parent = null; lines = [];
            if (isNewRecord) {
                // New unsaved record — show a blank panel (no message).
                if ($body)       $body.hide();
                if ($emptyState) $emptyState.hide();
            } else {
                if ($emptyState) $emptyState.text(lbl("VAS_240_NoRequisition", "Select a record to add lines"));
                // parent is already null here, so this reverts the heading to the neutral
                // wording — the panel must not keep naming the document it has let go of.
                updateDocTypeLabels();
                render();
            }
        };

        function fromServerRow(r) {
            // Start from the full column bag (every M_RequisitionLine column) so the
            // line VO carries all columns the callout may read / write.
            var vals = {};
            // Canonicalize each DB-cased key (PostgreSQL lowercases, Oracle uppercases) to the
            // dictionary ColumnName so the line bag holds exactly ONE key per column. Without
            // this, an edit / callout writes a proper-cased key (e.g. "Qty") ALONGSIDE
            // the loaded DB-cased one ("qty"), and BOTH are sent on save.
            if (r.Values) for (var k in r.Values) if (r.Values.hasOwnProperty(k)) {
                var canon = columnNameByLc[String(k).toLowerCase()] || k;
                vals[canon] = r.Values[k];
            }
            vals.M_RequisitionLine_ID = r.M_RequisitionLine_ID;
            vals.Line = r.Line;
            vals.M_Product_ID = r.M_Product_ID || 0;
            vals.C_Charge_ID = r.C_Charge_ID || 0;
            vals.M_AttributeSetInstance_ID = r.M_AttributeSetInstance_ID || 0;
            vals.Qty = r.Qty || 0;
            vals.QtyEntered = r.QtyEntered || 0;
            vals.C_UOM_ID = r.C_UOM_ID || 0;
            vals.PriceActual = r.PriceActual || 0;
            vals.Description = r.Description || "";
            if (vals.Discount == null) vals.Discount = 0;
            if (vals.Notes == null) vals.Notes = "";
            var line = {
                rowId: "r" + (++rowCounter), status: "saved", dirty: false, _priceOverride: false,
                _productType: r.ProductType || "",
                values: vals,
                display: {
                    productName: r.ProductName || "", chargeName: r.ChargeName || "",
                    uomName: r.UOMName || "",
                    attrName: r.AttrName || "", hasAttributeSet: r.M_Product_ID > 0 && (!!r.AttrName || !!r.HasAttributeSet)
                }
            };
            // Pristine snapshot of the just-loaded/just-saved state, so the row Undo can
            // revert any later edits back to this. Refreshed every time a row is rebuilt
            // from the server (initial load + mergeSavedLines after a save).
            line._saved = snapshotLine(line);
            return line;
        }

        /* Deep-clone the revertible parts of a line for the Undo snapshot. */
        function snapshotLine(line) {
            return {
                values: $.extend(true, {}, line.values),
                display: $.extend(true, {}, line.display),
                _productType: line._productType,
                _priceOverride: line._priceOverride
            };
        }

        /* Revert a dirty saved row to its last pristine (loaded/saved) snapshot. New
           (never-saved) rows have no snapshot - they are removed via Delete instead. */
        function undoLine(line) {
            if (!line || !line._saved) return;
            if (editing && editing.rowId === line.rowId) editing = null;
            commitMorePopover(); morePopoverFor = null;
            line.values = $.extend(true, {}, line._saved.values);
            line.display = $.extend(true, {}, line._saved.display);
            line._productType = line._saved._productType;
            line._priceOverride = line._saved._priceOverride;
            line.dirty = false;
            render();
        }

        /* Undo for a NEW (never-saved) line = remove it entirely. It was never persisted,
           so this is a client-only discard (no DeleteLines call) and there is no pristine
           snapshot to revert to - mirrors deleteSelected's localOnly splice. */
        function discardNewLine(line) {
            if (!line) return;
            if (editing && editing.rowId === line.rowId) editing = null;
            if (morePopoverFor === line.rowId) { morePopoverFor = null; closeDialogs(); }
            var i = lines.indexOf(line);
            if (i >= 0) lines.splice(i, 1);
            render();
        }

        /* Seed every M_RequisitionLine column (from the cached columnMeta) on a new
           line so the VO carries all columns; explicit defaults already set are
           preserved. */
        function seedAllColumns(v) {
            for (var col in columnMeta) {
                if (!columnMeta.hasOwnProperty(col)) continue;
                if (v[col] === undefined) v[col] = null;
            }
            if (parent) v.M_Requisition_ID = parent.M_Requisition_ID;
        }

        /* ---------- document kind ----------
           This panel serves ONE kind of document, so unlike VAS_107 (which fronts the
           sales order, purchase order, quotation, blanket and return windows and has to
           name whichever it is showing) it simply says "Requisition". The functions are
           kept so every document-flavoured string still goes through one place: a site
           that translates VAS_240_DocRequisition changes the whole panel at once. */
        function docNoun() {
            return lbl("VAS_240_DocRequisition", "Requisition");
        }

        /* Substitutes the document name into a message that carries a {0} placeholder.
           A site that has customised the AD_Message without the placeholder simply keeps
           its own wording — the replace is then a no-op rather than an error. */
        function docMsg(key, def) {
            return String(lbl(key, def)).replace(/\{0\}/g, docNoun());
        }

        /* The panel heading and the section's accessible name, e.g. "Requisition
           Lines & Summary". Both come from ONE message so a translation can put the
           document name wherever its language needs it. */
        function docTitle() {
            return docMsg("VAS_240_LinesSummaryFor", "{0} Lines & Summary");
        }

        /* Re-label everything that names the document. buildShell() builds the header
           before the requisition data arrives, so render() calls this once parent is
           known, and clear() calls it to go back to the neutral wording. */
        function updateDocTypeLabels() {
            if (!$body) return;
            var title = docTitle();
            $body.find(".vas-rbl-panel__title").text(title);
            $body.find(".vas-rbl-panel").attr("aria-label", title);
            // The empty state is deliberately NOT document-flavoured: render() shows it
            // precisely when there is no record selected, so there is no kind to name.
        }

        /* ---------- shell ---------- */
        function buildShell() {
            $body.empty();
            var $panel = $('<section class="vas-rbl-panel" aria-label="' + esc(docTitle()) + '"></section>');

            var $header = $('<header class="vas-rbl-panel__header"></header>');
            $header.append('<div><h2 class="vas-rbl-panel__title">' + esc(docTitle()) + '</h2></div>');

            var $actions = $('<div class="vas-rbl-panel__actions"></div>');
            // Scan hidden for now (handler/markup kept so it can be re-enabled by
            // dropping the vas-rbl-is-hidden class).
            var $scanBtn = $('<button type="button" class="vas-rbl-btn vas-rbl-btn--outline vas-rbl-is-hidden" data-action="open-scan">' + icon("scan-line", "▭") +
                "<span>" + esc(lbl("VAS_240_Scan", "Scan")) + "</span></button>");
            $addBtn = $('<button type="button" class="vas-rbl-btn vas-rbl-btn--outline" data-action="add-line" title="' + esc(lbl("VAS_240_AddLine", "Add line")) + ' (Ctrl+Alt+N)">' + icon("plus", "+") +
                "<span>" + esc(lbl("VAS_240_AddLine", "Add line")) + "</span></button>");
            $saveBtn = $('<button type="button" class="vas-rbl-btn vas-rbl-btn--save vas-rbl-is-disabled" data-action="save-rows" title="' + esc(lbl("VAS_240_SaveRow", "Save row")) + ' (Ctrl+Alt+S)"></button>');
            $deleteBtn = $('<button type="button" class="vas-rbl-btn vas-rbl-btn--danger vas-rbl-is-disabled" data-action="delete-selected" title="' + esc(lbl("VAS_240_DeleteRecord", "Delete record")) + ' (Ctrl+Alt+D)" disabled>' +
                icon("trash", "🗑") + "<span>" + esc(lbl("VAS_240_DeleteRecord", "Delete record")) + ' <span class="vas-rbl-sel-count"></span></span></button>');
            $refreshBtn = $('<button type="button" class="vas-rbl-btn vas-rbl-btn--outline" data-action="refresh" title="' + esc(lbl("VAS_240_Refresh", "Refresh")) + ' (Ctrl+Alt+Q)">' +
                icon("refresh-cw", "↺") + "<span>" + esc(lbl("VAS_240_Refresh", "Refresh")) + "</span></button>");
            $actions.append($scanBtn, $addBtn, $saveBtn, $deleteBtn, $refreshBtn);
            $header.append($actions);
            $panel.append($header);

            var $table = $('<div class="vas-rbl-table" role="table"></div>');
            $table.append(buildHeadRow());
            $linesBody = $('<div class="vas-rbl-tbody"></div>');
            $table.append($linesBody);
            $totalsRow = $('<div class="vas-rbl-totals-block"></div>');
            $table.append($totalsRow);
            $panel.append($table);
            // Server-side line pager (20/page): "X-Y of N" + prev/next.
            $pager = $('<div class="vas-rbl-linepager" style="display:none;"></div>');
            $pager.on("click", "[data-act=lp-prev]", function () { gotoLinePage(linePage - 1); });
            $pager.on("click", "[data-act=lp-next]", function () { gotoLinePage(linePage + 1); });
            $panel.append($pager);
            $body.append($panel);

            $header.on("click", "[data-action=open-scan]", openScanDialog);
            $header.on("click", "[data-action=add-line]", function () { addLine(); });
            $header.on("click", "[data-action=refresh]", function () { if (parent && parent.M_Requisition_ID) $self.fetchData(parent.M_Requisition_ID, linePage); });
            // Save on mousedown (not click): mousedown fires BEFORE the focused cell
            // editor blurs, so we can flush that pending edit ourselves and the action
            // never gets lost to a blur/commit re-render happening between mousedown and
            // mouseup. preventDefault keeps focus from being stolen first.
            $saveBtn.on("mousedown", function (e) { e.preventDefault(); flushAndSave(); });
            // Keyboard activation (Enter / Space) fires click with detail 0 and no
            // mousedown - handle that here so the button stays accessible, without
            // double-firing on a real mouse click (detail >= 1, already handled above).
            $saveBtn.on("click", function (e) { if (e.detail === 0) { flushAndSave(); } });
            $deleteBtn.on("click", deleteSelected);
        }

        function buildHeadRow() {
            var $row = $('<div class="vas-rbl-row vas-rbl-row--head" role="row"></div>');
            $selectAll = $('<input type="checkbox" aria-label="' + esc(lbl("VAS_240_SelectAll", "Select all lines")) + '" />');
            $row.append($('<div class="vas-rbl-cell vas-rbl-cell--check" role="columnheader"></div>').append($selectAll));
            $selectAll.on("change", function () {
                if (selectedCount() === lines.length) clearSelection();
                else for (var i = 0; i < lines.length; i++) lines[i]._sel = true;
                render();
            });
            $row.append('<div class="vas-rbl-cell" role="columnheader">' + esc(lbl("VAS_240_ProductCharge", "Product / Charge")) + "</div>");
            $row.append('<div class="vas-rbl-cell" role="columnheader">' + esc(lbl("Description", "Description")) + "</div>");
            $row.append('<div class="vas-rbl-cell vas-rbl-cell--right" role="columnheader">' + esc(lbl("VAS_240_QtyUom", "Quantity / UOM")) + "</div>");
            $row.append('<div class="vas-rbl-cell vas-rbl-cell--right vas-rbl-hdr-price" role="columnheader">' + esc(lbl("VAS_240_UnitPrice", "Unit Price")) + "</div>");
            $row.append('<div class="vas-rbl-cell vas-rbl-cell--right" role="columnheader">' + esc(lbl("VAS_240_LineAmt", "Line Amount")) + "</div>");
            $row.append('<div class="vas-rbl-cell vas-rbl-cell--more" role="columnheader" aria-label="' + esc(lbl("VAS_240_More", "More")) + '"></div>');
            return $row;
        }

        /* ---------- render ---------- */
        function render() {
            if (!parent || !parent.M_Requisition_ID) { $body.hide(); $emptyState.show(); return; }
            $emptyState.hide(); $body.show();
            // Read-only requisition (completed/void/closed): mark the panel so disabled
            // controls (checkbox, "...") show a not-allowed cursor via their (enabled)
            // parent cell - a disabled control ignores its own `cursor` in Chromium, so
            // the cell shows it instead.
            $body.toggleClass("vas-rbl-locked", !panelEditable());
            // The heading is built before the requisition data arrives, so it is written
            // once the header is known.
            updateDocTypeLabels();

            $linesBody.empty();
            if (!lines.length) {
                $linesBody.append('<div class="vas-rbl-emptyrow">' + esc(lbl("VAS_240_NoLines", "No lines yet - use Add line")) + "</div>");
            } else {
                for (var i = 0; i < lines.length; i++) $linesBody.append(renderRow(lines[i]));
            }
            renderTotals();
            renderHeaderButtons();
            renderPager();
        }

        /* Server-side line pager: "Showing X-Y of N" on the left, "‹ P of Q ›" on the
           right. Shown whenever the requisition has saved lines (even a single page). */
        function renderPager() {
            if (!$pager) return;
            var total = linesTotal || 0, size = linePageSize || 20;
            if (!total) { $pager.hide().empty(); return; }
            var pageCount = Math.max(1, Math.ceil(total / size));
            var start = linePage * size + 1;
            var end = Math.min(total, (linePage + 1) * size);
            var showing = lbl("VAS_240_Showing", "Showing") + " " + start + "–" + end + " " + lbl("VAS_240_Of", "of") + " " + total;
            var pageInfo = (linePage + 1) + " " + lbl("VAS_240_Of", "of") + " " + pageCount;
            $pager.html(
                '<span class="vas-rbl-linepager__showing">' + esc(showing) + "</span>" +
                '<div class="vas-rbl-linepager__nav">' +
                '<button type="button" class="vas-rbl-attr-pagebtn" data-act="lp-prev"' + (linePage <= 0 ? " disabled" : "") +
                ' aria-label="' + esc(lbl("VAS_240_Prev", "Previous")) + '">' + icon("chevron-left", "‹") + "</button>" +
                '<span class="vas-rbl-linepager__info">' + esc(pageInfo) + "</span>" +
                '<button type="button" class="vas-rbl-attr-pagebtn" data-act="lp-next"' + (linePage >= pageCount - 1 ? " disabled" : "") +
                ' aria-label="' + esc(lbl("VAS_240_Next", "Next")) + '">' + icon("chevron-right", "›") + "</button>" +
                "</div>"
            ).show();
        }

        /* Sync the pager state from a save/delete response (which returns the refreshed page). */
        function applyLinePaging(res) {
            if (!res) return;
            if (typeof res.LinesTotal === "number") linesTotal = res.LinesTotal;
            if (typeof res.LinePage === "number") linePage = res.LinePage;
            if (res.LinePageSize) linePageSize = +res.LinePageSize || linePageSize;
            if (typeof res.OtherPagesSubtotal === "number") otherSub = res.OtherPagesSubtotal;
        }

        /* Load another page of saved lines. Guards unsaved work so a page change never
           silently discards a new/edited row. */
        function gotoLinePage(p) {
            if (!parent || !parent.M_Requisition_ID) return;
            var pageCount = Math.max(1, Math.ceil((linesTotal || 0) / (linePageSize || 20)));
            if (p < 0) p = 0; if (p > pageCount - 1) p = pageCount - 1;
            if (p === linePage) return;
            if (unsavedLines().length) { showToast(lbl("VAS_240_SavePageFirst", "Save or discard your changes before changing page")); return; }
            $self.fetchData(parent.M_Requisition_ID, p);
        }

        /* The totals block. A requisition has one figure: the sum of its line amounts,
           which is what M_Requisition.TotalLines holds. otherSub carries the saved lines
           NOT on this page (from the server), and the loaded page is summed live so the
           total tracks an edit before it is saved. */
        function renderTotals() {
            $totalsRow.empty();
            var total = otherSub;
            for (var i = 0; i < lines.length; i++) total += lineAmount(lines[i]);
            var sym = (parent && parent.CurSymbol) ? parent.CurSymbol + " " : "";
            $totalsRow.append(totalsRow(lbl("VAS_240_TotalLines", "Total Lines") + ":", sym + fmtMoney(total), true));
        }
        function totalsRow(label, value, grand) {
            return '<div class="vas-rbl-totals-row' + (grand ? " vas-rbl-totals-row--grand" : "") + '">' +
                '<span class="vas-rbl-totals-row__label">' + label + '</span>' +
                '<span class="vas-rbl-totals-row__value">' + esc(value) + '</span></div>';
        }
        /* Kept as the single "totals changed" entry point so load / save / delete all
           call the same thing (VAS_107 fetches a tax breakdown here; a requisition has
           no second document total to fetch, so the block simply re-renders). */
        function refreshSummary() {
            renderTotals();
        }

        /* Re-reads the M_Requisition header record from the DB so the VIS framework status
           bar (TotalLines) reflects the amount MRequisitionLine.Save() just rolled up.
           curTab.dataRefresh() is the mTab-level wrapper: it resolves currentRow, calls
           gridTable.dataRefresh(rowIndex) to fetch the fresh row, then calls
           setCurrentRow(n, true) which fires fireDataStatusChanged and updates the status
           bar. Calling gridTable.dataRefresh() directly (without a row index) skips the
           status event entirely and leaves the bar stale. */
        function refreshHeaderRecord() {
            if ($self.curTab && typeof $self.curTab.dataRefresh === "function") {
                $self.curTab.dataRefresh();
            }
        }

        /* The panel is editable only while the requisition can still take line changes -
           server-computed IsEditable = !Processed && DocStatus NOT IN (CO, CL, VO, RE).
           When false the whole panel is read-only: no Add / Save / Delete and no cell edit. */
        function panelEditable() { return !!(parent && parent.IsEditable); }

        /* Returns true when the parent header tab has unsaved changes — i.e. the user has
           edited a header field (pricelist, BPartner, etc.) without saving yet, or the header
           record itself is brand-new and has never been saved.
           gridTable.rowChanged holds the ROW INDEX of the dirty row (>= 0) when a header
           field has been modified, or -1 when there are no unsaved changes. Using >= 0 (not
           !! or truthy) is critical: -1 is truthy in JavaScript, so the naïve !!rowChanged
           would fire false positives on every operation even with a clean header.
           getIsInserting() covers the new-record case where no row index is assigned yet. */
        function isHeaderDirty() {
            var gt = $self.curTab && $self.curTab.gridTable;
            if (!gt) return false;
            return (gt.rowChanged >= 0) || (typeof gt.getIsInserting === "function" && !!gt.getIsInserting());
        }

        function renderHeaderButtons() {
            var n = unsavedLines().length;
            var locked = !panelEditable();
            $addBtn.removeClass("vas-rbl-is-hidden");
            $saveBtn.removeClass("vas-rbl-is-hidden");
            var plural   = n > 1 ? lbl("VAS_240_PluralS", "s") : "";
            var countTxt = n > 0 ? " (" + n + ")" : "";
            $saveBtn.html(icon("hard-drive", "💾") + "<span>" + esc(lbl("VAS_240_SaveRow", "Save row")) + plural + countTxt + "</span>");
            var sc = selectedCount();
            $deleteBtn.find(".vas-rbl-sel-count").text(sc > 0 ? "(" + sc + ")" : "");
            if ($selectAll) $selectAll.prop("checked", lines.length > 0 && sc === lines.length).prop("disabled", locked);
            // Save button: HTML-disable only when the document is locked (completed/void/etc)
            // so that mousedown still fires when n===0 (the user is editing a field that hasn't
            // been committed yet). Visual disabled state uses the CSS class only — matching
            // the VAS_074 pattern. Without this, clicking Save while a field is in edit mode
            // requires two clicks: the first click is swallowed by the disabled attribute.
            $saveBtn.prop("disabled", locked).toggleClass("vas-rbl-is-disabled", locked || n === 0);
            VAS.PanelUtil.applyButtonState([
                { $el: $addBtn,     disabled: locked,              disabledCls: "vas-rbl-is-disabled" },
                { $el: $deleteBtn,  disabled: sc === 0 || locked,  disabledCls: "vas-rbl-is-disabled" },
                { $el: $refreshBtn, disabled: false,               disabledCls: "vas-rbl-is-disabled" }
            ]);
        }

        function renderRow(line) {
            var v = line.values;
            var $row = $('<div class="vas-rbl-row vas-rbl-row--line" role="row" data-rowid="' + line.rowId + '"></div>');
            if (line._sel) $row.addClass("is-selected");
            if (line.status === "new" || line.dirty) $row.addClass("is-unsaved");

            // Read-only requisition (completed/void/closed): the row can't be
            // deleted, so its selection checkbox is disabled too.
            var cb = $('<input type="checkbox" />').prop("checked", !!line._sel).prop("disabled", !panelEditable());
            cb.on("change", function () { line._sel = this.checked; renderHeaderButtons(); $row.toggleClass("is-selected", this.checked); });
            $row.append($('<div class="vas-rbl-cell vas-rbl-cell--check" role="cell"></div>').append(cb));

            // Clicking anywhere on the row toggles its selection checkbox, so the whole
            // record is easy to pick (e.g. for delete). Clicks that land on an interactive
            // control - the editable field inputs, the UOM / Tax selects, the "..." / attr
            // buttons and links, or the checkbox itself - keep their own behaviour (enter
            // edit / open control) and must NOT also toggle selection.
            $row.on("click", function (e) {
                if (!panelEditable()) return;   // checkbox is disabled on a locked requisition
                if ($(e.target).closest("input, select, textarea, button, a, [role=button], .vas-rbl-attr-link").length) return;
                line._sel = !line._sel;
                cb.prop("checked", line._sel);
                $row.toggleClass("is-selected", line._sel);
                renderHeaderButtons();
            });

            $row.append(renderPrimaryCell(line));
            $row.append(renderEditableCell(line, "description", v.Description, lbl("VAS_240_AddDescription", "Add description…"), { maxLength: colFieldLength("Description") }));
            $row.append(renderQtyUomCell(line));
            $row.append(renderEditableCell(line, "price", v.PriceActual, "0" + decSep + "00", { align: "right", amount: true, maxLength: colFieldLength("PriceActual") }));

            var amt = lineAmount(line);
            // Show any non-zero amount (negative is valid on discount/credit lines); mirrors VAS_074.
            $row.append($('<div class="vas-rbl-cell vas-rbl-cell--right" role="cell"></div>')
                .append('<span class="vas-rbl-amt">' + (amt ? esc(fmtMoney(amt)) : "") + "</span>"));

            $row.append(renderMoreCell(line));
            // Per-row inline validation error (set by validateUnsaved on Save) - shown as a
            // red label on the row itself instead of a global toast, so each failing record
            // in a multi-row save is flagged in place.
            if (line._error) {
                $row.addClass("is-invalid")
                    .append('<div class="vas-rbl-row-error" role="alert">' + esc(line._error) + "</div>");
            }
            // Re-apply the per-row spinner after a re-render so an in-flight save (or
            // callout) keeps its indicator even if render() rebuilds the rows (e.g. the
            // user added a new line while this row was still saving).
            if (line._busy || line._saving) $row.addClass("is-busy").append(rowSpinHtml(line._saving ? lbl("VAS_240_Saving", "Saving…") : lbl("VAS_240_Calculating", "Calculating…")));
            return $row;
        }

        function primaryField(line) { return (line.values.C_Charge_ID > 0 && line.values.M_Product_ID <= 0) ? "charge" : "product"; }
        function primaryValue(line) { return line.values.M_Product_ID > 0 ? line.display.productName : line.display.chargeName; }

        /* Resting (non-editing) cell value. Rendered as a borderless, read-only
           <input> - identical styling to the edit control - so the cell never swaps
           <p> <-> <input> (no layout fluctuation) and shows no border until focused.
           Clicking it enters edit mode exactly like the old <p> did. */
        function dispInput(line, field, text, opts) {
            opts = opts || {};
            var editable = parent && parent.IsEditable;
            // draggable=false stops the browser starting a text-drag on the readonly
            // input (which flashes the "not-allowed" / no-drop cursor while dragging).
            var $i = $('<input type="text" readonly tabindex="-1" draggable="false" class="vas-rbl-cell-edit__input vas-rbl-cell-disp" />');
            $i.val(text || "");
            if (opts.placeholder) $i.attr("placeholder", opts.placeholder);
            if (opts.align === "right") $i.css("text-align", "right");
            if (opts.cls) $i.addClass(opts.cls);
            $i.attr("title", text || "");
            if (editable && !opts.readOnly) $i.on("click", function () { startEdit(line, field); });
            else {
                $i.addClass("vas-rbl-cell-disp--ro");
                // A COLUMN-level read-only field (e.g. C_UOM_ID ReadOnlyLogic true) is rendered
                // DISABLED, not just readonly: `readonly` has no effect on a <select> and a plain
                // readonly text cell still looks editable, so disable + grey it so it's clearly
                // locked and browser-enforced. (A whole-document lock keeps the lighter readonly
                // look; that path leaves opts.readOnly unset.)
                if (opts.readOnly) $i.prop("disabled", true);
            }
            return $i;
        }

        function renderPrimaryCell(line) {
            var editable = parent && parent.IsEditable;
            var pField = primaryField(line);
            var isEditing = editing && editing.rowId === line.rowId && (editing.field === "product" || editing.field === "charge");
            var cell = $('<div class="vas-rbl-cell" role="cell"></div>');
            var wrap = $('<div class="vas-rbl-cell-edit"></div>');
            if (isEditing) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditing) {
                var inner = $('<div style="position:relative"></div>');
                wrap.append(inner);
                var $inp = $('<input type="text" class="vas-rbl-cell-edit__input" />');
                $inp.val(editing.field === "product" ? line.display.productName : line.display.chargeName);
                $inp.attr("placeholder", editing.field === "product" ? lbl("VAS_240_SearchProduct", "Search product…") : lbl("VAS_240_SearchCharge", "Search charge…"));
                $inp.on("input", function () { scheduleCatalog($(this).val(), inner, line, $inp); });
                $inp.on("blur", function (e) {
                    if (e.relatedTarget && $(e.relatedTarget).attr("data-catalog-item") === "true") return;
                    commitPrimary(line, editing && editing.field, $inp.val());
                });
                $inp.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Tab") { e.preventDefault(); advanceField(line, editing.field, $inp.val(), e.shiftKey ? -1 : 1); return; }
                    if (e.key === "ArrowDown") { e.preventDefault(); setHighlight(catalog.highlight + 1); return; }
                    if (e.key === "ArrowUp") { e.preventDefault(); setHighlight(catalog.highlight - 1); return; }
                    if (e.key === "Enter") {
                        if (catalog.results.length > 0) { e.preventDefault(); commitCatalogItem(line, catalog.results[Math.min(catalog.highlight, catalog.results.length - 1)]); return; }
                        $inp.blur(); return;
                    }
                    if (e.key === "Escape") { editing = null; render(); }
                });
                inner.append($inp);
                resetCatalog($inp.val(), inner, line, $inp);
                setTimeout(function () { $inp.focus(); }, 0);
            } else {
                var pv = primaryValue(line);
                wrap.append(dispInput(line, pField, pv, { placeholder: lbl("VAS_240_AddProductCharge", "Add product / charge…") }));
                // For a product carrying (or able to carry) an attribute set, show the
                // attribute-set-instance description as a clickable sub-line under the
                // product. Clicking it opens the attribute control instead of editing
                // the product name (so the click must not bubble to the cell handler).
                if (line.values.M_Product_ID > 0 && (line.display.attrName || line.display.hasAttributeSet)) {
                    var hasAttr = !!line.display.attrName;
                    var attrTxt = hasAttr ? line.display.attrName : lbl("VAS_240_SetAttribute", "Set attribute…");
                    var $attr = $('<span class="vas-rbl-attr-link"></span>').text(attrTxt).attr("title", attrTxt);
                    if (!hasAttr) $attr.addClass("vas-rbl-attr-link--empty");
                    // Clickable only when the requisition is editable AND the product actually
                    // carries an attribute set (M_AttributeSet_ID > 0). On a read-only requisition,
                    // or on a saved line whose product has no attribute set defined (e.g. the
                    // set was removed after the line was created but the old ASI description
                    // still shows), the attribute is informational only - not a link (no click,
                    // no pointer cursor / hover underline).
                    if (editable && productHasAttributeSet(line)) $attr.on("click", function (e) { e.stopPropagation(); openAttrDialog(line); });
                    else $attr.addClass("vas-rbl-attr-link--disabled");
                    wrap.append($attr);
                }
            }
            return cell;
        }

        function renderEditableCell(line, field, value, placeholder, opts) {
            opts = opts || {};
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-rbl-cell' + (opts.align === "right" ? " vas-rbl-cell--right" : "") + '" role="cell"></div>');
            var wrap = $('<div class="vas-rbl-cell-edit"></div>');
            var isEditing = editing && editing.rowId === line.rowId && editing.field === field;
            if (isEditing) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditing) {
                var $inp = $('<input type="text" class="vas-rbl-cell-edit__input" />');
                $inp.val(opts.amount ? fmtAmtInput(value, field === "quantity" ? 2 : precision()) : (value || ""));
                $inp.attr("placeholder", placeholder || "");
                if (opts.maxLength > 0) $inp.attr("maxlength", opts.maxLength);   // AD_Column.FieldLength cap
                if (opts.align === "right") $inp.css("text-align", "right");
                if (opts.amount) bindAmountInput($inp);
                $inp.on("blur", function () { commitField(line, field, opts.amount ? parseNum($inp.val()) : $inp.val()); editing = null; render(); });
                $inp.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Tab") { e.preventDefault(); advanceField(line, field, opts.amount ? parseNum($inp.val()) : $inp.val(), e.shiftKey ? -1 : 1); return; }
                    if (e.key === "Enter") $inp.blur();
                    if (e.key === "Escape") { editing = null; render(); }
                });
                wrap.append($inp);
                setTimeout(function () { $inp.focus(); }, 0);
            } else {
                var disp = opts.amount ? (value ? fmtMoney(value) : "") : (value || "");
                wrap.append(dispInput(line, field, disp, { align: opts.align, placeholder: placeholder }));
            }
            return cell;
        }

        function renderQtyUomCell(line) {
            var v = line.values;
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-rbl-cell vas-rbl-cell--right" role="cell"></div>');
            var wrap = $('<div class="vas-rbl-cell-edit"></div>');
            var editQty = editing && editing.rowId === line.rowId && editing.field === "quantity";
            var uomRO = isColumnReadOnly(line, "C_UOM_ID");   // AD_Column.ReadOnlyLogic / IsReadOnly
            var editUom = editing && editing.rowId === line.rowId && editing.field === "uom" && !uomRO;
            if (editQty || editUom) wrap.addClass("is-editing");
            cell.append(wrap);

            // quantity (top)
            if (editQty) {
                var $q = $('<input type="text" class="vas-rbl-cell-edit__input" inputmode="decimal" />').val(fmtAmtInput(v.QtyEntered, 2)).css("text-align", "right");
                var qLen = colFieldLength("QtyEntered"); if (qLen > 0) $q.attr("maxlength", qLen);   // AD_Column.FieldLength cap
                bindAmountInput($q);
                $q.on("blur", function () { commitField(line, "quantity", parseNum($q.val())); editing = null; render(); });
                $q.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Tab") { e.preventDefault(); advanceField(line, "quantity", parseNum($q.val()), e.shiftKey ? -1 : 1); return; }
                    if (e.key === "Enter") $q.blur();
                    if (e.key === "Escape") { editing = null; render(); }
                });
                wrap.append($q);
                setTimeout(function () { $q.focus(); }, 0);
            } else {
                var hasQ = v.QtyEntered !== undefined && v.QtyEntered !== "" && +v.QtyEntered !== 0;
                wrap.append(dispInput(line, "quantity", hasQ ? fmtAmtInput(v.QtyEntered, 2) : "",
                    { align: "right", placeholder: lbl("VAS_240_Qty", "Qty"), cls: "vas-rbl-qtyval", readOnly: isColumnReadOnly(line, "QtyEntered") }));
            }

            // UOM (bottom) — real editable C_UOM dropdown, options filtered to this
            // line's context (C_UOM_ID AD_Val_Rule). Render with what is cached now,
            // then refine in place once the per-row list arrives.
            if (editUom) {
                var $sel = $('<select class="vas-rbl-cell-edit__select"></select>');
                fillUomOptions($sel, line);
                ensureRowLookups(line, function () {
                    if (editing && editing.rowId === line.rowId && editing.field === "uom" && $sel.closest("body").length)
                        fillUomOptions($sel, line);
                });
                $sel.on("change", function () { setUom(line, parseInt($sel.val(), 10), $sel.find("option:selected").text()); });
                $sel.on("blur", function () { editing = null; render(); });
                $sel.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Tab") { e.preventDefault(); advanceField(line, "uom", parseInt($sel.val(), 10), e.shiftKey ? -1 : 1); return; }
                    if (e.key === "Enter") $sel.blur();
                    if (e.key === "Escape") { editing = null; render(); }
                });
                wrap.append($sel);
                setTimeout(function () { $sel.focus(); }, 0);
            } else {
                wrap.append(dispInput(line, "uom", line.display.uomName || "",
                    { align: "right", placeholder: lbl("VAS_240_Uom", "UOM"), cls: "vas-rbl-uomsub vas-rbl-cell-disp--sub", readOnly: uomRO }));
            }
            return cell;
        }

        function renderMoreCell(line) {
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-rbl-cell vas-rbl-cell--more" role="cell" style="position:relative"></div>');
            // Undo affordance (↺). On a SAVED row with unsaved edits it reverts the row to
            // its last pristine snapshot; on a NEW (never-saved) row it removes the row
            // entirely (client-only discard - a new line has no snapshot to revert to).
            var canUndoEdits = line.status === "saved" && line.dirty && line._saved;
            var canDiscardNew = line.status === "new";
            if (editable && !line._saving && (canUndoEdits || canDiscardNew)) {
                var undoTitle = (canDiscardNew ? lbl("VAS_240_UndoNewLine", "Undo (remove line)") : lbl("VAS_240_UndoChanges", "Undo changes")) + " (Ctrl+Alt+Z)";
                var $undo = $('<button type="button" class="vas-rbl-undo-btn" title="' + esc(undoTitle) + '">' + icon("rotate-ccw", "↺") + "</button>");
                var undoAct = canDiscardNew ? discardNewLine : undoLine;
                // Act on mousedown + preventDefault (like Save): a single click while a
                // cell editor is focused would otherwise blur->commit->re-render and
                // destroy this button before its click fired, needing a second click.
                // preventDefault keeps the focused input from blurring; Undo discards the
                // pending edit anyway (revert snapshot / remove the row).
                $undo.on("mousedown", function (e) { e.preventDefault(); e.stopPropagation(); undoAct(line); });
                // Keyboard activation (Enter/Space) fires click with detail 0, no mousedown.
                $undo.on("click", function (e) { if (e.detail === 0) { e.stopPropagation(); undoAct(line); } });
                cell.append($undo);
            }
            var _addlCols = additionalInfoColumns(line);
            var _hasVisible = hasVisibleAdditionalFields(line, _addlCols);
            var _btnTitle = _hasVisible
                ? esc(lbl("VAS_240_More", "More"))
                : esc(lbl("VAS_240_NoAdditionalInfo", "No additional info for this line"));
            var $btn = $('<button type="button" class="vas-rbl-more-btn" title="' + _btnTitle + '">' + icon("more-horizontal", "⋯") + "</button>");
            if (morePopoverFor === line.rowId) $btn.addClass("is-open");
            if (hasAdditionalValues(line, _addlCols)) $btn.addClass("has-values");
            // Disable when read-only OR when no additional-info field is visible for this line
            // (same DisplayLogic check the modal runs — would show "No additional info").
            $btn.prop("disabled", !editable || !_hasVisible);
            // Open the additional-fields MODAL.
            $btn.on("click", function (e) { e.stopPropagation(); openMoreDialog(line); });
            // Keyboard: Tab continues the row's tab chain (forward -> save,
            // Shift+Tab -> Price). Enter / Space open the modal explicitly - relying on the
            // button's native click-on-Enter was unreliable inside the grid (the keypress
            // was swallowed before the synthetic click fired), so open it directly here.
            $btn.on("keydown", function (e) {
                if (e.key === "Tab") { e.preventDefault(); advanceField(line, "more", null, e.shiftKey ? -1 : 1); return; }
                if (e.key === "Enter" || e.key === " " || e.key === "Spacebar" || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault(); e.stopPropagation(); openMoreDialog(line); return;
                }
            });
            cell.append($btn);
            return cell;
        }

        function openMoreDialog(line) {
            closeDialogs();
            morePopoverFor = line.rowId;
            // Snapshot the line's editable state BEFORE any field is touched. Dynamic
            // fields commit live to line.values/display on change, so closing via the
            // cross (Cancel) must restore this snapshot to leave the record unchanged.
            // Done keeps the edits. Deep-copy so in-dialog edits can't mutate the snapshot.
            var moreSnapshot = {
                values:        $.extend(true, {}, line.values),
                display:       $.extend(true, {}, line.display),
                dirty:         line.dirty,
                _priceOverride: line._priceOverride,
                _error:        line._error
            };
            var primaryName = line.display.productName || line.display.chargeName ||
                (lbl("VAS_240_Line", "Line") + " " + line.values.Line);

            var $backdrop = $('<div class="vas-rbl-dialog-backdrop" id="vasRblMore"></div>');
            var $dialog   = $('<div class="vas-rbl-dialog"></div>');
            $dialog.html(
                '<header class="vas-rbl-dialog__header"><div class="vas-rbl-dialog__header-row">' +
                '<h3 class="vas-rbl-dialog__title">' + esc(primaryName) + " - " + esc(lbl("VAS_240_AdditionalInfo", "Additional Info")) + "</h3>" +
                '<button type="button" class="vas-rbl-dialog__close" data-act="cancel-more" aria-label="' + esc(lbl("VAS_240_Close", "Close")) + '" title="' + esc(lbl("VAS_240_Close", "Close")) + '">' + icon("x", "✕") + "</button>" +
                "</div></header>" +
                '<div class="vas-rbl-dialog__body vas-rbl-more-body vas-rbl-more-grid" id="vasRblMoreBody"></div>' +
                '<footer class="vas-rbl-dialog__footer vas-rbl-dialog__footer--end">' +
                '<button type="button" class="vas-rbl-btn vas-rbl-btn--primary" data-act="close-more">' + esc(lbl("VAS_240_Done", "Done")) + "</button></footer>"
            );
            $backdrop.append($dialog);
            $("body").append($backdrop);

            var $body = $dialog.find("#vasRblMoreBody");

            // Close only via Done (commit) or the X cross (cancel/discard).
            // Never dismiss on backdrop click — an outside click (e.g. on a framework
            // lookup popup) must not close the modal and lose in-flight edits.
            // Both paths return focus to the row's "..." button so Tab continues.
            function done() {
                commitMorePopover(); closeDialogs(); render(); focusMoreBtn(line);
            }
            // X = Cancel: discard everything changed in the modal and restore the line
            // to its pre-open snapshot. No mandatory validation — edits are thrown away.
            function cancel() {
                var l = lineById(line.rowId);
                if (l) {
                    l.values        = moreSnapshot.values;
                    l.display       = moreSnapshot.display;
                    l.dirty         = moreSnapshot.dirty;
                    l._priceOverride = moreSnapshot._priceOverride;
                    l._error        = moreSnapshot._error;
                }
                closeDialogs(); render(); focusMoreBtn(line);
            }
            $dialog.on("click", "[data-act=close-more]",  done);
            $dialog.on("click", "[data-act=cancel-more]", cancel);
            // Enter / Space on Done must close even though the framework shell swallows
            // the native Enter event — wire it explicitly (mirrors VAS_074).
            $dialog.on("keydown", "[data-act=close-more]", function (e) {
                if (e.key === "Enter" || e.key === " " || e.key === "Spacebar" || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault(); e.stopPropagation(); done();
                }
            });
            $dialog.on("keydown", "[data-act=cancel-more]", function (e) {
                if (e.key === "Enter" || e.key === " " || e.key === "Spacebar" || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault(); e.stopPropagation(); cancel();
                }
            });
            // Field-group Show More/Less toggle (delegated so it survives body rebuild).
            $dialog.on("click", "[data-act=fldgrp-toggle]", function (e) {
                e.preventDefault(); e.stopPropagation();
                toggleFieldGroup($(this).closest(".vas-rbl-fldgrp"));
            });

            // Building the curated fields is heavy (each FK builds a native VIS control
            // + lookup synchronously). Paint a spinner first, then build on the next
            // tick so the user sees a busy indicator rather than a frozen panel.
            $body.removeClass("vas-rbl-more-grid").addClass("vas-rbl-dialog__body--loading")
                .html('<div class="vas-rbl-dialog-loading"><span class="vas-rbl-dialog-spin" aria-label="' +
                      esc(lbl("VAS_240_Loading", "Loading…")) + '"></span></div>');
            setTimeout(function () {
                if (morePopoverFor !== line.rowId || !$body.parent().length) return;
                $body.removeClass("vas-rbl-dialog__body--loading").addClass("vas-rbl-more-grid").empty();
                primeLineContext(line);
                appendDynFields(line, $body);
                applyFieldGroups($body);
                if (!$body.children("[data-col]:not(.vas-rbl-dyn-hidden)").length)
                    $body.append('<p class="vas-rbl-empty-message">' +
                                 esc(lbl("VAS_240_NoAdditionalInfo", "No additional info for this line")) + "</p>");
                $body.find("[data-col]:not(.vas-rbl-dyn-hidden)").find("input,select,textarea").first().focus();
            }, 0);
        }

        /* ---------- edit / commit ---------- */
        function startEdit(line, field) {
            if (!panelEditable()) return;             // completed/void/reversed/closed -> read-only
            if (line._saving) return;                 // row is being saved - locked until it returns
            if (fieldReadOnly(line, field)) return;   // AD_Column.ReadOnlyLogic / IsReadOnly
            commitMorePopover(); morePopoverFor = null;
            editing = { rowId: line.rowId, field: field };
            if (field === "product" || field === "charge") { catalog.term = ""; catalog.highlight = 0; }
            render();
        }

        function markDirty(line) { line.dirty = true; line._error = ""; }   // editing the row clears its inline error

        /* Loose value compare so re-committing the same value (e.g. clicking into a
           cell and tabbing out without typing) does NOT flag the line dirty - the
           Save button must reflect a GENUINE new/changed line only. */
        function sameVal(a, b) {
            return (a == null ? "" : String(a)) === (b == null ? "" : String(b));
        }

        function commitField(line, field, value) {
            var v = line.values, changed = false;
            if (field === "description") { if (!sameVal(v.Description, value)) { v.Description = value; changed = true; } }
            else if (field === "quantity") { var nq = value > 0 ? value : 0; if (!sameVal(v.QtyEntered, nq)) { v.QtyEntered = nq; changed = true; } }
            else if (field === "price") { if (!sameVal(v.PriceActual, value)) { v.PriceActual = value; line._priceOverride = true; changed = true; } }
            if (changed) markDirty(line);
            // A quantity change re-runs the line callout (quantity price-breaks + amounts,
            // attribute-aware) - same as UOM.
            if (changed && field === "quantity" && v.M_Product_ID > 0) runCallout(line, "QtyEntered");
            // A manual price change re-runs the line callout too, so LineNetAmt recomputes
            // from the entered price. The server keeps the entered price when
            // PriceOverride is set, so the manual override survives the round trip
            // (line._priceOverride stays set).
            else if (changed && field === "price" && v.M_Product_ID > 0) runCallout(line, "PriceActual");
        }

        function setUom(line, uomId, uomName) {
            if (sameVal(line.values.C_UOM_ID, uomId)) return;   // unchanged - no dirty / callout
            line.values.C_UOM_ID = uomId;
            line.display.uomName = uomName;
            markDirty(line);
            // Changing the UOM always re-runs the line callout (price/qty/amounts are
            // per-UOM), regardless of an earlier manual price edit.
            if (line.values.M_Product_ID > 0) runCallout(line, "C_UOM_ID");
        }

        /* ---------- per-row context-filtered lookups (AD_Val_Rule) ----------
         * The UOM (and catalog) controls load only values valid in the current
         * line's context: the column's AD_Val_Rule is resolved server-side against
         * the line's values + requisition header + session context. The filtered
         * UOM list is cached on the line, keyed by product / charge, so the dropdown
         * refines the moment the list arrives and is reused while context is
         * unchanged; it falls back to the panel-load list until then. */

        /* Compact, val-rule-relevant subset of a line's values (scalar + set only,
           no free text) - small enough to ride a catalog GET query string. */
        function compactCtx(values) {
            var out = {};
            if (!values) return out;
            for (var k in values) {
                if (!values.hasOwnProperty(k)) continue;
                if (k === "Description" || k === "Notes") continue;
                var val = values[k];
                if (val === null || val === undefined || val === "") continue;
                if (typeof val === "object") continue;
                if (typeof val === "number" && val === 0) continue;
                out[k] = val;
            }
            return out;
        }

        function rowLookupSig(line) {
            var v = line.values;
            return (v.M_Product_ID || 0) + ":" + (v.C_Charge_ID || 0) + ":" + (v.M_AttributeSetInstance_ID || 0);
        }
        function rowUomList(line) {
            return (line._lk && line._lk.sig === rowLookupSig(line) && line._lk.uom) ? line._lk.uom : uomList;
        }
        function fillUomOptions($sel, line) {
            var v = line.values, list = rowUomList(line), found = false;
            $sel.empty();
            for (var i = 0; i < list.length; i++) {
                var o = $("<option></option>").attr("value", list[i].C_UOM_ID).text(list[i].Name);
                if (list[i].C_UOM_ID === v.C_UOM_ID) { o.prop("selected", true); found = true; }
                $sel.append(o);
            }
            if (!found && v.C_UOM_ID > 0) $sel.prepend($("<option></option>").attr("value", v.C_UOM_ID).text(line.display.uomName || "").prop("selected", true));
        }
        /* Fetch (once, then cache) the UOM list valid for THIS line's context.
           cb() runs when the cache is ready (immediately when already cached). */
        function ensureRowLookups(line, cb) {
            if (!parent || !parent.M_Requisition_ID) { if (cb) cb(); return; }
            var sig = rowLookupSig(line);
            if (line._lk && line._lk.sig === sig) {
                if (line._lk.loaded) { if (cb) cb(); return; }
                if (line._lk.loading) { if (cb) line._lk.cbs.push(cb); return; }
            }
            line._lk = { sig: sig, loading: true, loaded: false, uom: null, cbs: cb ? [cb] : [] };
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/GetLookupData",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ M_Requisition_ID: parent.M_Requisition_ID, RowValues: compactCtx(line.values) }) },
                success: function (raw) {
                    var d = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!line._lk || line._lk.sig !== sig) return;   // context moved on
                    line._lk.uom = (d && d.UomList) || null;
                    line._lk.loading = false; line._lk.loaded = true;
                    var cbs = line._lk.cbs || []; line._lk.cbs = [];
                    for (var j = 0; j < cbs.length; j++) cbs[j]();
                },
                error: function (err) { console.log(err); if (line._lk) line._lk.loading = false; }
            });
        }

        function silentSet(line, field, value) { line.values[field] = value; line.dirty = true; }

        function commitMorePopover() {
            if (!morePopoverFor) return;
            var line = lineById(morePopoverFor);
            if (!line) return;
            // Legacy Discount/Notes ids (only present if re-added to the curated list);
            // dynamic fields commit themselves on change, so don't force-dirty here.
            var $d = $("#vasRblDisc-" + morePopoverFor), $n = $("#vasRblNotes-" + morePopoverFor);
            if ($d.length) { line.values.Discount = parseNum($d.val()); line.dirty = true; }
            if ($n.length) { line.values.Notes = $n.val(); line.dirty = true; }
        }

        /* Tab order: primary -> description -> quantity -> uom -> price -> "..." -> new row */
        function advanceField(line, currentField, value, direction) {
            // Commit the current field's value (UOM and the "..." button do not carry one).
            if (currentField === "product" || currentField === "charge") commitPrimary(line, currentField, value, true);
            else if (currentField !== "uom" && currentField !== "more") commitField(line, currentField, value);

            var isPrimary = currentField === "product" || currentField === "charge";
            var idx = isPrimary ? 0 : TAB_ORDER.indexOf(currentField);
            var nextIdx = idx + direction;
            // Skip read-only fields (e.g. UOM with a true AD_Column.ReadOnlyLogic).
            while (nextIdx >= 0 && nextIdx < TAB_ORDER.length) {
                var nf = TAB_ORDER[nextIdx];
                if (nf === "more") break;
                if (!fieldReadOnly(line, nf === "primary" ? primaryField(line) : nf)) break;
                nextIdx += direction;
            }
            if (nextIdx < 0) { editing = null; render(); return; }
            if (nextIdx >= TAB_ORDER.length) { saveThenAddLine(); return; }
            var nextField = TAB_ORDER[nextIdx];
            if (nextField === "more") {
                // The "..." cell is a button, not an editable cell - leave edit mode and
                // move keyboard focus to it so Enter / Space opens the more dialog.
                editing = null;
                render();
                focusMoreBtn(line);
                return;
            }
            var resolved = nextField === "primary" ? primaryField(line) : nextField;
            editing = { rowId: line.rowId, field: resolved };
            render();
        }

        /* Move keyboard focus to a row's "..." (More) button after a render. */
        function focusMoreBtn(line) {
            setTimeout(function () {
                var $b = $linesBody.find('[data-rowid="' + line.rowId + '"] .vas-rbl-more-btn');
                if ($b.length) $b.focus();
            }, 0);
        }

        /* ---------- line operations ---------- */
        function addLine() {
            if (!parent || !parent.IsEditable) { showToast(docMsg("VAS_240_NotEditable", "This {0} cannot take new lines")); return; }
            if (isHeaderDirty()) { showToast(lbl("VAS_240_SaveHeaderFirst", "Please save the header record before adding lines.")); return; }
            var maxLine = 0;
            for (var i = 0; i < lines.length; i++) maxLine = Math.max(maxLine, lines[i].values.Line || 0);
            var line = {
                rowId: "r" + (++rowCounter), status: "new", dirty: true, _priceOverride: false,
                values: { M_RequisitionLine_ID: 0, Line: maxLine + 10, M_Product_ID: 0, C_Charge_ID: 0, M_AttributeSetInstance_ID: 0,
                    Qty: 0, QtyEntered: 0, C_UOM_ID: 0, PriceActual: 0, Discount: 0, Notes: "", Description: "" },
                display: { productName: "", chargeName: "", uomName: "", attrName: "", hasAttributeSet: false }
            };
            seedAllColumns(line.values);
            lines.unshift(line);
            editing = { rowId: line.rowId, field: "product" };
            catalog.term = ""; catalog.highlight = 0;
            render();
        }

        function lineById(id) { for (var i = 0; i < lines.length; i++) if (lines[i].rowId === id) return lines[i]; return null; }
        function selectedLines() { return lines.filter(function (l) { return l._sel; }); }
        function selectedCount() { return selectedLines().length; }
        function clearSelection() { for (var i = 0; i < lines.length; i++) lines[i]._sel = false; }
        function unsavedLines() { return lines.filter(function (l) { return !l._saving && (l.status === "new" || l.dirty) && (l.values.M_Product_ID > 0 || l.values.C_Charge_ID > 0); }); }

        /* ---------- catalog popover ---------- */
        function scheduleCatalog(term, inner, line, $inp) {
            catalog.term = term; catalog.highlight = 0;
            if (catalog.debounce) clearTimeout(catalog.debounce);
            catalog.debounce = setTimeout(function () { resetCatalog(term, inner, line, $inp); }, SEARCH_DEBOUNCE);
        }

        /* Build the popover container ONCE and bind handlers via delegation, so
           hover / arrow navigation only toggles a class and paging only appends
           new rows - the whole list is never rebuilt (that was the scroll jank). */
        function resetCatalog(term, inner, line, $inp) {
            catalog.term = term || ""; catalog.offset = 0; catalog.hasMore = true; catalog.results = []; catalog.seq++; catalog.highlight = 0;
            catalog.$inp = $inp;   // kept so positionCatalog() can re-measure as rows load
            inner.find(".vas-rbl-catalog-popover").remove();
            catalog.$pop = $('<div class="vas-rbl-catalog-popover"></div>');
            catalog.$pop.on("scroll", function () {
                var el = this;
                if (catalog.hasMore && !catalog.loading && el.scrollTop + el.clientHeight >= el.scrollHeight - 40) loadCatalogPage(inner, line, $inp, false);
            });
            catalog.$pop.on("mousedown", ".vas-rbl-catalog-popover__item", function (e) {
                e.preventDefault();
                commitCatalogItem(line, catalog.results[+$(this).attr("data-idx")]);
            });
            catalog.$pop.on("mouseenter", ".vas-rbl-catalog-popover__item", function () { setHighlight(+$(this).attr("data-idx")); });
            inner.append(catalog.$pop);
            positionCatalog();
            // Fire the server search immediately, even for an empty term: the server uses
            // LIKE '%' which returns all products and charges so the user sees the full list
            // on first click without having to type anything.
            catalog.$pop.html('<div class="vas-rbl-catalog__hint">' + esc(lbl("VAS_240_Loading", "Loading…")) + "</div>");
            loadCatalogPage(inner, line, $inp, true);
        }

        /* Place the dropdown so it is NEVER clipped by the panel root (which is
           overflow-y:auto, a hard clip box). Open below by default; flip ABOVE only when the
           list can't fit below AND there is more room above. Either way the popover's
           max-height is clamped to the space actually available on the chosen side WITHIN
           the root, so it stays fully visible and every row is reachable via internal scroll
           (fixes: flipped-up list clipped at the top, top rows unreachable). Re-measured as
           rows load. The --above modifier attaches it flush over the input (see CSS). */
        var CATALOG_MAX_PX = 260;   // keep in sync with .vas-rbl-catalog-popover max-height (16.25em)
        function positionCatalog() {
            if (!catalog.$pop || !catalog.$pop.length) return;
            var $inp = catalog.$inp;
            if (!$inp || !$inp.length || !$inp[0].getBoundingClientRect) return;
            var r = $inp[0].getBoundingClientRect();
            // Clip boundary = the root's scroll box; fall back to the viewport.
            var clipTop = 0, clipBottom = window.innerHeight;
            if ($root && $root.length && $root[0].getBoundingClientRect) {
                var rr = $root[0].getBoundingClientRect();
                clipTop = Math.max(clipTop, rr.top);
                clipBottom = Math.min(clipBottom, rr.bottom);
            }
            var GAP = 4;   // small breathing gap from the clip edge
            var spaceBelow = clipBottom - r.bottom - GAP;
            var spaceAbove = r.top - clipTop - GAP;
            // Natural (unclamped) content height + borders, to decide whether it fits.
            var natural = (catalog.$pop[0].scrollHeight || CATALOG_MAX_PX) + 2;
            var above;
            if (natural <= spaceBelow) above = false;         // fits below - default
            else if (natural <= spaceAbove) above = true;     // fits above
            else above = spaceAbove > spaceBelow;             // neither fits - pick the roomier side
            var avail = above ? spaceAbove : spaceBelow;
            var maxH = Math.min(CATALOG_MAX_PX, Math.max(avail, 0));
            catalog.$pop.css("max-height", maxH > 0 ? (maxH + "px") : "");
            catalog.$pop.toggleClass("vas-rbl-catalog-popover--above", above);
        }

        /* Remove the catalog dropdown immediately (used on commit, before the row's busy
           opacity would make a still-open popover translucent). */
        function closeCatalog() {
            if (catalog.debounce) { clearTimeout(catalog.debounce); catalog.debounce = null; }
            if (catalog.$pop) { catalog.$pop.remove(); catalog.$pop = null; }
            catalog.$inp = null;
            catalog.results = []; catalog.loading = false;
        }

        function loadCatalogPage(inner, line, $inp, isReset) {
            if (catalog.loading || (!catalog.hasMore && !isReset)) return;
            catalog.loading = true;
            var mySeq = catalog.seq;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/SearchCatalog",
                type: "GET", dataType: "json",
                data: { M_Requisition_ID: parent.M_Requisition_ID, query: catalog.term, pageSize: CATALOG_PAGE_SIZE, offset: catalog.offset, rowContext: JSON.stringify(compactCtx(line.values)) },
                success: function (raw) {
                    if (mySeq !== catalog.seq || !catalog.$pop) { catalog.loading = false; return; }
                    var items = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw; items = items || [];
                    var start = catalog.results.length;
                    catalog.results = catalog.results.concat(items);
                    catalog.offset += items.length;
                    catalog.hasMore = items.length === CATALOG_PAGE_SIZE;
                    catalog.loading = false;
                    appendCatalogRows(items, start);
                    if (start === 0) setHighlight(0);
                },
                error: function (err) { console.log(err); catalog.loading = false; }
            });
        }

        function appendCatalogRows(items, startIdx) {
            if (!catalog.$pop) return;
            if (startIdx === 0 && !items.length) {
                catalog.$pop.html('<div class="vas-rbl-catalog__hint">' + esc(lbl("VAS_240_NoMatches", "No matches")) + "</div>");
                positionCatalog();
                return;
            }
            if (startIdx === 0) catalog.$pop.empty();
            var html = "";
            for (var i = 0; i < items.length; i++) html += catalogRowHtml(items[i], startIdx + i);
            catalog.$pop.append(html);
            // Re-measure only on the first page (height jumps from the Loading hint to the
            // list); later scroll-paged appends must NOT re-flip mid-scroll.
            if (startIdx === 0) positionCatalog();
        }

        function catalogRowHtml(it, idx) {
            var badge = it.Kind === "C" ? "charge" : "product";
            var blabel = it.Kind === "C" ? lbl("VAS_240_Charge", "Charge") : lbl("VAS_240_Product", "Product");
            // Name + type only (the key/value line is intentionally omitted to keep
            // the dropdown rows compact). Full "name (key)" stays in the tooltip.
            return '<button type="button" class="vas-rbl-catalog-popover__item" data-catalog-item="true" data-idx="' + idx +
                '" title="' + esc(it.DisplayName + (it.SearchKey ? " (" + it.SearchKey + ")" : "")) + '">' +
                '<span class="vas-rbl-catalog-popover__name">' + esc(it.DisplayName) + "</span>" +
                '<span class="vas-rbl-badge vas-rbl-badge--' + badge + '">' + esc(blabel) + "</span></button>";
        }

        /* Move the highlight by class only (no rebuild) and keep it in view. */
        function setHighlight(idx) {
            if (!catalog.$pop) return;
            var n = catalog.results.length; if (!n) return;
            idx = Math.max(0, Math.min(idx, n - 1));
            catalog.highlight = idx;
            var $items = catalog.$pop.children(".vas-rbl-catalog-popover__item");
            $items.removeClass("is-highlighted");
            var $sel = $items.eq(idx).addClass("is-highlighted");
            if ($sel.length && $sel[0].scrollIntoView) $sel[0].scrollIntoView({ block: "nearest" });
        }

        function commitCatalogItem(line, item) {
            var v = line.values, d = line.display;
            if (item.Kind === "C") {
                v.C_Charge_ID = item.RecordId; v.M_Product_ID = 0; v.M_AttributeSetInstance_ID = 0;
                d.chargeName = item.DisplayName; d.productName = ""; d.hasAttributeSet = false; d.attrName = "";
            } else {
                v.M_Product_ID = item.RecordId; v.C_Charge_ID = 0;
                d.productName = item.DisplayName; d.chargeName = "";
                d.hasAttributeSet = !!item.HasAttributeSet; v.M_AttributeSetInstance_ID = 0; d.attrName = "";
            }
            line._productType = (item.Kind === "C") ? "" : (item.ProductType || "");
            line._priceOverride = false;
            markDirty(line);
            editing = null;
            // Remove the dropdown NOW - the callout below marks the row busy (opacity),
            // and a still-open popover (a child of that row) would turn translucent and
            // bleed the row content through until the post-callout render() rebuilds it.
            closeCatalog();
            runCallout(line, item.Kind === "C" ? "C_Charge_ID" : "M_Product_ID", function () {
                // Warm the per-row UOM / tax lists for the new product / charge context.
                ensureRowLookups(line);
                if (d.hasAttributeSet) openAttrDialog(line);
                else { editing = { rowId: line.rowId, field: "description" }; render(); }
            });
        }

        function commitPrimary(line, field, newValue, fromTab) {
            // Product / Charge is a LOOKUP: a valid value is set ONLY by picking a catalog
            // row (commitCatalogItem). Text that was typed into the search box but never
            // selected from the dropdown is NOT a product/charge, so leaving the cell
            // (Tab / blur) discards it and keeps the cell on the committed record (or empty
            // on a new line). Previously the free text was kept as the display name, leaving
            // a name that no M_Product_ID / C_Charge_ID matched.
            if (fromTab) { editing = null; return; }   // advanceField owns the next field + render
            // Blur: only act while still editing THIS row's primary. If a catalog pick has
            // already advanced focus (e.g. to Description), leave that state untouched.
            if (!(editing && editing.rowId === line.rowId &&
                  (editing.field === "product" || editing.field === "charge"))) return;
            editing = null;
            render();   // repaint the cell from the committed value (revert the typed text)
        }

        /* ---------- callout host (runs the REAL framework callout) ----------
         * On Product / Charge (and UOM / attribute) change we read the changed
         * column's AD_Column.Callout (cached once in columnMeta), resolve the real
         * VIS.Model.CalloutXxx instance and invoke its method with lightweight
         * mTab / mField / ctx shims backed by THIS line + the parent requisition
         * context - the same GridTab contract the framework callout expects.
         * Whatever the callout writes via mTab.setValue lands in line.values.
         * If the framework callout isn't on the page we fall back to the server
         * CalcLine/RunCallout endpoint. No MRequisition is rebuilt and the callout
         * code is read from cache, not re-queried each change. */
        var ID_COLS = { M_Product_ID: 1, C_Charge_ID: 1, C_UOM_ID: 1, M_AttributeSetInstance_ID: 1, C_Currency_ID: 1 };
        var PANEL_COLS = {
            M_Product_ID: 1, C_Charge_ID: 1, M_AttributeSetInstance_ID: 1, Qty: 1, QtyEntered: 1,
            C_UOM_ID: 1, PriceActual: 1, C_Currency_ID: 1, Discount: 1, LineNetAmt: 1,
            Description: 1, M_Requisition_ID: 1
        };
        // FALLBACK ONLY. The callout configured in the dictionary
        // (AD_Column.Callout -> columnMeta[col].Callout) ALWAYS takes priority (see
        // runCallout); this map is used ONLY when a pricing / amount column has NO callout
        // configured, so Attribute / Qty / UOM / Price still recompute the SAME way a
        // Product change does — through the real client CalloutRequisition, the requisition
        // window's own callout class (Product / Charge / Qty / Amt), NOT CalloutOrder.
        // CalloutRequisition.Qty branches on the changed column and re-fetches the price
        // for the current quantity and unit; Amt restates the line amount from the price
        // now on the line. To override any of these, just set AD_Column.Callout on the
        // column - no code change needed.
        var DEFAULT_CALLOUTS = {
            M_AttributeSetInstance_ID: "VAdvantage.Model.CalloutRequisition.Qty",
            QtyEntered: "VAdvantage.Model.CalloutRequisition.Qty; VAdvantage.Model.CalloutRequisition.Amt",
            Qty: "VAdvantage.Model.CalloutRequisition.Qty; VAdvantage.Model.CalloutRequisition.Amt",
            C_UOM_ID: "VAdvantage.Model.CalloutRequisition.Qty; VAdvantage.Model.CalloutRequisition.Amt",
            PriceActual: "VAdvantage.Model.CalloutRequisition.Amt"
        };
        /* The callout string to run for a column: the Compiere callout(s) configured in the
           dictionary (AD_Column.Callout -> columnMeta[col].Callout) ALWAYS win; only when a
           column has none do we fall back to DEFAULT_CALLOUTS. The value may be a single
           callout or a ';'-separated chain across one or more classes (e.g. CalloutOrder
           + CalloutTax); resolveCallouts parses + resolves each in order. Returns null when
           neither applies (runCallout then takes the server recompute path). */
        function columnCalloutStr(column) {
            var m = columnMeta[column];
            return (m && m.Callout) || DEFAULT_CALLOUTS[column] || null;
        }

        function runCallout(line, column, done) {
            if (!parent) { if (done) done(); return; }
            var v = line.values;
            if (v.M_Product_ID <= 0 && v.C_Charge_ID <= 0) { render(); if (done) done(); return; }
            // Default qty so pricing / discount-break logic has a quantity.
            if (!(v.QtyEntered > 0)) { v.QtyEntered = 1; }
            if (!(v.Qty > 0)) { v.Qty = v.QtyEntered; }

            // Product, charge and attribute-set-instance changes always use the server
            // RunColumnCallout path. BuildCalcLine builds a transient MRequisitionLine
            // against the real MRequisition, so the price-list lookup, the unit default and
            // the QtyEntered -> Qty conversion all happen in one reliable round-trip
            // against the same code the requisition window itself saves through.
            if (column === "M_Product_ID" || column === "C_Charge_ID" || column === "M_AttributeSetInstance_ID") {
                setRowBusy(line, true);
                calloutPending++;
                runCalloutServer(line, column, function () {
                    line._busy = false;
                    render();
                    try { if (done) done(); } finally { calloutSettled(); }
                });
                return;
            }

            // Resolve the callout(s) to run - dictionary AD_Column.Callout first, else the
            // DEFAULT_CALLOUTS fallback (see columnCalloutStr).
            var calloutStr = columnCalloutStr(column);
            var chain = calloutStr ? resolveCallouts(calloutStr) : null;
            if (chain && chain.length) {
                // The framework callout runs synchronously (blocking sync AJAX), so
                // paint the row busy indicator first, then run on the next tick so
                // the spinner is actually visible while the callout executes.
                // Counted BEFORE going async so a Save in this same tick already sees the
                // callout as pending and defers (see afterCallouts).
                setRowBusy(line, true);
                calloutPending++;
                setTimeout(function () {
                    try {
                        runRealCallout(line, column, chain);
                        sanitizeLine(line);
                        applyCalloutReadback(line);
                    } finally {
                        line._busy = false;
                        render();
                        // done() may chain another callout, so settle AFTER it has had the
                        // chance to increment — otherwise the count could dip to zero and
                        // release Save between two links of the chain.
                        try { if (done) done(); } finally { calloutSettled(); }
                    }
                }, 0);
                return;
            }
            // Async server fallback - show the busy indicator until it returns.
            setRowBusy(line, true);
            calloutPending++;
            runCalloutServer(line, column, function () {
                line._busy = false;
                render();
                try { if (done) done(); } finally { calloutSettled(); }
            });
        }

        function rowSpinHtml(label) { return '<span class="vas-rbl-row-spin" aria-label="' + esc(label || "") + '"></span>'; }

        /* Toggle a per-row spinner immediately (no full re-render, so it paints before a
           synchronous callout blocks the thread). Used for both callouts and per-row save. */
        function setRowBusy(line, on, label) {
            line._busy = on;
            var $r = $linesBody.find('[data-rowid="' + line.rowId + '"]');
            $r.toggleClass("is-busy", on);
            $r.find(".vas-rbl-row-spin").remove();
            if (on) $r.append(rowSpinHtml(label || lbl("VAS_240_Calculating", "Calculating…")));
        }

        /* Resolve a single namespace path + class name to a constructor.
           ViennaAdvantage.* and VAdvantage.* are aliased to VIS.* so the
           standard framework callouts resolve the same as in VAS_074. Any other
           namespace (VAS, VAFAM, …) is walked on window directly. */
        function resolveCalloutCtor(ns, cls) {
            if (!ns || ns.length === 0) {
                return (window.VIS && VIS.Model && typeof VIS.Model[cls] === "function") ? VIS.Model[cls] : null;
            }
            var path = ns.slice();
            if (path[0] === "VAdvantage" || path[0] === "ViennaAdvantage") path[0] = "VIS";
            var scope = (typeof window !== "undefined") ? window : null;
            for (var j = 0; j < path.length && scope; j++) scope = scope[path[j]];
            return (scope && typeof scope[cls] === "function") ? scope[cls] : null;
        }

        /* Parse "Ns.Class.Method;Ns2.Class2.Method2" into resolvable callout
           instances + methods. Method match is case-insensitive (AD_Column may
           store "product" while the class exports "Product"). */
        function resolveCallouts(calloutStr) {
            var out = [];
            var parts = String(calloutStr).split(";");
            for (var i = 0; i < parts.length; i++) {
                var token = parts[i].trim();
                if (!token) continue;
                var seg = token.split(".");
                if (seg.length < 2) continue;
                var method = seg.pop();
                var cls    = seg.pop();
                var ctor   = resolveCalloutCtor(seg, cls);
                if (typeof ctor !== "function") continue;
                var inst;
                try { inst = new ctor(); } catch (e) { continue; }
                var fn = inst[method];
                if (typeof fn !== "function") {
                    for (var k in inst) {
                        if (typeof inst[k] === "function" && k.toLowerCase() === method.toLowerCase()) { fn = inst[k]; break; }
                    }
                }
                if (typeof fn === "function") out.push({ inst: inst, fn: fn });
            }
            return out;
        }

        function runRealCallout(line, column, chain) {
            var mTab    = makeMTab(line);
            var mField  = makeFieldShim(line, column);
            var ctxShim = makeCalloutCtx(line);
            VAS.PanelUtil.executeCalloutChain(chain, $self.windowNo || 0, mTab, mField, ctxShim, line.values[column]);
        }

        function makeMTab(line) {
            return VAS.PanelUtil.makeMTabShim({
                getVal:       function (col)      { return fieldGet(line, col); },
                setVal:       function (col, val) { line.values[col] = val; },
                findColumn:   function (col)      { return (columnMeta[col] || PANEL_COLS[col]) ? 0 : -1; },
                getField:     function (col) {
                    var fm = columnMeta[col];
                    return (fm && fm.IsTabField) ? makeFieldShim(line, col) : null;
                },
                keyColumnName: "M_RequisitionLine_ID"
            });
        }
        function makeMField(column, line) { return makeFieldShim(line, column); }

        function makeFieldShim(line, col) {
            return VAS.PanelUtil.makeFieldShim({
                getVal: function (c) { return fieldGet(line, c); },
                setVal: function (c, v) { line.values[c] = v; },
                col:    col,
                meta:   columnMeta[col]
            });
        }

        function fieldGet(line, col) {
            if (col === "M_Requisition_ID") return parent.M_Requisition_ID;
            if (col === "QtyEntered") {
                var qe = lineVal(line, "QtyEntered");
                return (qe == null || qe === "") ? 0 : qe;
            }
            if (col === "Qty") {
                var qo = lineVal(line, "Qty");
                return (qo == null || qo === "") ? 0 : qo;
            }
            // Case-insensitive read: a saved line's values are keyed in DB case
            // (PostgreSQL lowercases unquoted identifiers, Oracle uppercases), which can
            // differ from the dictionary ColumnName a callout asks for - e.g. the VA106
            // TCS callout reads getValue("LineTotalAmt") / getValue("VA106_TaxCollectedAtSource_ID").
            var v = lineVal(line, col);
            if (v === undefined) return null;
            // An unset FK reads as null (live GridTab semantics) - our line VO stores
            // unset ids as 0, but the framework callouts test `getValue(col) != null`
            // for exclusivity (e.g. Charge clears itself if M_Product_ID isn't null);
            // `0 != null` would wrongly wipe C_Charge_ID. Coerce only FK (*_ID) zeros,
            // never numeric amounts (Qty / Price 0 must stay 0).
            if (v === 0 && col.length > 3 && col.slice(-3) === "_ID") return null;
            return v;
        }

        /* ctx shim: resolves the header / line context keys the callout reads and
           captures setContext into a scratch bag. Handles single-arg (#GLOBAL)
           and (windowNo, key) forms. */
        function makeCalloutCtx(line) {
            var scratch = {};
            function resolve(key) {
                if (key == null) return "";
                key = String(key);
                if (key.charAt(0) === "#") key = key.substring(1);
                switch (key) {
                    case "M_Requisition_ID": return parent.M_Requisition_ID || 0;
                    case "M_Product_ID": return line.values.M_Product_ID || 0;
                    case "C_Charge_ID": return line.values.C_Charge_ID || 0;
                    case "C_UOM_ID": return line.values.C_UOM_ID || 0;
                    case "M_AttributeSetInstance_ID": return line.values.M_AttributeSetInstance_ID || 0;
                    case "M_PriceList_ID": return parent.M_PriceList_ID || 0;
                    case "C_BPartner_ID": return parent.C_BPartner_ID || 0;
                    case "AD_Org_ID": return parent.AD_Org_ID || 0;
                    case "AD_Client_ID": return parent.AD_Client_ID || 0;
                    case "M_Warehouse_ID": return parent.M_Warehouse_ID || 0;
                    case "DateDoc": return dateStr(parent.DateDoc);
                    // A requisition is always a purchase-side document: the callouts that
                    // price a line branch on this token and must price it as bought.
                    case "IsSOTrx": return "N";
                    default:
                        if (scratch[key] != null) return scratch[key];
                        // Any other header column the callout asks for (DateRequired,
                        // PriorityRule, a module's own column) - the server sends these in
                        // LogicContext, keyed by ColumnName.
                        var lg = (parent && parent.LogicContext) || {};
                        return lg.hasOwnProperty(key) ? lg[key] : "";
                }
            }
            return {
                getContext: function (a, b) { return String(resolve(b === undefined ? a : b)); },
                getContextAsInt: function (a, b) { return parseInt(resolve(b === undefined ? a : b), 10) || 0; },
                getWindowContext: function (wn, key) { return String(resolve(key)); },
                setContext: function (wn, key, val) { scratch[key] = val; },
                getAD_Client_ID: function () { return parent.AD_Client_ID || 0; }
            };
        }

        function dateStr(d) {
            if (!d) return "";
            var dt = (d instanceof Date) ? d : new Date(d);
            if (isNaN(dt.getTime())) return String(d).slice(0, 10);
            var m = dt.getMonth() + 1, day = dt.getDate();
            return dt.getFullYear() + "-" + (m < 10 ? "0" + m : m) + "-" + (day < 10 ? "0" + day : day);
        }

        /* Coerce id / price fields the callout may have written as null / string. */
        function sanitizeLine(line) {
            var v = line.values;
            for (var col in ID_COLS) if (ID_COLS.hasOwnProperty(col)) v[col] = parseInt(v[col], 10) || 0;
            v.PriceActual = parseNum(v.PriceActual);
            if (v.QtyEntered != null && v.QtyEntered !== "") v.QtyEntered = parseNum(v.QtyEntered);
            // Qty is the base-UOM quantity after UOM conversion. Callouts may write it
            // as a converted value; keep it as a numeric scratch for callouts that read it back.
            // The display column is QtyEntered (user-entered UOM quantity), not Qty.
            if (v.Qty != null && v.Qty !== "") v.Qty = parseNum(v.Qty);
        }

        /* Refresh display labels + product/charge exclusivity after the callout. */
        function applyCalloutReadback(line) {
            var v = line.values, d = line.display;
            if (v.M_Product_ID > 0) d.chargeName = "";
            else if (v.C_Charge_ID > 0) d.productName = "";
            d.uomName = uomName(v.C_UOM_ID) || d.uomName;
            line.dirty = true;
        }
        function uomName(id) { for (var i = 0; i < uomList.length; i++) if (uomList[i].C_UOM_ID === id) return uomList[i].Name; return ""; }

        /* Server fallback: the original CalcLine/RunCallout path, used only when
           the framework callout isn't loaded on the page. */
        function runCalloutServer(line, trigger, done) {
            var v = line.values;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/RunCallout",
                type: "GET", dataType: "json",
                data: {
                    M_Requisition_ID: parent.M_Requisition_ID, TriggerColumn: trigger,
                    M_Product_ID: v.M_Product_ID || 0, C_Charge_ID: v.C_Charge_ID || 0,
                    M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0,
                    QtyEntered: v.QtyEntered || 0, C_UOM_ID: v.C_UOM_ID || 0,
                    PriceActual: v.PriceActual || 0, PriceOverride: !!line._priceOverride,
                    Discount: v.Discount || 0
                },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res) applyPatch(line, res);
                    render(); if (done) done();
                },
                error: function (err) { console.log(err); render(); if (done) done(); }
            });
        }

        function applyPatch(line, res) {
            var v = line.values, d = line.display;
            var vals = res.Values || {}, disp = res.Display || {};
            for (var col in vals) {
                if (!vals.hasOwnProperty(col)) continue;
                if (col === "PriceActual" && line._priceOverride) continue;
                // Case-insensitive write: a saved line keys columns in DB case (PG lowercases,
                // e.g. "linenetamt") which won't match the dictionary-cased name the callout
                // returns, so a direct v[col]= would silently write a second key.
                setLineVal(line, col, vals[col]);
            }
            if (disp.uomName != null) d.uomName = disp.uomName;
            markDirty(line);
        }

        /* ---------- AD_Column validation ----------
         * Driven by the cached columnMeta. A column flagged IsMandatory in
         * AD_Column must hold a value before the line can be saved. Returns the
         * first violation (column + message) or null. */
        function isMandatory(col) { var m = columnMeta[col]; return !!(m && m.IsMandatory); }
        /* AD_Column.FieldLength for a column (0 when unknown) - caps text input length. */
        function colFieldLength(col) { var m = columnMeta[col]; return (m && +m.FieldLength) || 0; }

        /* ---------- AD_Column read-only logic ----------
         * A field is read-only when AD_Field.IsReadOnly is set or the column's
         * AD_Column.ReadOnlyLogic evaluates true for the line. The logic grammar
         * mirrors the framework Evaluator: comparison tuples "@token@<op>value"
         * (op = =, !, ^, <, >) joined by & (AND) / | (OR); @tokens@ resolve from the
         * line values first, then the requisition header / document context. */
        /* Columns the panel always shows read-only, whatever the dictionary says: each is
           stamped by the process that consumed or produced the line — the purchase-order
           line the requisition was converted into (RequisitionPOCreate), the reserved and
           delivered quantities maintained by the warehouse — so editing them here would
           break the link back to that document or falsify a stock figure. */
        var FORCED_READONLY_COLS = {
            C_OrderLine_ID: 1, QtyReserved: 1,
            DTD001_ReservedQty: 1, DTD001_DeliveredQty: 1
        };

        function isColumnReadOnly(line, col) {
            if (FORCED_READONLY_COLS[col]) return true;
            // C_UOM_ID: always read-only for charge lines (default UOM is auto-assigned).
            // For product lines: editable until saved, then locked.
            if (col === "C_UOM_ID" && line && line.values) {
                if ((line.values.C_Charge_ID || 0) > 0) return true;
                return (line.values.M_RequisitionLine_ID || 0) > 0;
            }
            // QtyEntered/Qty must remain editable whenever the panel is editable;
            // the DB ReadOnlyLogic (e.g. @Processed@=Y) is guarded at the header level
            // by panelEditable() in startEdit, so we skip column-level locking here.
            if (col === "QtyEntered" || col === "Qty") return false;
            var m = columnMeta[col];
            if (!m) return false;
            if (m.IsReadOnly) return true;
            return !!(m.ReadOnlyLogic && evalLogic(line, m.ReadOnlyLogic));
        }
        var FIELD_COL = { uom: "C_UOM_ID", quantity: "QtyEntered", price: "PriceActual", description: "Description" };
        function fieldReadOnly(line, field) {
            var col = FIELD_COL[field];
            return col ? isColumnReadOnly(line, col) : false;
        }
        /* Dynamic mandatory evaluation for Additional-Info modal fields. Mirrors VAS_074.
           VAS_240 has no conditional-mandatory columns today; extend this function if any
           are added (e.g. an asset-related column whose mandatory state depends on line values). */
        function dynMandatory(line, m) {
            return !!m.IsMandatory;
        }
        /* Read a login/global token from the live framework context (VIS.Env / VIS.context).
           The 1-arg getContext(name) reads the GLOBAL bag where $/# tokens (e.g. the
           accounting-element flags $Element_*) live. Returns "" when absent / unavailable. */
        function frameworkGlobalCtxVal(key) {
            try {
                if (!window.VIS) return "";
                var ctx = VIS.context || (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx());
                if (ctx && typeof ctx.getContext === "function") return ctx.getContext(String(key));
            } catch (e) { }
            return "";
        }
        /* Resolve a logic token. Login/session tokens (@$Element_OT@, @#Global@) come from
           the live framework context first, then the server-sent login context; everything
           else from the current line then the requisition header. A not-found login token
           resolves to "" (empty) - matching the framework evaluator - so the comparison
           evaluates (a ='Y' gate fails -> field hidden) instead of defaulting to show. */
        function logicCtxVal(line, key) {
            key = String(key);
            if (key.charAt(0) === "$" || key.charAt(0) === "#") {
                // Accounting-element flags ($Element_OT/PJ/...) and other login/global tokens
                // are set into the LIVE framework context (VIS.Env) at window load - the same
                // source the framework's own display-logic evaluator reads - so resolve from
                // there FIRST. The server session ctx does not reliably carry these, so the
                // server-sent parent.LoginContext is only a fallback.
                var fv = frameworkGlobalCtxVal(key);
                if (fv !== null && fv !== undefined && fv !== "") return String(fv);
                var lc = (parent && parent.LoginContext) || {};
                if (lc.hasOwnProperty(key)) return String(lc[key]);
                var bare = key.replace(/^[#$]/, "");
                if (lc.hasOwnProperty(bare)) return String(lc[bare]);
                // Not found -> empty string (NOT null), matching the framework evaluator: an
                // unresolved login token evaluates as "" so e.g. @$Element_MC@='Y' becomes
                // "" = 'Y' -> false -> the field is HIDDEN (treated as "N"), rather than the
                // tuple defaulting to SHOW. Login/global gates fail CLOSED when absent.
                return "";
            }
            var rv = lineVal(line, key);
            if (rv !== undefined && rv !== null && rv !== "") return String(rv);
            switch (key) {
                case "M_Requisition_ID": return String((parent && parent.M_Requisition_ID) || 0);
                case "M_PriceList_ID": return String((parent && parent.M_PriceList_ID) || 0);
                case "C_BPartner_ID": return String((parent && parent.C_BPartner_ID) || 0);
                case "M_Warehouse_ID": return String((parent && parent.M_Warehouse_ID) || 0);
                case "C_Currency_ID": return String((parent && parent.C_Currency_ID) || 0);
                case "AD_Org_ID": return String((parent && parent.AD_Org_ID) || 0);
                case "AD_Client_ID": return String((parent && parent.AD_Client_ID) || 0);
                // A requisition is always bought, never sold - a field whose logic gates on
                // the sales flag must read it as 'N' rather than as an unresolved token.
                case "IsSOTrx": return "N";
                case "Processed": return (parent && parent.Processed) ? "Y" : "N";
                case "DocStatus": return (parent && parent.DocStatus) || "";
                default: break;
            }
            // Header columns the requisition line's DisplayLogic names as tokens
            // (@DateRequired@, @PriorityRule@, a module's own header column). The server
            // omits a column that is NULL on the requisition, so an absent key resolves to
            // "" - which the evaluator reads as null for "@x@=null" and as non-numeric
            // (hence false) for "@x@>0", exactly as the dictionary intends.
            var lg = (parent && parent.LogicContext) || {};
            if (lg.hasOwnProperty(key)) return String(lg[key]);
            var lc = String(key).toLowerCase();
            for (var k in lg) if (lg.hasOwnProperty(k) && String(k).toLowerCase() === lc) return String(lg[k]);
            return "";
        }
        function evalLogic(line, logic, dflt) {
            return VAS.PanelUtil.evalLogic(
                function (key) { return logicCtxVal(line, key); },
                logic, dflt
            );
        }

        /* ---------- dynamic "more" fields (tab fields not on the panel grid) ----------
         * Renders the M_RequisitionLine tab fields that are NOT one of the fixed panel
         * columns as proper controls in the "..." popover, by AD_Reference_ID. Each
         * honours read-only logic (isColumnReadOnly), runs the column callout on change
         * when one is configured, enforces AD_Val_Rule (FK lookups, server-side) and is
         * covered by the mandatory check in validateLine. Values live in line.values and
         * persist through the existing generic column save. */
        /* Curated "Additional Info" columns shown in the "..." modal, in this order.
           To add a field, append its column name here (with an optional `when`
           condition). A column missing from the dictionary (e.g. a module's columns
           when the module isn't installed) is silently skipped. */
        var ADDITIONAL_INFO_FIELDS = [
            // --- Dimension group ---
            { col: "AD_OrgTrx_ID" },
            { col: "C_Project_ID" },
            // Project Phase / Project Line belong with Project (each is scoped by the
            // chosen project). Both are skipped silently where the dictionary has neither.
            { col: "C_ProjectPhase_ID" },
            { col: "C_ProjectLine_ID" },
            { col: "C_Campaign_ID" },
            { col: "C_Activity_ID" },
            // --- Requirement group: what is being asked for, and by when ---
            { col: "DateRequired" },
            { col: "PriorityRule" },
            { col: "M_Warehouse_ID" },
            { col: "M_Locator_ID" },
            // --- References group ---
            // The purchase-order line this requisition line was converted into, and the
            // quantities the warehouse maintains against it. All read-only: each is
            // stamped by the process that consumed the line, never entered by hand
            // (see FORCED_READONLY_COLS).
            { col: "C_OrderLine_ID" },
            { col: "QtyReserved" },
            { col: "DTD001_ReservedQty",  when: "dtd001_" },
            { col: "DTD001_DeliveredQty", when: "dtd001_" },
            // The work-order component this requisition was raised for (VAMFG module);
            // skipped silently wherever that module is not installed.
            { col: "VAMFG_M_WorkOrderComponent_ID", when: "vamfg_" }
        ];

        /* Document helpers for the Additional-Info `when` conditions. The panel is bound
           to one requisition, so these depend on the header (or the installed modules),
           not on the line. Unlike VAS_107 there is no document KIND to branch on: a
           requisition is always a requisition, and always purchase-side. */

        /* Whether a conditional group applies to this line. */
        function dynCondMet(line, when) {
            if (!when) return true;
            if (when === "svcExpenseOrCharge") {
                // groupCols asks about the DOCUMENT with no line in hand; a line-level
                // condition cannot be decided there, so answer "may apply" and let the
                // per-line pass in additionalInfoColumns settle it.
                if (!line || !line.values) return true;
                if (line.values.C_Charge_ID > 0) return true;          // charge line
                var pt = line._productType || "";
                return pt === "S" || pt === "E";                        // Service / Expense product
            }
            // Optional-module gates: the module is installed when its column reached
            // columnMeta (the server already filtered on Env.IsModuleInstalled).
            if (when === "vafam")   return !!columnMeta["VAFAM_IsAssetRelated"];
            if (when === "dtd001_") return !!columnMeta["DTD001_ReservedQty"];
            if (when === "vamfg_")  return !!columnMeta["VAMFG_M_WorkOrderComponent_ID"];
            return true;
        }

        /* ---------- collapsible field groups (Additional Info modal) ----------
           Reusable full-row section headers injected before an anchor field. A group owns
           the sibling fields between its header and the next header; the header's Show
           More/Less toggle collapses/expands them. Add an object here to add another group:
           `anchor` = ColumnName of the group's first field, `key`/`def` = AD_Message key +
           English fallback, `collapsed` = initial collapsed state. */
        var MORE_FIELD_GROUPS = [
            { anchor: "AD_OrgTrx_ID",    key: "VAS_240_GrpDimension",   def: "Dimension",   collapsed: false },
            { anchor: "DateRequired",    key: "VAS_240_GrpRequirement", def: "Requirement", collapsed: false },
            { anchor: "C_OrderLine_ID",  key: "VAS_240_GrpReferences",  def: "References",  collapsed: false }
        ];
        // Per-anchor collapsed state; persists across modal re-opens in the same session.
        var moreGroupCollapsed = {};

        /* Position of a curated column in ADDITIONAL_INFO_FIELDS (-1 when absent). */
        function dynSpecIndex(col) {
            for (var i = 0; i < ADDITIONAL_INFO_FIELDS.length; i++)
                if (ADDITIONAL_INFO_FIELDS[i].col === col) return i;
            return -1;
        }

        /* All ColumnNames owned by group gi (from its anchor to the next group's anchor).
           Specs that do not apply to THIS document are skipped, so a group only ever
           claims fields the modal actually built. That matters if a column is ever listed
           under two groups with mutually exclusive `when` conditions: without the filter
           the group it is NOT in would still claim it, and applyFieldGroups would anchor
           that group's header on a field rendered somewhere else entirely. */
        function groupCols(gi) {
            var start = dynSpecIndex(MORE_FIELD_GROUPS[gi].anchor);
            if (start < 0) return [];
            var end = ADDITIONAL_INFO_FIELDS.length;
            for (var g = gi + 1; g < MORE_FIELD_GROUPS.length; g++) {
                var idx = dynSpecIndex(MORE_FIELD_GROUPS[g].anchor);
                if (idx > start && idx < end) end = idx;
            }
            var out = [];
            for (var k = start; k < end; k++) {
                if (!dynCondMet(null, ADDITIONAL_INFO_FIELDS[k].when)) continue;
                out.push(ADDITIONAL_INFO_FIELDS[k].col);
            }
            return out;
        }

        /* Idempotently (re)insert every group header before the first field it owns that is
           actually present in the DOM, then apply the collapse state. */
        function applyFieldGroups($body) {
            if (!$body || !$body.length) return;
            $body.children(".vas-rbl-fldgrp").remove();
            for (var g = 0; g < MORE_FIELD_GROUPS.length; g++) {
                var grp = MORE_FIELD_GROUPS[g];
                var owned = groupCols(g), $anchor = null;
                for (var oc = 0; oc < owned.length; oc++) {
                    var $f = $body.children('[data-col="' + owned[oc] + '"]');
                    if ($f.length) { $anchor = $f; break; }
                }
                if (!$anchor) continue;
                var collapsed = (grp.anchor in moreGroupCollapsed) ? moreGroupCollapsed[grp.anchor] : !!grp.collapsed;
                $anchor.before(
                    '<div class="vas-rbl-fldgrp" data-grp="' + grp.anchor + '"' + (collapsed ? ' data-collapsed="1"' : "") + '>' +
                    '<span class="vas-rbl-fldgrp-name">' + esc(lbl(grp.key, grp.def)) + "</span>" +
                    '<button type="button" class="vas-rbl-fldgrp-toggle" data-act="fldgrp-toggle">' +
                    '<span class="vas-rbl-fldgrp-txt">' + esc(collapsed ? lbl("VAS_240_ShowMore", "Show More") : lbl("VAS_240_ShowLess", "Show Less")) + "</span>" +
                    '<svg class="vas-rbl-fldgrp-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 15 12 9 18 15"/></svg>' +
                    "</button></div>");
            }
            applyGroupCollapse($body);
            syncGroupHeaders($body);
        }

        /* Hide/show each group's member fields per the header's collapsed state. */
        function applyGroupCollapse($body) {
            $body.children(".vas-rbl-fldgrp").each(function () {
                var $hdr = $(this);
                $hdr.nextUntil(".vas-rbl-fldgrp").toggleClass("vas-rbl-grp-collapsed", $hdr.attr("data-collapsed") === "1");
            });
        }

        /* Hide a section header whose every owned field is hidden by DisplayLogic. */
        function syncGroupHeaders($body) {
            if (!$body || !$body.length) return;
            $body.children(".vas-rbl-fldgrp").each(function () {
                var $hdr = $(this);
                var live = $hdr.nextUntil(".vas-rbl-fldgrp", "[data-col]").not(".vas-rbl-dyn-hidden").length;
                $hdr.toggleClass("vas-rbl-fldgrp-empty", !live);
            });
        }

        /* Flip one group's collapsed state (triggered by its Show More/Less button). */
        function toggleFieldGroup($hdr) {
            if (!$hdr || !$hdr.length) return;
            var collapsed = $hdr.attr("data-collapsed") !== "1";
            $hdr.attr("data-collapsed", collapsed ? "1" : "0");
            moreGroupCollapsed[$hdr.attr("data-grp")] = collapsed;
            $hdr.find(".vas-rbl-fldgrp-txt").text(collapsed ? lbl("VAS_240_ShowMore", "Show More") : lbl("VAS_240_ShowLess", "Show Less"));
            applyGroupCollapse($hdr.closest(".vas-rbl-more-grid"));
        }

        /* Returns true when the module that owns columnName is installed.
           System-prefix columns (AD_, C_, M_, VAS_, VIS_, etc.) are always considered
           installed. Non-system prefixes (e.g. "VA106_") must appear in installedModulePrefixes,
           which is built from the server column list — columns whose module is not installed
           are stripped server-side by Env.IsModuleInstalled before reaching the client. */
        function isModuleInstalled(columnName) {
            if (!columnName) return true;
            var idx = columnName.indexOf("_");
            if (idx <= 0) return true;
            var prefix = columnName.substring(0, idx + 1);
            if (SYSTEM_PREFIXES[prefix.toUpperCase()]) return true;
            return !!installedModulePrefixes[prefix];
        }

        /* Curated CANDIDATE column metas for this line (skips missing columns, unmet
           `when` conditions, and columns whose module is not installed). NOTE:
           DisplayLogic is NOT applied here: the field is still BUILT, and its show/hide is
           decided AFTER buildDynField by applyDynDisplay (per the design - build the control
           first, then apply display logic), so a control keeps its state and just toggles
           visibility instead of being dropped/rebuilt. */
        /* Returns true when at least one additional-info column would be VISIBLE in the
           modal for this line after DisplayLogic is applied. Mirrors the exact check the
           modal does after building its fields. Used to decide whether the "..." button
           should be enabled — if no column is visible the modal would show "No additional
           info for this line", so the button is disabled.
           Pass a pre-computed cols array to avoid calling additionalInfoColumns twice. */
        function hasVisibleAdditionalFields(line, cols) {
            if (!cols) cols = additionalInfoColumns(line);
            for (var i = 0; i < cols.length; i++) {
                if (dynFieldVisible(line, cols[i])) return true;
            }
            return false;
        }

        /* Returns true when the line has at least one non-zero / non-empty value in any
           column that appears in the Additional Info modal (module-guarded + applicable).
           Used to visually highlight the "..." button so the user can see at a glance
           that the modal contains data without opening it.
           Uses dynFieldKind to classify each field — critical for YesNo columns whose
           value is "N" (No): +("N") === NaN, and NaN !== 0 is true, so the naive
           +val !== 0 check would wrongly highlight the button for an unset checkbox.
           Pass a pre-computed cols array to avoid calling additionalInfoColumns twice. */
        function hasAdditionalValues(line, cols) {
            if (!cols) cols = additionalInfoColumns(line);
            for (var i = 0; i < cols.length; i++) {
                var m = cols[i], v = lineVal(line, m.ColumnName);
                if (v == null) continue;
                var s = String(v).trim();
                if (!s) continue;
                switch (dynFieldKind(m)) {
                    case "fk": case "int": if (parseInt(s, 10) > 0) return true; break;
                    case "number": if (parseFloat(s) !== 0) return true; break;
                    case "yesno": if (s === "Y" || s === "true" || s === "1") return true; break;
                    default: return true;   // string / memo / date / list with any text
                }
            }
            return false;
        }

        function additionalInfoColumns(line) {
            var out = [];
            for (var i = 0; i < ADDITIONAL_INFO_FIELDS.length; i++) {
                var spec = ADDITIONAL_INFO_FIELDS[i];
                if (!isModuleInstalled(spec.col)) continue;
                // Exact dictionary case first, then a case-insensitive fallback - the curated
                // list above is hand-written and must not lose a field to a casing difference
                // (e.g. NoofCycle vs NoOfCycle) between deployments.
                var m = columnMeta[spec.col] || columnMeta[columnNameByLc[String(spec.col).toLowerCase()]];
                if (!m) continue;
                if (!dynCondMet(line, spec.when)) continue;
                out.push(m);
            }
            return out;
        }

        /* Whether a built field is visible per its AD_Field.DisplayLogic. Default SHOW when
           the logic is empty or a token can't be resolved.

           The dictionary is the ONLY authority here - there is no per-column override. A
           field whose logic names a HEADER column resolves it through logicCtxVal, which
           reads the LogicContext bag the model sends (DateRequired, M_Warehouse_ID, ...);
           an absent column resolves to "" and "@x@=null" is evaluated as an IS NULL test
           rather than a compare against the string "null" (PanelUtil), so the dictionary's
           own rules decide - which is what they were written to do. */
        function dynFieldVisible(line, m) {
            return !(m && m.DisplayLogic && !evalLogic(line, m.DisplayLogic, true));
        }

        /* Apply DisplayLogic to the ALREADY-BUILT modal fields: toggle each field's
           visibility (display:none via .vas-rbl-dyn-hidden) instead of adding/removing DOM,
           so a control - and its native lookup - is preserved across toggles. Returns the
           number of currently-visible fields (so the caller can show the empty message). */
        function applyDynDisplay(line, $body) {
            var visible = 0;
            $body.children("[data-col]").each(function () {
                var m = columnMeta[$(this).attr("data-col")];
                var show = dynFieldVisible(line, m);
                $(this).toggleClass("vas-rbl-dyn-hidden", !show);
                if (show) visible++;
            });
            syncGroupHeaders($body);   // hide empty group headers after toggling fields
            return visible;
        }

        /* AD_Reference_ID -> control kind. */
        function dynFieldKind(m) {
            switch (m.AD_Reference_ID) {
                case 10: return "string";
                case 14: case 36: return "memo";
                case 11: return "int";
                case 12: case 22: case 29: case 37: return "number";
                case 15: case 16: return "date";
                case 20: return "yesno";
                case 17: return (m.RefListValues && m.RefListValues.length) ? "list" : "string";
                case 18: case 19: case 30: return "fk";
                default: return "ro";   // unsupported reference -> read-only display
            }
        }

        function appendDynFields(line, $body) {
            var cols = additionalInfoColumns(line);
            // Build every candidate field FIRST (control + lookup created), then apply
            // DisplayLogic to show/hide - so display logic runs AFTER buildDynField.
            for (var i = 0; i < cols.length; i++) $body.append(buildDynField(line, cols[i]));
            applyDynDisplay(line, $body);
            applyFieldGroups($body);   // inject collapsible Dimension / References headers
            // No overflow clip on the body - an FK dropdown would be cut off.
        }

        function buildDynField(line, m) {
            var ro = isColumnReadOnly(line, m.ColumnName);
            var kind = dynFieldKind(m);
            // Caption only - the framework renders the mandatory red asterisk itself.
            var caption = m.Name || m.ColumnName;
            // Framework borderless field structure (createforecast.js).
            var $field = tryViennaControl(line, m, kind, ro, caption);
            if (!$field) {
                var $c = buildFallbackControl(line, m, kind, ro);
                if ($c && $c.attr) $c.attr("placeholder", " ");
                $field = $('<div class="input-group vis-input-wrap"></div>')
                    .append($('<div class="vis-control-wrap"></div>').append($c).append($('<label></label>').text(caption)));
            }
            // Prepend the AD_Field image using the framework's NATIVE icon-prepend structure
            // (input-group-prepend > input-group-text > i/img) INSIDE the .vis-input-wrap,
            // before .vis-control-wrap - so it sits in the same bordered flex row as the
            // framework's own field icons and is styled by the global .input-group-text CSS.
            // FIRST priority is the AD_Image FontName (icon-font class, e.g. "vis vis-xxx");
            // only when no font is set do we fall back to the bitmap thumbnail (ImageUrl).
            // data-col stays on $field (the direct child) so the modal reconcile logic is
            // unaffected. A broken image removes just its prepend box.
            // Always render a leading icon for alignment: AD_Image FontName first, then the
            // bitmap thumbnail, else the default "fa fa-file-text". A bitmap that fails to
            // load falls back to the default icon (keeps every field's left edge aligned).
            // EXCEPTION: YesNo (checkbox) fields - they carry their own vis-ec-col-lblchkbox
            // label and have no input-group/underline, so an icon prepend doesn't fit.
            if (kind !== "yesno") {
                var $prep = $('<div class="input-group-prepend"><span class="input-group-text"></span></div>');
                var $cell = $prep.find(".input-group-text");
                if (m.IconFont) {
                    $cell.append($('<i aria-hidden="true"></i>').attr("class", m.IconFont));
                } else if (m.ImageUrl) {
                    var base = (window.VIS && VIS.Application && VIS.Application.contextUrl) || "";
                    $cell.append($('<img alt="" />').attr("src", base + m.ImageUrl)
                        .on("error", function () { $cell.empty().append('<i aria-hidden="true" class="fa fa-file-text"></i>'); }));
                } else {
                    $cell.append('<i aria-hidden="true" class="fa fa-file-text"></i>');
                }
                $field.prepend($prep);
            }
            // Mirror VAS_074: mark mandatory fields so CSS can show the red asterisk indicator.
            $field.toggleClass("vas-rbl-dyn-mandatory", dynMandatory(line, m));
            return $field.attr("data-col", m.ColumnName);
        }

        /* Leading icon per control kind (Lucide names + a unicode fallback glyph). */
        function dynIcon(kind) {
            switch (kind) {
                case "date": return icon("calendar", "🗓");
                case "int": case "number": return icon("hash", "#");
                case "yesno": return icon("check", "✔");
                case "list": return icon("list", "≣");
                case "fk": return icon("link", "🔗");
                case "memo": return icon("align-left", "¶");
                default: return icon("type", "T");
            }
        }

        /* ---------- native Vienna controls (VIS.Controls.* via MLookupFactory) ----------
         * Mirrors the framework's getControl() (see AiFunctionItemMap.js): builds the real
         * dictionary control for a column by AD_Reference_ID - native combo / search /
         * date / amount / checkbox with proper lookups + formatting. Used for the modal
         * fields; falls back to the lightweight controls when VIS.Controls isn't present
         * or a type isn't handled. Toggle off with VAS_VIENNA_CTRL = false. */
        var VAS_VIENNA_CTRL = true;

        function viennaAvailable() {
            return !!(VAS_VIENNA_CTRL && window.VIS && VIS.Controls && VIS.DisplayType && VIS.MLookupFactory);
        }

        function isChangeKind(dt) {
            var DT = VIS.DisplayType;
            return dt == DT.YesNo || DT.IsDate(dt) || DT.IsLookup(dt) || dt == DT.ID;
        }

        /* MLookup cache. Building a column's lookup (VIS.MLookupFactory.getMLookUp) is the
           bulk of the "..." modal's open cost, and the SAME Additional-Info columns are
           rebuilt every time ANY line's modal opens. Cache the lookup per column+type so it
           is created once and reused on every later open (instant after the first). One modal
           is open at a time and the lookup is a shared read data source, so reuse is safe;
           the cache lives for the panel session and is dropped when the panel is disposed. */
        var _mlookupCache = {};
        var _LOOKUP_FAILED = {}; // sentinel: marks a column whose getMLookUp threw, so we never retry
        function getCachedMLookUp(vctx, windowNo, adColumnId, displayType) {
            var key = (adColumnId || 0) + "_" + displayType;
            if (_mlookupCache[key] === _LOOKUP_FAILED) return null; // previous attempt failed - don't retry
            if (!_mlookupCache[key]) {
                try {
                    _mlookupCache[key] = VIS.MLookupFactory.getMLookUp(vctx, windowNo, adColumnId, displayType);
                } catch (e) {
                    _mlookupCache[key] = _LOOKUP_FAILED; // prevent duplicate server calls on retry
                    throw e; // re-throw so tryViennaControl's catch logs it and returns null
                }
            }
            return _mlookupCache[key];
        }

        /* Prime the framework window context (VIS.context @ this panel's windowNo) with the
           requisition HEADER + the CURRENT line's column values, so a modal FK control's
           AD_Val_Rule resolves its @tokens@ against THIS line before the MLookup loads.
           Writes directly via ctx.setContext() so that VIS.MLookupFactory.getMLookUp()
           finds a properly initialised _vInfo map.

           Duplicate-key safety: line.values contains ALL M_RequisitionLine columns returned by
           GetLineProjectionColumns(). On PostgreSQL these arrive lowercase in the DataTable,
           and fromServerRow() only re-cases columns that are in columnNameByLc (Additional
           Info columns). All OTHER columns stay lowercase. If those lowercase names are
           written into VIS.context while the Requisition window's framework context already
           holds the Pascal-cased version, VIS.context ends up with both "c_campaign_id" and
           "C_Campaign_ID" for the same column. JSON.stringify emits both; ASP.NET MVC's
           case-insensitive JsonValueProviderFactory throws "same key already added".

           Solution: the stage() helper preserves the canonical name already stored in the
           bag (from the Pascal-cased header write) so a later lowercase line-value write
           never changes the name already resolved. Additionally, the line-values loop only
           stages a column when its canonical name is known via columnNameByLc OR it was
           already placed in the bag by the header staging — skipping truly unknown columns
           that have no val-rule tokens and would only introduce casing conflicts. */
        function primeLineContext(line) {
            if (!window.VIS) return;
            var ctx = VIS.context || (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx());
            if (!ctx || typeof ctx.setContext !== "function") return;
            var wn = $self.windowNo || 0;
            var bag = {};
            function coerce(val) {
                if (val === true || val === false) return val ? "Y" : "N";
                if (val instanceof Date) return dateStr(val);
                if (val == null) return "";
                return String(val);
            }
            function stage(col, val) {
                if (col == null) return;
                var lc = String(col).toLowerCase();
                // (1) metadata canonical name, (2) already-staged canonical name from the
                // header write, (3) raw col as last resort — same priority as VAS_074.
                // Priority (2) ensures a Pascal-cased header write is never overridden by a
                // subsequent lowercase DB column name that would create a case-insensitive
                // duplicate in the serialised context sent to GetLookup.
                var name = columnNameByLc[lc] || (bag[lc] && bag[lc].name) || col;
                bag[lc] = { name: name, value: coerce(val) };
            }
            if (parent) {
                stage("M_Requisition_ID",  parent.M_Requisition_ID  || 0);
                stage("C_BPartner_ID",     parent.C_BPartner_ID     || 0);
                stage("M_PriceList_ID",    parent.M_PriceList_ID    || 0);
                stage("C_Currency_ID",     parent.C_Currency_ID     || 0);
                stage("AD_Org_ID",         parent.AD_Org_ID         || 0);
                stage("AD_Client_ID",      parent.AD_Client_ID      || 0);
                stage("M_Warehouse_ID",    parent.M_Warehouse_ID    || 0);
                stage("DateDoc",           parent.DateDoc);
                // A requisition is always purchase-side; a val rule gating on the sales
                // flag must see 'N' rather than an unset token.
                stage("IsSOTrx",           false);
                stage("Processed",         !!parent.Processed);
                stage("DocStatus",         parent.DocStatus         || "");
                // Every other header column the model sends as a logic token
                // (DateRequired, PriorityRule, a module's own column).
                var lg = parent.LogicContext || {};
                for (var lk in lg) if (lg.hasOwnProperty(lk)) stage(lk, lg[lk]);
            }
            // Stage line columns ONLY when the canonical name is known (columnNameByLc covers
            // all Additional-Info columns whose val-rules need @token@ resolution) OR when the
            // header already placed a proper-cased entry in the bag. Unknown extra columns
            // returned by GetLineProjectionColumns() are skipped to avoid casing collisions
            // with the Requisition window context the framework has already populated.
            var v = line.values || {};
            for (var k in v) {
                if (!v.hasOwnProperty(k)) continue;
                var klc = String(k).toLowerCase();
                if (columnNameByLc[klc] || bag[klc]) stage(k, v[k]);
            }
            // Re-assert the header org so @AD_Org_ID@ always resolves to the requisition's org.
            if (parent && (parent.AD_Org_ID || 0) > 0) stage("AD_Org_ID", parent.AD_Org_ID);
            // Write each column exactly once.
            for (var key in bag) {
                if (!bag.hasOwnProperty(key)) continue;
                try { ctx.setContext(wn, bag[key].name, bag[key].value); } catch (e) { }
            }
            // Login / accounting-element tokens (@#Global@ / @$Element_*@) as #global context.
            if (parent) {
                var glc = parent.LoginContext || {};
                for (var gk in glc) {
                    if (!glc.hasOwnProperty(gk)) continue;
                    try { ctx.setContext(String(gk), String(glc[gk])); } catch (e) { }
                }
            }
        }

        /* Build the framework control for a column (or null when unsupported). */
        function makeViennaCtrl(displayType, columnName, header, refValueId, mandatory, readOnly, adColumnId) {
            var DT = VIS.DisplayType, C = VIS.Controls, upd = !readOnly;
            // The checkbox renders its OWN label (vis-ec-col-lblchkbox) with the caption
            // text inside it - so pass the caption in (no separate floating label).
            if (displayType == DT.YesNo) return new C.VCheckBox(columnName, mandatory, readOnly, upd, header || "", null, true);
            if (DT.IsDate(displayType)) { var vd = new C.VDate(columnName, mandatory, readOnly, upd, displayType, header); vd.setName(columnName); return vd; }
            if (DT.IsLookup(displayType) || DT.ID == displayType) {
                var vctx = VIS.context || (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx());
                // Cannot build a lookup control without a valid framework context.
                // Returning null causes tryViennaControl to fall back to buildFallbackControl.
                if (!vctx) return null;
                var lookup = getCachedMLookUp(vctx, ($self.windowNo || 0), (adColumnId || 0), displayType);
                if (displayType != DT.Search && displayType != DT.MultiKey) {
                    return new C.VComboBox(columnName, mandatory, readOnly, upd, lookup, 50);
                }
                return new C.VTextBoxButton(columnName, mandatory, readOnly, upd, DT.Search, lookup);   // button appended by caller
            }
            if (DT.Text == displayType || DT.TextLong == displayType) return new C.VTextArea(columnName, mandatory, readOnly, upd, 50, 100, displayType);
            if (DT.IsNumeric(displayType)) {
                if (DT.Integer == displayType) return new C.VNumTextBox(columnName, mandatory, readOnly, upd, 50, 100, "FixedHeader");
                return new C.VAmountTextBox(columnName, mandatory, readOnly, upd, 50, 100, displayType, "FixedHeader");
            }
            if (displayType == DT.String) return new C.VTextBox(columnName, mandatory, readOnly, upd, 50, 100, null, null, false);
            return null;   // unsupported -> caller falls back
        }

        /* Coerce a stored line value -> what the control's setValue expects. */
        function viennaSetVal(dt, v) {
            var DT = VIS.DisplayType;
            if (v === undefined || v === null || v === "") return null;
            if (dt == DT.YesNo) return (v === true || v === "Y" || v === "true" || v === 1 || v === "1");
            if (DT.IsLookup(dt) || dt == DT.ID || DT.IsNumeric(dt)) { var n = +v; return isNaN(n) ? v : n; }
            return v;
        }
        /* Coerce a control's newValue -> what we store in line.values / save. */
        function viennaNewVal(dt, nv) {
            var DT = VIS.DisplayType;
            if (dt == DT.YesNo) return (nv === true || nv === "Y" || nv === "true") ? "Y" : "N";
            if (DT.IsDate(dt)) return nv ? dateStr(nv) : "";
            return nv;
        }

        /* Returns the native control wrapped in the framework field structure
           (createforecast.js): .vis-control-wrap holds the control + <label>; a Search
           control adds .input-group-append for the lookup button. No custom classes.
           Returns null to fall back. */
        function tryViennaControl(line, m, kind, ro, caption) {
            if (!viennaAvailable() || kind === "ro") return null;
            var col = m.ColumnName, dt = m.AD_Reference_ID, ctrl;
            try {
                ctrl = makeViennaCtrl(dt, col, m.Name || col, m.AD_Reference_Value_ID, dynMandatory(line, m), ro, m.AD_Column_ID);
                ctrl.getControl().css("width", "100%");
            }
            catch (e) { if (window.console) console.log("VAS_240 vienna ctrl error " + col, e); return null; }
            if (!ctrl || typeof ctrl.getControl !== "function") return null;
            try {
                var iv = viennaSetVal(dt, lineVal(line, col));
                if (iv !== null)
                    ctrl.setValue(iv);
            } catch (e) { }
            ctrl.fireValueChanged = function (ev) {
                setDyn(line, col, viennaNewVal(dt, ev ? ev.newValue : null), isChangeKind(dt));
            };
            try {
                // YesNo: the VCheckBox supplies its own <label class="vis-ec-col-lblchkbox">
                // with the caption inside it - no floating label / vis-input-wrap, and no
                // separate caption <label> (that would duplicate the caption).
                if (dt == VIS.DisplayType.YesNo) {
                    var $cb = $(ctrl.getControl()).attr("placeholder", " ").attr("data-placeholder", "");
                    return $('<div class="vis-control-wrap"></div>').append($cb);
                }
                var $c = $(ctrl.getControl()).attr("placeholder", " ").attr("data-placeholder", "");
                var $cw = $('<div class="vis-control-wrap"></div>').append($c).append($('<label></label>').text(caption || ""));
                // Search reference (AD_Reference_ID == 30) renders a VTextBoxButton whose
                // getBtn(0) is the info/zoom button that opens the framework Info window
                // (the control wires the click internally, same as createforecast.js). Wrap
                // it in .input-group-append and flag the control data-hasbtn so vis-input-wrap
                // lays out the button. Only for Search - TableDir/Table use VComboBox's own
                // dropdown and don't need the extra button.
                if (dt == VIS.DisplayType.Search && typeof ctrl.getBtn === "function") {
                    var btn0 = null;
                    try { btn0 = ctrl.getBtn(0); } catch (eb) { btn0 = null; }
                    var $bw = btn0 ? $('<div class="input-group-append"></div>').append(btn0) : null;
                    if ($bw && $bw.children().length) {
                        $c.attr("data-hasbtn", " ");
                        return $('<div class="input-group vis-input-wrap"></div>').append($cw).append($bw);
                    }
                }
                // Non-lookup control: same framework container so vis-input-wrap styles it.
                return $('<div class="input-group vis-input-wrap"></div>').append($cw);
            } catch (e) { if (window.console) console.log("VAS_240 vienna wrap error " + col, e); return null; }
        }

        function lineVal(line, col) {
            return VAS.PanelUtil.lineVal(line.values, col);
        }
        function setLineVal(line, col, value) {
            VAS.PanelUtil.setLineVal(line.values, col, value);
        }

        /* True when the line's product actually carries an attribute set (M_AttributeSet_ID > 0).
           Reads the raw server flag VASOLDISP_HasAttrSet (= COALESCE(p.M_AttributeSet_ID, 0))
           off the line's value bag, case-insensitively via lineVal - a saved line's keys are
           DB-cased (PostgreSQL lowercases the alias to "vascildisp_hasattrset", Oracle uppercases
           it). This is authoritative: unlike line.display.hasAttributeSet it is NOT OR'd with an
           existing ASI description, so a line whose product no longer has an attribute set (but
           still carries an old ASI, so AttrName is set) correctly reports false. Falls back to the
           display flag when the raw column isn't on the line - e.g. a brand-new (unsaved) line,
           which has no server projection but carries the pure product flag in display. */
        function productHasAttributeSet(line) {
            var raw = lineVal(line, "VASOLDISP_HasAttrSet");
            if (raw === undefined || raw === null || raw === "") return !!(line.display && line.display.hasAttributeSet);
            return (parseInt(raw, 10) || 0) > 0;
        }

        /* Lightweight fallback control (plain element, no custom class - the framework
           .vis-control-wrap styles it) for when the native Vienna control is unavailable. */
        function buildFallbackControl(line, m, kind, ro) {
            var col = m.ColumnName, v = lineVal(line, col);
            if (ro || kind === "ro") {
                var disp = (kind === "fk") ? "" : dynDisplay(line, m, v);
                var $d = $('<input type="text" readonly tabindex="-1" />').val(disp);
                if (kind === "fk") ensureRefLabel(line, m, $d);
                return $d;
            }
            if (kind === "memo") {
                var $t = $('<textarea rows="2"></textarea>').val(v == null ? "" : String(v));
                $t.on("blur", function () { setDyn(line, col, $t.val()); });
                return $t;
            }
            if (kind === "string") {
                var $s = $('<input type="text" />').val(v == null ? "" : String(v));
                if (m.FieldLength > 0) $s.attr("maxlength", m.FieldLength);
                $s.on("blur", function () { setDyn(line, col, $s.val()); });
                return $s;
            }
            if (kind === "int" || kind === "number") {
                var $n = $('<input type="text" inputmode="decimal" />').val(v == null || v === "" ? "" : String(v));
                $n.on("blur", function () { setDyn(line, col, kind === "int" ? (parseInt($n.val(), 10) || 0) : parseNum($n.val())); });
                return $n;
            }
            if (kind === "date") {
                var $dt = $('<input type="date" />').val(dateStr(v));
                $dt.on("change", function () { setDyn(line, col, $dt.val(), true); });
                return $dt;
            }
            if (kind === "yesno") {
                var on = (v === true || v === "Y" || v === "true" || v === 1 || v === "1");
                var $c = $('<input type="checkbox" />').prop("checked", on);
                $c.on("change", function () { setDyn(line, col, $c.prop("checked") ? "Y" : "N", true); });
                return $c;
            }
            if (kind === "list") {
                var $l = $('<select></select>');
                $l.append($('<option value=""></option>').text(lbl("VAS_240_SelectOption", "Select")));
                for (var i = 0; i < m.RefListValues.length; i++) {
                    var rv = m.RefListValues[i];
                    var $o = $("<option></option>").attr("value", rv.Value).text(rv.Name || rv.Value);
                    if (String(v) === String(rv.Value)) $o.prop("selected", true);
                    $l.append($o);
                }
                $l.on("change", function () { setDyn(line, col, $l.val(), true); });
                return $l;
            }
            // fk (Table / TableDir / Search) - searchable dropdown via GetRefLookup.
            return buildFkControl(line, m);
        }

        /* Stored-value display for a non-FK field (List resolves from its values). */
        function dynDisplay(line, m, v) {
            if (v === undefined) v = lineVal(line, m.ColumnName);
            if (v == null || v === "") return "";
            if (m.AD_Reference_ID === 17) {
                for (var i = 0; i < (m.RefListValues || []).length; i++)
                    if (String(m.RefListValues[i].Value) === String(v)) return m.RefListValues[i].Name || m.RefListValues[i].Value;
            }
            if (line._dynDisp && line._dynDisp[m.ColumnName] != null) return line._dynDisp[m.ColumnName];
            return String(v);
        }
        function setDynDisplay(line, col, name) { if (!line._dynDisp) line._dynDisp = {}; line._dynDisp[col] = name; }

        /* Set a dynamic field value + mark dirty, then re-evaluate the modal (display
           logic / read-only / values refresh). When the column carries an AD_Column.Callout,
           hand off to runCallout - which executes the real CLIENT callout when its class is
           loaded on the page and otherwise falls back to the server RunColumnCallout - so a
           modal field's callout fires even when its client class isn't present on the page. */
        function setDyn(line, col, value, refresh) {
            var prev = lineVal(line, col);
            setLineVal(line, col, value);
            // Keep the window context current so a dependent FK's val rule (and any control
            // built by a following refreshMoreDialog) resolves against the new value.
            primeLineContext(line);
            if (!sameVal(prev, value)) {
                markDirty(line);
                // Track which modal fields the user intentionally changed (including intentional
                // clears to null/0) so ApplyExtraColumns on the server can distinguish a
                // deliberate null from a column the user never touched. Without this, a new line
                // created from a template loses un-touched columns that would have been kept.
                if (!line._dynTouched) line._dynTouched = {};
                line._dynTouched[col] = true;
            }
            var m = columnMeta[col];
            if (m && m.Callout) {
                // Snapshot the other modal fields so we can refresh just the ones the
                // callout changed (e.g. VA106_TCSAmount after picking the TCS type).
                var before = snapshotDynValues(line);
                runCallout(line, col, function () {
                    refreshIfAffectsLogic(line, col);
                    syncMoreDialogValues(line, before, col);
                    renderTotals();   // a callout (e.g. VA106 TCS) may change an amount in the grand total
                });
                return;
            }
            if (refresh) refreshIfAffectsLogic(line, col);
        }

        /* Clear an Additional Info FK field to null (e.g. user presses the × button on a
           lookup control). Mirrors setDyn but always writes null and removes the display name.
           Like setDyn, records the column in _dynTouched so the server knows the null was
           intentional and does not re-apply the column's default value on save. */
        function clearDynValue(line, col) {
            if (!columnMeta[col]) return;
            var prev = lineVal(line, col);
            setLineVal(line, col, null);
            if (line._dynDisp) delete line._dynDisp[col];
            if (!sameVal(prev, null)) {
                markDirty(line);
                if (!line._dynTouched) line._dynTouched = {};
                line._dynTouched[col] = true;
            }
        }

        /* Rebuild a single Additional-Info modal field in-place without closing/reopening
           the modal. Mirrors VAS_074.rebuildDynField. Called after a callout updates a field
           value that may change the control's mandatory state or lookup filter. */
        function rebuildDynField(line, col) {
            var $b = $("#vasRblMoreBody");
            if (!$b.length || morePopoverFor !== line.rowId) return;
            var m = columnMeta[col];
            if (!m) return;
            var $old = $b.children('[data-col="' + col + '"]');
            if (!$old.length) return;
            $old.replaceWith(buildDynField(line, m));
            applyDynDisplay(line, $b);
        }

        /* Snapshot the current value of every curated modal field (case-insensitive). */
        function snapshotDynValues(line) {
            var snap = {}, cols = additionalInfoColumns(line);
            for (var i = 0; i < cols.length; i++) snap[cols[i].ColumnName] = lineVal(line, cols[i].ColumnName);
            return snap;
        }

        /* After a callout, rebuild the displayed control of any OTHER curated modal field
           whose line value the callout changed (the trigger field keeps its own DOM / focus).
           Only changed fields are rebuilt, so a number field like VA106_TCSAmount refreshes
           without re-creating every lookup in the modal. */
        function syncMoreDialogValues(line, before, triggerCol) {
            var $b = $("#vasRblMoreBody");
            if (!$b.length || morePopoverFor !== line.rowId) return;
            var cols = additionalInfoColumns(line);
            for (var i = 0; i < cols.length; i++) {
                var c = cols[i].ColumnName;
                if (c === triggerCol) continue;
                var was = before.hasOwnProperty(c) ? before[c] : undefined, now = lineVal(line, c);
                if (String(was == null ? "" : was) === String(now == null ? "" : now)) continue;
                var $old = $b.children('[data-col="' + c + '"]');
                if ($old.length) $old.replaceWith(buildDynField(line, cols[i]));
            }
            // A rebuilt field starts without the hidden class - re-apply DisplayLogic.
            applyDynDisplay(line, $b);
        }

        /* Rebuild the modal only when ANOTHER curated field's DisplayLogic / ReadOnlyLogic
           references the column that just changed - otherwise leave the controls (and the
           one in focus) untouched, so selecting a value doesn't rebuild every field. */
        function refreshIfAffectsLogic(line, col) {
            var tok = "@" + col + "@";
            for (var i = 0; i < ADDITIONAL_INFO_FIELDS.length; i++) {
                var mm = columnMeta[ADDITIONAL_INFO_FIELDS[i].col];
                if (!mm || mm.ColumnName === col) continue;
                if ((mm.DisplayLogic && mm.DisplayLogic.indexOf(tok) >= 0) ||
                    (mm.ReadOnlyLogic && mm.ReadOnlyLogic.indexOf(tok) >= 0)) {
                    refreshMoreDialog(line);
                    return;
                }
            }
        }

        /* Rebuild the open modal's fields in place (after a value change) so display
           logic, read-only state and callout-updated values reflect immediately. */
        /* Reconcile the modal fields in place: keep existing field DOM (and its native
           controls / lookups), build only candidates not yet present, drop only `when`-
           excluded ones, then re-apply DisplayLogic via applyDynDisplay (show/hide toggle).
           So toggling a field (e.g. Asset Related) doesn't rebuild the whole modal /
           re-create every lookup - which was the slow part. */
        function refreshMoreDialog(line) {
            var $b = $("#vasRblMoreBody");
            if (!$b.length || morePopoverFor !== line.rowId) return;
            primeLineContext(line);   // newly-visible FK controls validate against this line
            var cols = additionalInfoColumns(line);
            var existing = {};
            $b.children("[data-col]").each(function () { existing[$(this).attr("data-col")] = this; });
            var seen = {}, prev = null;
            for (var i = 0; i < cols.length; i++) {
                var c = cols[i].ColumnName; seen[c] = true;
                var node = existing[c] || buildDynField(line, cols[i]).get(0);
                if (prev) $(node).insertAfter(prev); else $b.prepend(node);
                prev = node;
            }
            // Only `when`-excluded fields (module / product-type) are removed from the DOM;
            // DisplayLogic-hidden fields stay built and are toggled by applyDynDisplay so a
            // control (and its lookup) is preserved when it re-appears.
            $b.children("[data-col]").each(function () { if (!seen[$(this).attr("data-col")]) $(this).remove(); });
            var visible = applyDynDisplay(line, $b);
            $b.children(".vas-rbl-empty-message").remove();
            if (!visible)
                $b.append('<p class="vas-rbl-empty-message">' + esc(lbl("VAS_240_NoAdditionalInfo", "No additional info for this line")) + "</p>");
        }

        /* Resolve and cache an FK value's display label (existing value caption). */
        function ensureRefLabel(line, m, $input) {
            var col = m.ColumnName, v = lineVal(line, col);
            if (!v || +v <= 0) return;
            if (line._dynDisp && line._dynDisp[col] != null) { $input.val(line._dynDisp[col]); return; }
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/GetRefLookup",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ M_Requisition_ID: parent.M_Requisition_ID, ColumnName: col, Id: +v }) },
                success: function (raw) {
                    var rows = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw; rows = rows || [];
                    if (rows.length) { setDynDisplay(line, col, rows[0].Name); if ($input && $input.closest("body").length) $input.val(rows[0].Name); }
                },
                error: function () {}
            });
        }

        function buildFkControl(line, m) {
            var col = m.ColumnName;
            var $wrap = $('<div class="vas-rbl-fk" style="position:relative"></div>');
            var $i = $('<input type="text" class="vas-rbl-field__input" autocomplete="off" />').val(dynDisplay(line, m));
            $wrap.append($i);
            ensureRefLabel(line, m, $i);
            var fk = { seq: 0, deb: null, $pop: null };
            $i.on("input", function () {
                var term = $i.val();
                if (fk.deb) clearTimeout(fk.deb);
                fk.deb = setTimeout(function () { fkSearch(line, m, $wrap, $i, term, fk); }, SEARCH_DEBOUNCE);
            });
            $i.on("focus", function () { if (!($i.val() || "").trim()) fkSearch(line, m, $wrap, $i, "", fk); });
            $i.on("blur", function (e) {
                if (e.relatedTarget && $(e.relatedTarget).attr("data-fk-item") === "true") return;
                setTimeout(function () { if (fk.$pop) { fk.$pop.remove(); fk.$pop = null; } }, 150);
            });
            return $wrap;
        }

        function fkSearch(line, m, $wrap, $i, term, fk) {
            fk.seq++; var mySeq = fk.seq;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/GetRefLookup",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ M_Requisition_ID: parent.M_Requisition_ID, ColumnName: m.ColumnName, Query: term, PageSize: CATALOG_PAGE_SIZE, Offset: 0, RowValues: compactCtx(line.values) }) },
                success: function (raw) {
                    if (mySeq !== fk.seq) return;
                    var rows = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw; rows = rows || [];
                    showFkPopover($wrap, $i, line, m, rows, fk);
                },
                error: function (err) { console.log(err); }
            });
        }

        function showFkPopover($wrap, $i, line, m, rows, fk) {
            if (fk.$pop) fk.$pop.remove();
            fk.$pop = $('<div class="vas-rbl-catalog-popover vas-rbl-fk-popover"></div>');
            if (!rows.length) {
                fk.$pop.html('<div class="vas-rbl-catalog__hint">' + esc(lbl("VAS_240_NoMatches", "No matches")) + "</div>");
            } else {
                for (var i = 0; i < rows.length; i++) {
                    fk.$pop.append($('<button type="button" class="vas-rbl-catalog-popover__item" data-fk-item="true"></button>')
                        .text(rows[i].Name).attr("data-id", rows[i].Id).attr("data-name", rows[i].Name));
                }
            }
            fk.$pop.on("mousedown", ".vas-rbl-catalog-popover__item", function (e) {
                e.preventDefault();
                var id = parseInt($(this).attr("data-id"), 10) || 0, name = $(this).attr("data-name") || "";
                setDynDisplay(line, m.ColumnName, name);
                $i.val(name);
                if (fk.$pop) { fk.$pop.remove(); fk.$pop = null; }
                setDyn(line, m.ColumnName, id, true);
            });
            $wrap.append(fk.$pop);
        }

        function validateLine(line) {
            var v = line.values;
            if (v.M_Product_ID <= 0 && v.C_Charge_ID <= 0)
                return { col: "M_Product_ID", msg: lbl("VAS_240_ProductChargeRequired", "Select a product or charge") };

            var err = VAS.PanelUtil.validateFields(v, [
                { col: "QtyEntered",   msg: lbl("VAS_240_FieldRequired", "Required") + ": QtyEntered",   check: function (vv) { return isMandatory("QtyEntered")   && !(vv.QtyEntered > 0); } },
                { col: "C_UOM_ID",     msg: lbl("VAS_240_FieldRequired", "Required") + ": C_UOM_ID",     check: function (vv) { return isMandatory("C_UOM_ID")     && !(vv.C_UOM_ID > 0); } },
                { col: "PriceActual", msg: lbl("VAS_240_FieldRequired", "Required") + ": PriceActual", check: function (vv) { return isMandatory("PriceActual") && (vv.PriceActual == null || vv.PriceActual === ""); } },
                { col: "Description",  msg: lbl("VAS_240_FieldRequired", "Required") + ": Description",  check: function (vv) { return isMandatory("Description")  && !(vv.Description && String(vv.Description).trim()); } }
            ]);
            if (err) return err;

            if (!(v.QtyEntered > 0))
                return { col: "QtyEntered", msg: lbl("VAS_240_QtyRequired", "Quantity must be greater than zero") };

            var dyn = additionalInfoColumns(line);
            var dynRules = [];
            for (var d = 0; d < dyn.length; d++) {
                (function (dm) {
                    if (!dm.IsMandatory) return;
                    dynRules.push({
                        col: dm.ColumnName,
                        msg: lbl("VAS_240_FieldRequired", "Required") + ": " + (dm.Name || dm.ColumnName),
                        check: function (vv) {
                            if (!dynFieldVisible(line, dm)) return false;
                            if (isColumnReadOnly(line, dm.ColumnName)) return false;
                            var dv   = VAS.PanelUtil.lineVal(vv, dm.ColumnName);
                            var isFk = (dm.AD_Reference_ID === 18 || dm.AD_Reference_ID === 19 || dm.AD_Reference_ID === 30 || dm.AD_Reference_ID === 13);
                            return isFk ? !(+dv > 0) : (dv == null || String(dv).trim() === "");
                        }
                    });
                })(dyn[d]);
            }
            return VAS.PanelUtil.validateFields(v, dynRules);
        }

        /* Validate the supplied batch (or all unsaved lines when batch is omitted) and record
           each error ON THE LINE (line._error) so the row renders its own red message instead
           of a single toast that only names the first failure. Repaints so every offending
           record in a multi-row save is flagged in place; returns false when any line is
           invalid. The batch parameter lets saveRows validate only the rows being submitted,
           leaving any other unsaved lines (added while a save was in flight) untouched. */
        function validateUnsaved(batch) {
            var dirty = batch || unsavedLines();
            var anyBad = false;
            for (var i = 0; i < dirty.length; i++) {
                var err = validateLine(dirty[i]);
                dirty[i]._error = err ? err.msg : "";   // clear on now-valid rows too
                if (err) anyBad = true;
            }
            render();   // paint each row's inline error (and clear the ones that passed)
            if (anyBad) {
                // Bring the first failing row into view so it isn't missed off-screen.
                var $first = $linesBody.find(".vas-rbl-row--line.is-invalid").first();
                if ($first.length && $first[0].scrollIntoView) $first[0].scrollIntoView({ block: "nearest" });
            }
            return !anyBad;
        }

        /* ---------- attribute picker dialog ---------- */
        // Attribute picker: now delegated to the shared, reusable VIS.AttributeControl
        // (Scripts/app/util/AttributeControl.js). We pass this panel's helpers so the look
        // and behaviour are unchanged, and apply the chosen instance to the line in onApply.
        // (The control handles the pre-select + unchanged-OK no-op internally.)
        function openAttrDialog(line) {
            if (!line.values.M_Product_ID) return;
            // The product must actually carry an attribute set (M_AttributeSet_ID > 0) for
            // the attribute control to be meaningful. A saved line whose product has no
            // attribute set defined must not open the control even if it's clicked - guard
            // here as well as at the link's click binding so no caller can bypass it. Uses the
            // raw VASOLDISP_HasAttrSet flag off the line (case-insensitive), not the AttrName-
            // conflated display flag - see productHasAttributeSet.
            if (!productHasAttributeSet(line)) return;
            closeDialogs();
            VIS.AttributeControl.open({
                M_Product_ID: line.values.M_Product_ID,
                M_AttributeSetInstance_ID: line.values.M_AttributeSetInstance_ID,
                productName: line.display.productName,
                // A requisition is a purchase-side document (IsSOTrx = false): the
                // instance is normally being created rather than picked out of stock,
                // so open the new-attribute form directly — the same choice the panel
                // makes for a purchase order.
                IsSOTrx: false,
                newAttribute: true,
                // Default to hiding zero / negative qty instances; user can toggle via checkbox.
                showAll: false,
                lbl: lbl, esc: esc, icon: icon,
                showBusy: showBusy, showToast: showToast,
                dateStr: dateStr, fmtMoney: fmtMoney, parseNum: parseNum,
                onApply: function (res) {
                    var asi = (res && res.M_AttributeSetInstance_ID) || 0;
                    line.values.M_AttributeSetInstance_ID = asi;
                    line.display.attrName = (res && res.description) || "";
                    markDirty(line);
                    editing = { rowId: line.rowId, field: "description" };
                    if (asi > 0) runCallout(line, "M_AttributeSetInstance_ID");
                    else render();
                },
                // Picker dismissed without selecting -> don't leave focus stranded;
                // land on the description field (same as a product with no attribute set).
                onClose: function () {
                    editing = { rowId: line.rowId, field: "description" };
                    render();
                }
            });
        }
        function blankAttr() { return { code: "", label: "", spec: "", priceDelta: "", availability: "", M_Attribute_ID: 0, M_AttributeValue_ID: 0 }; }
        function flattenAttrs(info) {
            var out = [];
            if (info && info.Attributes) {
                for (var a = 0; a < info.Attributes.length; a++) {
                    var attr = info.Attributes[a];
                    if (attr.Values) for (var i = 0; i < attr.Values.length; i++) {
                        var val = attr.Values[i];
                        out.push({ code: val.Code || val.Name, label: val.Name, spec: attr.Name, priceDelta: "—", availability: "—",
                            M_Attribute_ID: attr.M_Attribute_ID, M_AttributeValue_ID: val.M_AttributeValue_ID });
                    }
                }
            }
            return out;
        }

        // Stable selection identity for a list row - unique PER ROW (rowKey), so two
        // rows of the same ASI (different locators) don't select together.
        function optionKey(o) { return (o && (o.rowKey || o.key || o.code)) || ""; }

        // First row index whose instance matches the given M_AttributeSetInstance_ID
        // (one ASI may span several locator rows; the first is enough to highlight/page).
        function indexOfAsi(options, asi) {
            for (var i = 0; i < options.length; i++) {
                if ((parseInt(options[i].M_AttributeSetInstance_ID, 10) || 0) === asi) return i;
            }
            return -1;
        }

        /* Loads the product's existing M_AttributeSetInstance rows through the framework
           PAttributes/GetAttributeData endpoint - the SAME data the framework "Select
           Existing" grid (pattributeinstance.js) shows. IMPORTANT: that endpoint builds
           its own SELECT ... FROM (storage / locator / shelf-life joins) and appends
           `WHERE <the string we send>`, binding @M_Product_ID - so we must pass a WHERE
           clause (+ ORDER BY), NOT a full SELECT. The clause mirrors the framework's
           msqlWhere. Returns [] when the framework data context is unavailable. */
        function loadExistingInstances(line) {
            var pid = parseInt(line.values.M_Product_ID, 10) || 0;
            if (pid <= 0) return [];
            try {
                if (!(window.VIS && VIS.secureEngine && VIS.dataContext && typeof VIS.dataContext.getJSONData === "function")) return [];
                var where = "patr.M_Product_ID=@M_Product_ID AND patr.M_AttributeSetInstance_ID != 0 ORDER BY asi.GuaranteeDate, QtyOnHand DESC";
                var rows = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "PAttributes/GetAttributeData",
                    { Sq1Atribute: VIS.secureEngine.encrypt(where), Product_ID: pid }, null);
                return flattenInstances(rows);
            } catch (e) { console.log(e); return []; }
        }
        function flattenInstances(rows) {
            var out = [];
            if (rows && rows.length) {
                for (var i = 0; i < rows.length; i++) {
                    var r = rows[i];
                    var qoh = +r.QtyOnHand || 0;
                    out.push({
                        // Per-ROW identity: GetAttributeData joins M_Storage, so one ASI can
                        // appear on several rows (one per locator) - keying selection by ASI
                        // would light up every row of that ASI. rowKey is unique per row.
                        rowKey: "r" + i,
                        key: "ASI:" + r.M_AttributeSetInstance_ID,
                        code: r.Lot || r.SerNo || ("#" + r.M_AttributeSetInstance_ID),
                        label: r.Description || "",
                        spec: r.GuaranteeDate ? dateStr(r.GuaranteeDate) : "",   // guarantee / expiration
                        locator: r.Value || "",                                  // l.Value (locator)
                        availability: fmtMoney(qoh),                             // QtyOnHand
                        qtyOnHand: qoh,
                        M_AttributeSetInstance_ID: r.M_AttributeSetInstance_ID,
                        M_Locator_ID: +r.M_Locator_ID || 0,
                        lot: r.Lot || "", serno: r.SerNo || "", guaranteeDate: r.GuaranteeDate ? dateStr(r.GuaranteeDate) : ""
                    });
                }
            }
            return out;
        }

        function buildAttrDialog() {
            var backdrop = $('<div class="vas-rbl-dialog-backdrop" id="vasRblAttr"></div>');
            var dialog = $('<div class="vas-rbl-dialog vas-rbl-dialog--wide"></div>');
            dialog.html(
                '<header class="vas-rbl-dialog__header">' +
                '<div class="vas-rbl-dialog__header-row"><h3 class="vas-rbl-dialog__title" id="vasRblAttrTitle">' + esc(lbl("VAS_240_SelectAttribute", "Select attribute")) + "</h3>" +
                '<button type="button" class="vas-rbl-btn vas-rbl-btn--outline-pill vas-rbl-is-hidden" data-act="attr-back">' + icon("arrow-left", "←") + "<span>" + esc(lbl("VAS_240_Back", "Back")) + "</span></button>" +
                (attrState.info && attrState.info.IsCanCreate ?
                    '<button type="button" class="vas-rbl-btn vas-rbl-btn--outline-pill" data-act="attr-create">' + icon("plus", "+") + "<span>" + esc(lbl("VAS_240_NewAttribute", "New attribute")) + "</span></button>" : "") + "</div>" +
                '<div class="vas-rbl-dialog__type"><span class="vas-rbl-badge vas-rbl-badge--product">' + esc(lbl("VAS_240_Product", "Product")) + '</span><p class="vas-rbl-dialog__primary-name">' + esc(attrState.product) + "</p></div>" +
                '<div class="vas-rbl-search-input" id="vasRblAttrSearchRow">' + icon("search", "🔍") + '<input type="text" id="vasRblAttrSearch" placeholder="' + esc(lbl("VAS_240_AttrSearch", "Search attribute values")) + '" /></div>' +
                "</header>" +
                '<div class="vas-rbl-dialog__body vas-rbl-dialog__body--fixed">' +
                '<div id="vasRblAttrList"' + (attrState.info && attrState.info.IsCanEdit ? ' class="vas-rbl-attr-grid--editable"' : "") + '><div class="vas-rbl-attr-grid__head"><div></div><div>' + esc(lbl("VAS_240_Code", "Code")) + "</div><div>" + esc(lbl("Description", "Description")) +
                "</div><div>" + esc(lbl("GuaranteeDate", "Guarantee Date")) + "</div><div>" + esc(lbl("M_Locator_ID", "Locator")) + '</div><div class="vas-rbl-attr-h-right">' + esc(lbl("QtyOnHand", "On Hand")) + "</div>" +
                (attrState.info && attrState.info.IsCanEdit ? "<div>" + esc(lbl("VAS_240_Edit", "Edit")) + "</div>" : "") +
                '</div><div class="vas-rbl-attr-grid__body" id="vasRblAttrRows"></div></div>' +
                '<div id="vasRblAttrCreate" class="vas-rbl-is-hidden">' + attrCreateForm() + "</div>" +
                "</div>" +
                '<footer class="vas-rbl-dialog__footer vas-rbl-dialog__footer--end">' +
                '<div id="vasRblAttrListFoot">' +
                '<div class="vas-rbl-attr-pager"><button type="button" class="vas-rbl-attr-pagebtn" data-act="attr-prev" aria-label="' + esc(lbl("VAS_240_Prev", "Previous")) + '">' + icon("chevron-left", "‹") + '</button>' +
                '<span class="vas-rbl-attr-pageinfo" id="vasRblAttrPageInfo"></span>' +
                '<button type="button" class="vas-rbl-attr-pagebtn" data-act="attr-next" aria-label="' + esc(lbl("VAS_240_Next", "Next")) + '">' + icon("chevron-right", "›") + "</button></div>" +
                '<button type="button" class="vas-rbl-btn vas-rbl-btn--primary" data-act="attr-ok">' + esc(lbl("VAS_240_OK", "OK")) + "</button></div>" +
                '<div id="vasRblAttrCreateFoot" class="vas-rbl-is-hidden"><button type="button" class="vas-rbl-btn vas-rbl-btn--ghost" data-act="attr-cancel">' + esc(lbl("VAS_240_Cancel", "Cancel")) +
                '</button><button type="button" class="vas-rbl-btn vas-rbl-btn--primary" data-act="attr-submit" disabled>' + esc(lbl("VAS_240_AddAttribute", "Add attribute")) + "</button></div></footer>");
            backdrop.append(dialog);
            $("body").append(backdrop);

            // Do NOT close on outside/backdrop click - a stray click (incl. on a framework
            // lookup popup) must not dismiss the picker; it closes only via OK / Cancel.
            dialog.on("click", "[data-act=attr-create]", function () { openCreateForm(null); });
            dialog.on("click", "[data-act=attr-back],[data-act=attr-cancel]", function () { attrState.mode = "list"; attrState.editAsi = null; attrState.error = ""; renderAttr(); });
            dialog.on("click", "[data-act=attr-ok]", commitAttribute);
            dialog.on("click", "[data-act=attr-submit]", submitNewAttribute);
            dialog.on("click", "[data-act=attr-edit]", function (e) {
                e.stopPropagation();
                var key = $(this).attr("data-key");
                var o = attrState.options.filter(function (x) { return optionKey(x) === key; })[0];
                if (o) editExistingInstance(o);
            });
            dialog.on("click", "[data-act=attr-newlot]", createNewLot);
            dialog.on("click", "[data-act=attr-genserno]", generateSerNo);
            dialog.on("click", "[data-act=attr-prev]", function () { if (attrState.page > 0) { attrState.page--; renderAttrRows(); } });
            dialog.on("click", "[data-act=attr-next]", function () { attrState.page++; renderAttrRows(); });
            dialog.find("#vasRblAttrSearch").on("input", function () { attrState.search = $(this).val(); attrState.page = 0; renderAttrRows(); });
            renderAttr();
            setTimeout(function () { dialog.find("#vasRblAttrSearch").focus(); }, 0);
        }

        /* Dynamic create-mode form, generated from the product's M_AttributeSet
           (mirrors the framework PAttributesForm.LoadInit markup): one control per
           INSTANCE attribute - List -> <select>, Number -> numeric, Text -> text -
           plus Lot / Serial No / Guarantee Date when the set captures them. Every
           control is wrapped in the framework borderless field structure
           (`input-group vis-input-wrap` -> `vis-control-wrap` + floating <label>), so
           it matches both the PAttributesForm controls and design.md's Form Field spec
           (label above value, bottom-border-only, primary-blue utility actions). */
        function attrCreateForm() {
            var info = attrState.info || { Attributes: [] };
            var html = '<div class="vas-rbl-attr-form vas-rbl-more-grid">';
            var any = false;
            var attrs = (info.Attributes || []).filter(function (a) { return a.IsInstanceAttribute; });
            for (var i = 0; i < attrs.length; i++) {
                var a = attrs[i];
                any = true;
                var id = "vasRblAttrF_" + a.M_Attribute_ID;
                var ctrl;
                if (a.ValueType === "L") {
                    ctrl = '<select id="' + id + '" placeholder=" " data-placeholder=""><option value=""> </option>';
                    for (var v = 0; v < (a.Values || []).length; v++) {
                        var val = a.Values[v];
                        ctrl += '<option value="' + val.M_AttributeValue_ID + '">' + esc((val.Code ? val.Code + " - " : "") + val.Name) + "</option>";
                    }
                    ctrl += "</select>";
                } else if (a.ValueType === "N") {
                    ctrl = '<input type="text" inputmode="decimal" id="' + id + '" placeholder=" " data-placeholder="" />';
                } else {
                    ctrl = '<input type="text" id="' + id + '" placeholder=" " data-placeholder="" />';
                }
                html += attrField(ctrl, a.Name, null, a.IsMandatory);
            }
            if (info.IsLot) { any = true; html += attrField(attrTextInput("vasRblAttrLot", true), lbl("VAS_240_Lot", "Lot"), attrFieldBtn("attr-newlot", lbl("VAS_240_NewLot", "New")), false); }
            if (info.IsSerNo) { any = true; html += attrField(attrTextInput("vasRblAttrSerNo", true), lbl("VAS_240_SerialNo", "Serial No"), attrFieldBtn("attr-genserno", lbl("VAS_240_Generate", "Generate")), false); }
            if (info.IsGuaranteeDate) { any = true; html += attrField('<input type="date" id="vasRblAttrGuarantee" placeholder=" " data-placeholder="" />', lbl("VAS_240_GuaranteeDate", "Guarantee Date"), null, false); }
            html += "</div>";
            if (!any) html = '<p class="vas-rbl-empty-message">' + esc(lbl("VAS_240_NoInstanceAttr", "No instance attributes to capture")) + "</p>";
            return html + '<p class="vas-rbl-form-error vas-rbl-is-hidden" id="vasRblAttrCreateError"></p>';
        }

        /* Wrap a control (HTML string) in the framework borderless floating-label field
           structure. `btnHtml` (optional) adds a trailing utility action via
           .input-group-append (the control then carries data-hasbtn so vis-input-wrap
           reserves room for it). The framework CSS renders the floating <label>; the
           mandatory asterisk is added manually for these non-dictionary controls. */
        function attrField(ctrlHtml, caption, btnHtml, mandatory) {
            var cap = esc(caption) + (mandatory ? ' <em class="vas-rbl-req">*</em>' : "");
            var inner = '<div class="vis-control-wrap">' + ctrlHtml + "<label>" + cap + "</label></div>";
            if (btnHtml) return '<div class="input-group vis-input-wrap">' + inner + '<div class="input-group-append">' + btnHtml + "</div></div>";
            return '<div class="input-group vis-input-wrap">' + inner + "</div>";
        }
        function attrTextInput(id, hasBtn) {
            return '<input type="text" id="' + id + '" placeholder=" " data-placeholder=""' + (hasBtn ? ' data-hasbtn=" "' : "") + " />";
        }
        // Trailing utility action button (Lot + New, Serial No + Generate) - primary-blue
        // text action per design.md's Right Utility Icons / action-link guidance.
        function attrFieldBtn(act, label) {
            return '<button type="button" class="vas-rbl-attr-fieldbtn" data-act="' + act + '">' + esc(label) + "</button>";
        }

        /* Create a new lot for the product via the framework PAttributes/CreateLot
           endpoint and drop the generated lot number into the Lot field. */
        function createNewLot() {
            var line = attrState.line;
            var pid = parseInt(line.values.M_Product_ID, 10) || 0;
            var asi = parseInt(line.values.M_AttributeSetInstance_ID, 10) || 0;
            if (pid <= 0) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "PAttributes/CreateLot",
                type: "GET", dataType: "json",
                data: { mAttributeSetInstanceId: asi, mProductId: pid },
                success: function (res) {
                    showBusy(false);
                    var r = (res && typeof res.result !== "undefined") ? res.result : res;
                    if (r && r.Name != null) { $("#vasRblAttrLot").val(r.Name); attrState.newLotId = r.Key; }
                    else showToast(lbl("VAS_240_LotFailed", "Could not create lot"));
                },
                error: function (e) { console.log(e); showBusy(false); showToast(lbl("VAS_240_LotFailed", "Could not create lot")); }
            });
        }
        /* Generate the next serial number for the product via the framework
           PAttributes/GetSerNo endpoint and drop it into the Serial No field. */
        function generateSerNo() {
            var line = attrState.line;
            var pid = parseInt(line.values.M_Product_ID, 10) || 0;
            var asi = parseInt(line.values.M_AttributeSetInstance_ID, 10) || 0;
            if (pid <= 0) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "PAttributes/GetSerNo",
                type: "GET", dataType: "json",
                data: { mAttributeSetInstanceId: asi, mProductId: pid },
                success: function (res) {
                    showBusy(false);
                    var r = (res && typeof res.result !== "undefined") ? res.result : res;
                    if (r != null && String(r).length) $("#vasRblAttrSerNo").val(r);
                    else showToast(lbl("VAS_240_SerNoFailed", "Could not generate serial number"));
                },
                error: function (e) { console.log(e); showBusy(false); showToast(lbl("VAS_240_SerNoFailed", "Could not generate serial number")); }
            });
        }

        function renderAttr() {
            var d = $("#vasRblAttr");
            var isCreate = attrState.mode === "create";
            d.find("#vasRblAttrSearchRow").toggleClass("vas-rbl-is-hidden", isCreate);
            d.find("#vasRblAttrTitle").toggleClass("vas-rbl-is-hidden", isCreate);
            d.find("[data-act=attr-back]").toggleClass("vas-rbl-is-hidden", !isCreate);
            d.find("[data-act=attr-create]").toggleClass("vas-rbl-is-hidden", isCreate);
            d.find("#vasRblAttrList").toggleClass("vas-rbl-is-hidden", isCreate);
            d.find("#vasRblAttrCreate").toggleClass("vas-rbl-is-hidden", !isCreate);
            d.find("#vasRblAttrListFoot").toggleClass("vas-rbl-is-hidden", isCreate);
            d.find("#vasRblAttrCreateFoot").toggleClass("vas-rbl-is-hidden", !isCreate);
            if (isCreate) {
                // Submit button reflects edit (update existing) vs add (new) mode.
                d.find("[data-act=attr-submit]").text(attrState.editAsi ? lbl("VAS_240_UpdateAttribute", "Update attribute") : lbl("VAS_240_AddAttribute", "Add attribute"));
                updateAttrError(); updateAttrSubmit();
                setTimeout(function () { d.find("#vasRblAttrCreate").find("input, select").first().focus(); }, 0);
            }
            else renderAttrRows();
        }

        var ATTR_PAGE_SIZE = 20;
        function renderAttrRows() {
            var body = $("#vasRblAttrRows"); body.empty();
            var canEdit = !!(attrState.info && attrState.info.IsCanEdit);
            var q = (attrState.search || "").trim().toLowerCase();
            var matched = attrState.options.filter(function (o) {
                if (!q) return true;
                return (o.code + " " + o.label + " " + o.spec + " " + (o.locator || "")).toLowerCase().indexOf(q) !== -1;
            });
            // Clamp the page to the available range, then take this page's 20 rows.
            var pageCount = Math.max(1, Math.ceil(matched.length / ATTR_PAGE_SIZE));
            if (attrState.page > pageCount - 1) attrState.page = pageCount - 1;
            if (attrState.page < 0) attrState.page = 0;
            var start = attrState.page * ATTR_PAGE_SIZE;
            var visible = matched.slice(start, start + ATTR_PAGE_SIZE);
            renderAttrPager(matched.length, pageCount, start, visible.length);
            if (!matched.length) { body.append('<p class="vas-rbl-empty-message">' + esc(lbl("VAS_240_NoMatches", "No matches")) + "</p>"); return; }
            // Row is a div (not a <button>) so the per-row Edit button can nest legally.
            visible.forEach(function (o) {
                var $row = $('<div class="vas-rbl-attr-grid__row" role="button" tabindex="0"></div>');
                var key = optionKey(o);
                if (key === attrState.selected) $row.addClass("is-selected");
                var editCell = (canEdit && o.M_AttributeSetInstance_ID > 0) ?
                    '<span class="vas-rbl-attr-edit"><button type="button" class="vas-rbl-attr-editbtn" data-act="attr-edit" data-key="' + esc(key) +
                    '" title="' + esc(lbl("VAS_240_Edit", "Edit")) + '">' + icon("pencil", "✎") + "</button></span>" : (canEdit ? "<span></span>" : "");
                $row.html('<span class="vas-rbl-attr-radio">' + (key === attrState.selected ? '<span class="vas-rbl-attr-radio__dot"></span>' : "") + "</span>" +
                    '<span class="vas-rbl-attr-code">' + esc(o.code) + '</span><span class="vas-rbl-attr-label">' + esc(o.label) + "</span>" +
                    '<span class="vas-rbl-attr-spec">' + esc(o.spec) + '</span><span class="vas-rbl-attr-delta">' + esc(o.locator || "—") + "</span>" +
                    '<span class="vas-rbl-attr-avail">' + esc(o.availability) + "</span>" + editCell);
                // Ignore clicks on the Edit button - the row's re-render would detach it
                // before the delegated attr-edit handler runs, so let that handler take it.
                $row.on("click", function (e) { if ($(e.target).closest("[data-act=attr-edit]").length) return; attrState.selected = key; renderAttrRows(); });
                $row.on("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); attrState.selected = key; renderAttrRows(); } });
                body.append($row);
            });
        }

        /* Update the list pager: "start-end of total" + prev/next enabled state. */
        function renderAttrPager(total, pageCount, start, shown) {
            var info = $("#vasRblAttrPageInfo");
            if (!info.length) return;
            if (!total) { info.text(lbl("VAS_240_NoRecords", "No records")); }
            else { info.text((start + 1) + "-" + (start + shown) + " " + lbl("VAS_240_Of", "of") + " " + total); }
            $("#vasRblAttr [data-act=attr-prev]").prop("disabled", attrState.page <= 0);
            $("#vasRblAttr [data-act=attr-next]").prop("disabled", attrState.page >= pageCount - 1);
        }

        /* Switch the dialog into create/edit mode. Rebuilds the create form fresh (so a
           prior edit's prefilled values don't linger) and, when an existing instance is
           passed, autofills it for editing. editAsi != null -> the submit UPDATES that
           M_AttributeSetInstance in place; null -> creates a new one. */
        function openCreateForm(editAsi) {
            attrState.editAsi = editAsi || null;
            attrState.mode = "create";
            attrState.error = "";
            $("#vasRblAttrCreate").html(attrCreateForm());   // fresh, empty controls
            renderAttr();
            if (editAsi) prefillCreateForm(editAsi);
        }

        /* Edit an existing instance in the panel's OWN create form: open it, autofill the
           selected record's values (set-level lot/serno/guarantee from the row, plus the
           per-attribute values fetched from GetInstanceValues), and let the user update.
           Shown only when the role allows editing (info.IsCanEdit). */
        function editExistingInstance(o) {
            if (!o || o.M_AttributeSetInstance_ID <= 0) return;
            openCreateForm(o);
        }

        /* Autofill the (already-rendered) create form from an existing instance. */
        function prefillCreateForm(o) {
            if (o.lot) $("#vasRblAttrLot").val(o.lot);
            if (o.serno) $("#vasRblAttrSerNo").val(o.serno);
            if (o.guaranteeDate) $("#vasRblAttrGuarantee").val(o.guaranteeDate);
            var asi = parseInt(o.M_AttributeSetInstance_ID, 10) || 0;
            if (asi <= 0) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/GetInstanceValues",
                type: "GET", dataType: "json", data: { M_AttributeSetInstance_ID: asi },
                success: function (raw) {
                    showBusy(false);
                    if (attrState.mode !== "create" || !attrState.editAsi || attrState.editAsi.M_AttributeSetInstance_ID !== asi) return;  // moved on
                    var vals = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!vals || !vals.length) return;
                    for (var i = 0; i < vals.length; i++) {
                        var v = vals[i], $el = $("#vasRblAttrF_" + v.M_Attribute_ID);
                        if (!$el.length) continue;
                        if (v.ValueType === "L") $el.val(v.M_AttributeValue_ID > 0 ? String(v.M_AttributeValue_ID) : "");
                        else if (v.ValueType === "N") $el.val(v.NumberValue != null ? String(v.NumberValue) : "");
                        else $el.val(v.StringValue || "");
                    }
                },
                error: function (e) { console.log(e); showBusy(false); }
            });
        }

        function updateAttrError() {
            var el = $("#vasRblAttrCreateError");
            el.text(attrState.error).toggleClass("vas-rbl-is-hidden", !attrState.error);
        }
        // The dynamic form validates on submit, so the button stays enabled.
        function updateAttrSubmit() { $("#vasRblAttr [data-act=attr-submit]").prop("disabled", false); }
        function attrErr(name) { attrState.error = lbl("VAS_240_FieldRequired", "Required") + ": " + name; updateAttrError(); }

        function commitAttribute() {
            var sel = attrState.options.filter(function (o) { return optionKey(o) === attrState.selected; })[0];
            var line = attrState.line;
            if (!sel) { closeDialogs(); return; }
            // Unchanged selection: the dialog pre-selects the line's current instance, so
            // OK'ing without picking a different one must NOT re-dirty the line or re-run
            // the callout (that would reset the attribute / price). Just close.
            var curAsi = parseInt(line.values.M_AttributeSetInstance_ID, 10) || 0;
            if (curAsi > 0 && (parseInt(sel.M_AttributeSetInstance_ID, 10) || 0) === curAsi) {
                closeDialogs();
                return;
            }
            // Reuse an existing M_AttributeSetInstance loaded via GetAttributeData:
            // bind it straight to the line and re-run the callout - no save needed.
            if (sel.M_AttributeSetInstance_ID > 0) {
                line.values.M_AttributeSetInstance_ID = sel.M_AttributeSetInstance_ID;
                line.display.attrName = sel.label || sel.code; markDirty(line);
                closeDialogs(); editing = { rowId: line.rowId, field: "description" };
                runCallout(line, "M_AttributeSetInstance_ID");
                return;
            }
            // A client-created option has no real value id — store its code as the
            // display attribute but it cannot persist as an M_AttributeSetInstance.
            if (!sel.M_AttributeValue_ID) {
                line.display.attrName = sel.code; markDirty(line);
                closeDialogs(); editing = { rowId: line.rowId, field: "description" }; render();
                showToast(lbl("VAS_240_AttrNotPersisted", "New attribute is display-only until it exists in the product attribute set"));
                return;
            }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/SaveAttribute",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ M_Product_ID: line.values.M_Product_ID, Lot: "", SerNo: "", GuaranteeDate: "",
                    Values: [{ M_Attribute_ID: sel.M_Attribute_ID, ValueType: "L", M_AttributeValue_ID: sel.M_AttributeValue_ID, DisplayValue: sel.label }] }) },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.M_AttributeSetInstance_ID > 0) {
                        line.values.M_AttributeSetInstance_ID = res.M_AttributeSetInstance_ID;
                        line.display.attrName = sel.code; markDirty(line);
                        closeDialogs(); editing = { rowId: line.rowId, field: "description" };
                        runCallout(line, "M_AttributeSetInstance_ID");
                    } else { showToast(lbl("VAS_240_AttrSaveFailed", "Could not save attribute")); }
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_240_AttrSaveFailed", "Could not save attribute")); }
            });
        }

        /* Collect the dynamic controls, build a real M_AttributeSetInstance through
           SaveAttribute (lot / serial / guarantee + typed attribute values), then
           set it on the line and re-run the callout. */
        function submitNewAttribute() {
            var info = attrState.info || { Attributes: [] };
            var line = attrState.line;
            attrState.error = "";
            var values = [], labelParts = [];
            var attrs = (info.Attributes || []).filter(function (a) { return a.IsInstanceAttribute; });
            for (var i = 0; i < attrs.length; i++) {
                var a = attrs[i];
                var $el = $("#vasRblAttrF_" + a.M_Attribute_ID);
                var raw = $el.val();
                var empty = (raw == null || String(raw).trim() === "");
                if (a.ValueType === "L") {
                    var vid = parseInt(raw, 10) || 0;
                    if (!vid) { if (a.IsMandatory) { attrErr(a.Name); return; } continue; }
                    var txt = $el.find("option:selected").text();
                    values.push({ M_Attribute_ID: a.M_Attribute_ID, ValueType: "L", M_AttributeValue_ID: vid, DisplayValue: txt });
                    labelParts.push(txt);
                } else if (a.ValueType === "N") {
                    if (empty) { if (a.IsMandatory) { attrErr(a.Name); return; } continue; }
                    var num = parseNum(raw);
                    values.push({ M_Attribute_ID: a.M_Attribute_ID, ValueType: "N", NumberValue: num, DisplayValue: String(num) });
                    labelParts.push(a.Name + ": " + num);
                } else {
                    if (empty) { if (a.IsMandatory) { attrErr(a.Name); return; } continue; }
                    values.push({ M_Attribute_ID: a.M_Attribute_ID, ValueType: "S", StringValue: String(raw), DisplayValue: String(raw) });
                    labelParts.push(String(raw));
                }
            }
            var lot = info.IsLot ? ($("#vasRblAttrLot").val() || "") : "";
            var serno = info.IsSerNo ? ($("#vasRblAttrSerNo").val() || "") : "";
            var guarantee = info.IsGuaranteeDate ? ($("#vasRblAttrGuarantee").val() || "") : "";
            if (lot) labelParts.push(lbl("VAS_240_Lot", "Lot") + ": " + lot);
            if (serno) labelParts.push(lbl("VAS_240_SerialNo", "Serial No") + ": " + serno);
            if (!values.length && !lot && !serno && !guarantee) {
                attrState.error = lbl("VAS_240_NothingEntered", "Enter at least one attribute value"); updateAttrError(); return;
            }

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/SaveAttribute",
                type: "POST", dataType: "json",
                // editAsi set -> UPDATE that instance in place; else create a new one.
                data: { payload: JSON.stringify({ M_Product_ID: line.values.M_Product_ID,
                    M_AttributeSetInstance_ID: (attrState.editAsi && attrState.editAsi.M_AttributeSetInstance_ID) || 0,
                    Lot: lot, SerNo: serno, GuaranteeDate: guarantee, Values: values }) },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.M_AttributeSetInstance_ID > 0) {
                        line.values.M_AttributeSetInstance_ID = res.M_AttributeSetInstance_ID;
                        line.display.attrName = res.Description || labelParts.join(", "); markDirty(line);
                        closeDialogs(); editing = { rowId: line.rowId, field: "description" };
                        runCallout(line, "M_AttributeSetInstance_ID");
                    } else { showToast(lbl("VAS_240_AttrSaveFailed", "Could not save attribute")); }
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_240_AttrSaveFailed", "Could not save attribute")); }
            });
        }

        /* ---------- scan dialog ---------- */
        function openScanDialog() {
            closeDialogs();
            scanState = { rows: [], input: "", error: "" };
            var backdrop = $('<div class="vas-rbl-dialog-backdrop" id="vasRblScan"></div>');
            var dialog = $('<div class="vas-rbl-dialog vas-rbl-dialog--wide"></div>');
            dialog.html(
                '<header class="vas-rbl-dialog__header">' +
                '<div class="vas-rbl-dialog__header-row"><div class="vas-rbl-dialog__title-block">' + icon("scan-line", "▭") + '<h3 class="vas-rbl-dialog__title">' + esc(lbl("VAS_240_QuickAddBarcode", "Quick add by barcode")) + "</h3></div>" +
                '<p class="vas-rbl-dialog__hint">' + esc(lbl("VAS_240_ScanToReviewSubmit", "Scan to add · review · submit")) + "</p></div>" +
                '<p class="vas-rbl-dialog__sub">' + esc(lbl("VAS_240_ScanHint", "Scan a product barcode; items collect below. Duplicate scans increase quantity.")) + "</p>" +
                '<div class="vas-rbl-scan-input"><button type="button" class="vas-rbl-scan-input__icon" data-act="sim-scan" title="' + esc(lbl("VAS_240_SimulateScan", "Simulate scan")) + '">' + icon("scan-line", "▭") + "</button>" +
                '<input type="text" id="vasRblScanInput" placeholder="' + esc(lbl("VAS_240_ListeningScans", "Listening for scans… (type a code + Enter)")) + '" autocomplete="off" /></div>' +
                '<p class="vas-rbl-dialog__error vas-rbl-is-hidden" id="vasRblScanError"></p></header>' +
                '<div class="vas-rbl-dialog__body vas-rbl-dialog__body--fixed">' +
                '<div class="vas-rbl-scan-empty" id="vasRblScanEmpty"><div class="vas-rbl-scan-empty__badge">' + icon("scan-line", "▭") + "</div>" +
                '<p class="vas-rbl-scan-empty__title">' + esc(lbl("VAS_240_ScanToBegin", "Scan a barcode to begin")) + '</p><p class="vas-rbl-scan-empty__hint">e.g. PRD-BLW-001 · CHG-INS-001</p></div>' +
                '<div class="vas-rbl-scan-grid vas-rbl-is-hidden" id="vasRblScanGrid"><div class="vas-rbl-scan-grid__head"><div>' + esc(lbl("VAS_240_Code", "Code")) + "</div><div>" + esc(lbl("VAS_240_ProductCharge", "Product / Charge")) +
                "</div><div>" + esc(lbl("VAS_240_Status", "Status")) + "</div><div>" + esc(lbl("VAS_240_Qty", "Qty")) + '</div><div></div></div><div class="vas-rbl-scan-grid__body" id="vasRblScanRows"></div></div></div>' +
                '<footer class="vas-rbl-dialog__footer"><p class="vas-rbl-dialog__summary" id="vasRblScanSummary">' + esc(lbl("VAS_240_NoScans", "No scans yet")) + "</p>" +
                '<div class="vas-rbl-dialog__actions"><button type="button" class="vas-rbl-btn vas-rbl-btn--ghost" data-act="close-scan">' + esc(lbl("VAS_240_Cancel", "Cancel")) +
                '</button><button type="button" class="vas-rbl-btn vas-rbl-btn--primary" data-act="submit-scan" disabled>' + esc(lbl("VAS_240_AddLines", "Add lines")) + "</button></div></footer>");
            backdrop.append(dialog);
            $("body").append(backdrop);

            backdrop.on("mousedown", function (e) { if (e.target === backdrop[0]) closeDialogs(); });
            var $field = dialog.find("#vasRblScanInput");
            $field.on("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); handleScan($field.val()); $field.val(""); } });
            dialog.on("click", "[data-act=sim-scan]", function () { $field.focus(); });
            dialog.on("click", "[data-act=close-scan]", closeDialogs);
            dialog.on("click", "[data-act=submit-scan]", submitScan);
            dialog.on("click", ".vas-rbl-scan-del", function () { var id = $(this).attr("data-id"); scanState.rows = scanState.rows.filter(function (r) { return r.id !== id; }); renderScanRows(); });
            dialog.on("click", "[data-qminus]", function () { stepScan($(this).attr("data-qminus"), -1); });
            dialog.on("click", "[data-qplus]", function () { stepScan($(this).attr("data-qplus"), 1); });
            dialog.on("input", "[data-qinput]", function () { var r = scanRow($(this).attr("data-qinput")); if (r) r.qty = ($(this).val().replace(/[^0-9]/g, "") || "1"); });
            setTimeout(function () { $field.focus(); }, 0);
        }
        function scanRow(id) { return scanState.rows.filter(function (r) { return r.id === id; })[0]; }
        function stepScan(id, dir) { var r = scanRow(id); if (!r) return; r.qty = String(Math.max(1, (parseInt(r.qty || "1", 10) || 1) + dir)); renderScanRows(); }

        function handleScan(code) {
            code = (code || "").trim(); if (!code) return;
            var existing = scanState.rows.filter(function (r) { return r.code.toLowerCase() === code.toLowerCase(); })[0];
            if (existing) { existing.qty = String((parseInt(existing.qty || "1", 10) || 1) + 1); renderScanRows(); return; }
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/ScanLookup",
                type: "GET", dataType: "json", data: { M_Requisition_ID: parent.M_Requisition_ID, code: code },
                success: function (raw) {
                    var it = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    scanState.rows.push({ id: "s" + (++rowCounter), code: code, item: (it && it.RecordId > 0) ? it : null, qty: "1" });
                    scanState.error = (it && it.RecordId > 0) ? "" : lbl("VAS_240_NoMatchFor", "No catalog match for") + ' "' + code + '"';
                    renderScanRows();
                },
                error: function (err) { console.log(err); }
            });
        }

        function renderScanRows() {
            var d = $("#vasRblScan"); if (!d.length) return;
            var err = d.find("#vasRblScanError");
            err.toggleClass("vas-rbl-is-hidden", !scanState.error).text(scanState.error);
            var empty = d.find("#vasRblScanEmpty"), grid = d.find("#vasRblScanGrid");
            if (!scanState.rows.length) { empty.removeClass("vas-rbl-is-hidden"); grid.addClass("vas-rbl-is-hidden"); }
            else {
                empty.addClass("vas-rbl-is-hidden"); grid.removeClass("vas-rbl-is-hidden");
                var body = d.find("#vasRblScanRows"); body.empty();
                scanState.rows.forEach(function (r) {
                    var matched = !!r.item;
                    var badge = matched ? (r.item.Kind === "C" ? "charge" : "product") : "danger";
                    var blabel = matched ? (r.item.Kind === "C" ? lbl("VAS_240_Charge", "Charge") : lbl("VAS_240_Product", "Product")) : lbl("VAS_240_Unknown", "Unknown");
                    var row = $('<div class="vas-rbl-scan-grid__row' + (matched ? "" : " is-unknown") + '"></div>');
                    row.html('<span class="vas-rbl-scan-grid__code">' + esc(r.code) + '</span><span class="vas-rbl-scan-grid__name">' + esc(matched ? r.item.DisplayName : "—") +
                        '</span><span class="vas-rbl-badge vas-rbl-badge--' + badge + '">' + esc(blabel) + "</span>" +
                        '<div class="vas-rbl-qty-stepper"><button type="button" data-qminus="' + r.id + '" ' + (!matched || parseInt(r.qty, 10) <= 1 ? "disabled" : "") + ">−</button>" +
                        '<input type="text" inputmode="numeric" data-qinput="' + r.id + '" value="' + esc(r.qty) + '" ' + (matched ? "" : "disabled") + ' />' +
                        '<button type="button" data-qplus="' + r.id + '" ' + (matched ? "" : "disabled") + ">+</button></div>" +
                        '<button type="button" class="vas-rbl-icon-btn--danger vas-rbl-scan-del" data-id="' + r.id + '">' + icon("trash", "🗑") + "</button>");
                    body.append(row);
                });
            }
            var matched = scanState.rows.filter(function (r) { return !!r.item; });
            var units = matched.reduce(function (a, r) { return a + (parseInt(r.qty || "0", 10) || 0); }, 0);
            var unknown = scanState.rows.length - matched.length;
            d.find("#vasRblScanSummary").text(!scanState.rows.length ? lbl("VAS_240_NoScans", "No scans yet")
                : matched.length + " " + lbl("VAS_240_Matched", "matched") + " · " + units + " " + lbl("VAS_240_Units", "unit(s)") + (unknown > 0 ? " · " + unknown + " " + lbl("VAS_240_Unknown", "unknown") : ""));
            d.find("[data-act=submit-scan]").prop("disabled", matched.length === 0).text(lbl("VAS_240_AddLines", "Add lines") + (matched.length ? " (" + matched.length + ")" : ""));
        }

        function submitScan() {
            var added = [];
            var maxLine = 0;
            for (var i = 0; i < lines.length; i++) maxLine = Math.max(maxLine, lines[i].values.Line || 0);
            scanState.rows.forEach(function (r) {
                if (!r.item) return;
                maxLine += 10;
                var line = {
                    rowId: "r" + (++rowCounter), status: "new", dirty: true, _priceOverride: false,
                    _productType: r.item.Kind === "C" ? "" : (r.item.ProductType || ""),
                    values: { M_RequisitionLine_ID: 0, Line: maxLine, M_Product_ID: r.item.Kind === "C" ? 0 : r.item.RecordId, C_Charge_ID: r.item.Kind === "C" ? r.item.RecordId : 0,
                        M_AttributeSetInstance_ID: 0, Qty: parseInt(r.qty || "1", 10) || 1, QtyEntered: parseInt(r.qty || "1", 10) || 1, C_UOM_ID: 0, PriceActual: 0, Discount: 0, Notes: "", Description: "" },
                    display: { productName: r.item.Kind === "C" ? "" : r.item.DisplayName, chargeName: r.item.Kind === "C" ? r.item.DisplayName : "", uomName: "", attrName: "", hasAttributeSet: !!r.item.HasAttributeSet }
                };
                seedAllColumns(line.values);
                lines.unshift(line); added.push(line);
            });
            closeDialogs(); render();
            added.forEach(function (l) { runCallout(l, l.values.C_Charge_ID > 0 ? "C_Charge_ID" : "M_Product_ID"); });
        }

        /* Tab off the last cell ("...") saves the pending row(s). A new line is NOT
           auto-created - the user adds the next line manually via the Add button. A
           blank/invalid row blocks with the same message the Save button shows. */
        function saveThenAddLine() {
            if (!parent || !parent.IsEditable) { showToast(docMsg("VAS_240_NotEditable", "This {0} cannot take new lines")); return; }
            if (unsavedLines().length) afterCallouts(function () { saveRows(); });   // wait for in-flight callout first
            editing = null; render();
        }

        /* ---------- save / delete ---------- */
        /* Commit whatever inline cell editor is focused right now (its blur handler does
           the value commit + re-render). Called before a toolbar Save so a value the user
           just typed but hasn't blurred yet is included in the save. */
        function flushActiveEdit() {
            var el = document.activeElement;
            if (!el || !$root || !$root[0] || !$root[0].contains(el)) return;
            var tag = el.tagName;
            if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") $(el).triggerHandler("blur");
        }

        function buildRowPayload(l) {
            var v = l.values;
            // Core fields drive the business setters; Values carries the full column
            // bag so every callout-set column is persisted server-side.
            return { M_RequisitionLine_ID: v.M_RequisitionLine_ID || 0, RowKey: l.rowId, Line: v.Line || 0, M_Product_ID: v.M_Product_ID || 0, C_Charge_ID: v.C_Charge_ID || 0,
                M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0, QtyEntered: v.QtyEntered || 0, Qty: v.Qty || 0, C_UOM_ID: v.C_UOM_ID || 0,
                PriceActual: v.PriceActual || 0, Discount: v.Discount || 0, Description: v.Description || "",
                Values: v,
                // Columns the user intentionally changed in the Additional Info modal.
                // ApplyExtraColumns uses this list to distinguish a deliberate null (should be
                // persisted) from a column the user never touched (should keep the DB value).
                TouchedCols: l._dynTouched ? Object.keys(l._dynTouched) : [] };
        }

        /* Replay any rows queued while a save was in flight. Called from the success / error
           handlers of saveRows so rows the user edited or added during the previous POST are
           picked up automatically without requiring another explicit Save click. */
        function flushPendingSave() {
            var ids = pendingSaveIds; pendingSaveIds = {};
            for (var k in ids) { if (ids.hasOwnProperty(k)) { saveRows(null, ids); return; } }
        }

        /* One async callout finished. Drains the waiter queue once nothing is outstanding. */
        function calloutSettled() {
            if (calloutPending > 0) calloutPending--;
            if (calloutPending > 0 || !calloutWaiters.length) return;
            var waiters = calloutWaiters;
            calloutWaiters = [];
            for (var i = 0; i < waiters.length; i++) {
                try { waiters[i](); } catch (ex) { if (window.console) console.log(ex); }
            }
        }

        /* Run fn once every in-flight callout has applied its values — immediately when none
           is pending. Used by Save so a qty/price typed and Saved without tabbing out is
           persisted with the callout's recomputed price / tax / amounts. */
        function afterCallouts(fn) {
            if (!calloutPending) { fn(); return; }
            var ran = false;
            var once = function () { if (ran) return; ran = true; fn(); };
            calloutWaiters.push(once);
            // Backstop: never let a wedged callout block Save forever.
            setTimeout(once, CALLOUT_WAIT_MS);
        }

        /* The single Save entry point for every user gesture (Save button, keyboard shortcut).
           Commits the focused cell then waits for any in-flight callout to finish before
           posting — so the line saves with the recomputed price / tax / amounts. */
        function flushAndSave() {
            flushActiveEdit();
            afterCallouts(function () { saveRows(); });
        }

        /* Save the currently-unsaved lines as a non-blocking batch. Each saved row shows
           its OWN per-row spinner (like a callout) instead of a panel-wide overlay, and
           is locked from editing until the save returns - so the user can keep working
           (Add line, edit other rows) while the save is in flight.
           restrictIds (optional): map of rowId -> true. When supplied only those rows are
           saved; used by flushPendingSave to replay a queued partial batch. */
        function saveRows(done, restrictIds) {
            commitMorePopover();
            if (!panelEditable()) { if (done) done(false); return; }   // read-only when doc completed/void/reversed/closed
            if (isHeaderDirty()) { showToast(lbl("VAS_240_SaveHeaderFirst", "Please save the header record before adding lines.")); if (done) done(false); return; }
            var batch = unsavedLines();
            if (restrictIds) batch = batch.filter(function (l) { return restrictIds[l.rowId]; });
            if (!batch.length || !parent) { if (done) done(false); return; }
            if (!validateUnsaved(batch)) { if (done) done(false); return; }   // AD_Column mandatory validation
            // Prevent a concurrent POST from racing on the same DB transaction.
            // Queue the row IDs instead; flushPendingSave replays them once this POST returns.
            if (saveInFlight) {
                batch.forEach(function (l) { pendingSaveIds[l.rowId] = true; });
                if (done) done(false);
                return;
            }
            saveInFlight = true;
            var rows = batch.map(buildRowPayload);
            // Lock + show a per-row spinner on each saving row.
            batch.forEach(function (l) { l._saving = true; setRowBusy(l, true, lbl("VAS_240_Saving", "Saving…")); });
            renderHeaderButtons();   // the batch no longer counts as "unsaved" -> Save mutes
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/SaveLines",
                type: "POST", dataType: "json", data: { payload: JSON.stringify({ M_Requisition_ID: parent.M_Requisition_ID, AD_Window_ID: $self.AD_Window_ID || 0, Page: linePage, Lines: rows }) },
                success: function (raw) {
                    batch.forEach(function (l) { l._saving = false; });
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    saveInFlight = false;
                    if (res && res.Success) { applyLinePaging(res); mergeSavedLines(batch, res.Lines); showToast(lbl("VAS_240_LinesSaved", "Lines saved")); refreshSummary(); refreshHeaderRecord(); if (done) done(true); }
                    else { batch.forEach(function (l) { setRowBusy(l, false); }); showServerSaveErrors(batch, res); if (done) done(false); }
                    flushPendingSave();
                },
                error: function (err) {
                    console.log(err);
                    batch.forEach(function (l) { l._saving = false; setRowBusy(l, false); });
                    saveInFlight = false;
                    showServerSaveErrors(batch, null);
                    if (done) done(false);
                    flushPendingSave();
                }
            });
        }

        /* Paint server-side save failures on their record rows (line._error) instead of a
           single toast - so every failing record in a multi-row save is flagged in place.
           Falls back to the first row (then a toast) for a batch-level error with no per-line
           mapping. */
        function showServerSaveErrors(batch, res) {
            var errs = (res && res.LineErrors) || [];
            var mapped = 0;
            for (var i = 0; i < errs.length; i++) {
                var l = findBatchLine(batch, errs[i]);
                if (l) { l._error = errs[i].Message || lbl("VAS_240_SaveFailed", "Save failed"); mapped++; }
            }
            if (!mapped) {
                // No per-line info (e.g. a batch-level exception) - still show it in place on
                // the first row, and toast as a safety net.
                var detail = (res && res.ErrorDetail) ? res.ErrorDetail : "";
                var msg = lbl((res && res.ErrorKey) || "VAS_240_SaveFailed", "Save failed") + (detail ? " - " + detail : "");
                if (batch.length) batch[0]._error = msg; else showToast(msg);
            }
            render();
            var $first = $linesBody.find(".vas-rbl-row--line.is-invalid").first();
            if ($first.length && $first[0].scrollIntoView) $first[0].scrollIntoView({ block: "nearest" });
        }

        /* Match a server LineSaveError back to its client line: by RowKey (the client rowId),
           then by M_RequisitionLine_ID (existing lines), then by Line number. */
        function findBatchLine(batch, e) {
            var i;
            if (e && e.RowKey) { for (i = 0; i < batch.length; i++) if (batch[i].rowId === e.RowKey) return batch[i]; }
            if (e && e.M_RequisitionLine_ID > 0) { for (i = 0; i < batch.length; i++) if ((batch[i].values.M_RequisitionLine_ID || 0) === e.M_RequisitionLine_ID) return batch[i]; }
            if (e && e.Line > 0) { for (i = 0; i < batch.length; i++) if ((batch[i].values.Line || 0) === e.Line) return batch[i]; }
            return null;
        }

        /* Replace just the saved lines with the server's fresh copies, while PRESERVING
           any client line the save did not cover: brand-new lines the user started while
           the save was in flight, and saved lines they are mid-editing (still dirty, not
           in this batch). Without this, the server's full line list would clobber that
           in-progress work. */
        function mergeSavedLines(batch, serverRows) {
            var inBatch = function (l) { return batch.indexOf(l) >= 0; };
            // Saved lines edited after the batch started - keep the client (dirty) copy.
            var dirtyById = {};
            lines.forEach(function (l) {
                var id = l.values.M_RequisitionLine_ID || 0;
                if (id > 0 && !inBatch(l) && l.dirty) dirtyById[id] = l;
            });
            // Brand-new client lines not part of this batch (added during the save).
            var newKeep = lines.filter(function (l) { return (l.values.M_RequisitionLine_ID || 0) <= 0 && !inBatch(l); });
            var merged = (serverRows || []).map(function (r) {
                return dirtyById[r.M_RequisitionLine_ID] || fromServerRow(r);
            });
            lines = newKeep.concat(merged);
            if (editing && !lineById(editing.rowId)) editing = null;
            render();
        }

        function reloadLines(serverRows) {
            lines = []; editing = null; morePopoverFor = null;
            if (serverRows) for (var i = 0; i < serverRows.length; i++) lines.push(fromServerRow(serverRows[i]));
            render();
        }

        /* Rebuild from the server rows after a DELETE while PRESERVING unsaved client work,
           so deleting one existing record doesn't discard other rows the user is still
           creating / editing. Brand-new client lines (id<=0) and dirty edits on existing
           lines (id>0) are kept as-is; clean saved lines are refreshed from the server rows.
           A deleted line is simply absent from serverRows (and any selected client-only new
           line was already spliced out by the caller), so it drops out naturally. Because
           page navigation is blocked while unsaved lines exist (gotoLinePage), all unsaved /
           dirty rows are on the current page, so the returned page rows cover them. Mirrors
           mergeSavedLines (the save path) - the delete is the only other full server reload. */
        function reloadLinesKeepingUnsaved(serverRows) {
            var dirtyById = {};
            lines.forEach(function (l) {
                var id = l.values.M_RequisitionLine_ID || 0;
                if (id > 0 && l.dirty) dirtyById[id] = l;   // edited-but-unsaved existing line
            });
            var newKeep = lines.filter(function (l) { return (l.values.M_RequisitionLine_ID || 0) <= 0; });
            var merged = (serverRows || []).map(function (r) {
                return dirtyById[r.M_RequisitionLine_ID] || fromServerRow(r);
            });
            lines = newKeep.concat(merged);
            morePopoverFor = null;
            if (editing && !lineById(editing.rowId)) editing = null;   // clear edit target if it was deleted
            render();
        }

        function deleteSelected() {
            if (!panelEditable()) return;             // read-only when doc completed/void/reversed/closed
            var sel = selectedLines(); if (!sel.length) return;
            var ids = [], localOnly = [];
            sel.forEach(function (l) { if (l.values.M_RequisitionLine_ID > 0) ids.push(l.values.M_RequisitionLine_ID); else localOnly.push(l); });
            localOnly.forEach(function (l) { var i = lines.indexOf(l); if (i >= 0) lines.splice(i, 1); });
            if (!ids.length) { render(); return; }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_240_RequisitionBottomPanel/DeleteLines",
                type: "POST", dataType: "json", data: { payload: JSON.stringify({ M_Requisition_ID: parent.M_Requisition_ID, AD_Window_ID: $self.AD_Window_ID || 0, Page: linePage, LineIds: ids }) },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.Success) { applyLinePaging(res); reloadLinesKeepingUnsaved(res.Lines); showToast(lbl("VAS_240_LinesDeleted", "Lines deleted")); refreshSummary(); refreshHeaderRecord(); }
                    else showToast(lbl((res && res.ErrorKey) || "VAS_240_DeleteFailed", "Delete failed"));
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_240_DeleteFailed", "Delete failed")); }
            });
        }

        /* ---------- misc ---------- */
        function showToast(msg) {
            VAS.PanelUtil.showToast(msg);
        }

        function closeDialogs() { $("#vasRblAttr, #vasRblScan, #vasRblMore").remove(); attrState = null; scanState = null; morePopoverFor = null; }

        function onDocMouseDown(e) {
            // The additional-fields modal manages its own outside-click (backdrop); no
            // inline popover to dismiss here anymore.
        }

        // Escape closes the top-most open dialog / popover.
        $(document).on("keydown.vascil", function (e) {
            if (e.key !== "Escape") return;
            if (scanState) { closeDialogs(); return; }
            if (attrState) { closeDialogs(); return; }
            // The Additional-Info modal closes ONLY via its Done button (Escape ignored).
        });

        // Alt+Ctrl+N/S/D/Z/Q keyboard shortcuts via the shared utility (VAI154 12-Aug-2026).
        $self._shortcuts = VAS.PanelShortcuts.register({
            /**
             * Panel is active when it is visible in the DOM and an order is loaded.
             * Both conditions must hold; the shortcut is silently ignored otherwise.
             */
            isActive: function () {
                return !!(parent && $root && $root.is(":visible"));
            },
            /**
             * Suppress shortcuts while any attribute/scan dialog or the more-popover
             * is open — the user must finish or dismiss it before acting via keyboard.
             */
            hasBlockingDialog: function () {
                return !!(attrState || scanState || morePopoverFor ||
                          document.getElementById("vasRblAttr") ||
                          document.getElementById("vasRblScan"));
            },
            /** Alt+Ctrl+N — add a new line, same as the Add button. */
            onNew: function () { addLine(); },
            /** Alt+Ctrl+S — save all unsaved lines, same as the Save button. */
            onSave: function () { flushAndSave(); },
            /**
             * Alt+Ctrl+D — delete the selected line(s).
             * Shows a toast when nothing is selected; read-only guard is
             * handled inside deleteSelected (panelEditable check).
             */
            onDelete: function () {
                if (!selectedCount()) {
                    showToast(lbl("VAS_240_SelectRowToDelete", "Select a row to delete"));
                    return;
                }
                deleteSelected();
            },
            /**
             * Alt+Ctrl+Z — undo the "active" line using the same priority chain as VAS_074:
             *   1. The row currently in edit mode (field has focus).
             *   2. The first selected unsaved row.
             *   3. The first unsaved row on the page (no selection required).
             *   4. Toast "Nothing to undo" when there is nothing to revert.
             * A saved+dirty line reverts to its pristine snapshot (undoLine).
             * A never-saved line is discarded entirely (discardNewLine).
             */
            onUndo: function () {
                if (!panelEditable()) return;
                var target = (editing && lineById(editing.rowId)) || null;
                if (!target || !(target.status === "new" || target.dirty)) {
                    target = selectedLines().filter(function (l) { return !l._saving && (l.status === "new" || l.dirty); })[0] || null;
                }
                if (!target) {
                    for (var i = 0; i < lines.length; i++) {
                        if (!lines[i]._saving && (lines[i].status === "new" || lines[i].dirty)) { target = lines[i]; break; }
                    }
                }
                if (!target) { showToast(lbl("VAS_240_NothingToUndo", "Nothing to undo")); return; }
                if (target.status === "new") { discardNewLine(target); } else { undoLine(target); }
            },
            /**
             * Alt+Ctrl+Q — refresh the current page for the loaded order, same
             * as the Refresh button (re-fetches from the server, discards any
             * unsaved client-side edits on this page).
             */
            onRefresh: function () {
                if (parent && parent.M_Requisition_ID) $self.fetchData(parent.M_Requisition_ID, linePage);
            }
        });

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_240_RequisitionBottomPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") this.table_ID = curTab.getAD_Table_ID();
        if (curTab && typeof curTab.getAD_Window_ID === "function") this.AD_Window_ID = curTab.getAD_Window_ID();
        this.init();
    };

    VAS.VAS_240_RequisitionBottomPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            // Pass true when a row exists in the grid but the order has no DB ID yet
            // (new unsaved record), so the panel can show a more helpful message.
            this.clear(selectedRow !== undefined && recordID <= 0);
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    VAS.VAS_240_RequisitionBottomPanel.prototype.sizeChanged = function (width) { this.panelWidth = width; };

    VAS.VAS_240_RequisitionBottomPanel.prototype.dispose = function () {
        // Remove the capture-phase shortcut listener registered during init (VAI154 12-Aug-2026).
        if (this._shortcuts) { this._shortcuts.dispose(); this._shortcuts = null; }
        $(document).off("mousedown.vascil").off("keydown.vascil");
        $("#vasRblAttr, #vasRblScan, .vas-rbl-toast").remove();
        this.record_ID = 0; this.table_ID = 0; this.windowNo = 0;
        this.curTab = null; this.selectedRow = null; this.panelWidth = null;
    };

})(VAS, jQuery);
