/************************************************************
 * Module Name    : VAS
 * Purpose        : Create Invoice Line tab panel. Renders the editable
 *                  "Invoice Lines & Summary" grid for the selected parent
 *                  invoice exactly as the Onfinity prototype:
 *                    - click-to-edit cells (Product/Charge, Description,
 *                      Quantity, UOM, Price, Tax) with keyboard tab order
 *                      Primary -> Description -> Qty -> UOM -> Price -> Tax,
 *                    - catalog autocomplete popover (keyword + scroll paging,
 *                      arrow-key navigation) over real products AND charges,
 *                    - attribute picker dialog (list + create) backed by
 *                      M_AttributeSetInstance,
 *                    - quick-add-by-barcode scan dialog,
 *                    - per-row more popover (Discount % + Notes),
 *                    - live subtotal / tax / total footer.
 *                  UOM and Tax are real, editable dropdowns (C_UOM / C_Tax);
 *                  price / tax / amounts resolve through the server callout and
 *                  every line is persisted through MInvoiceLine.
 * chronological  : Development
 *   VAI_145       Created  25 June 2026
 ************************************************************/
// NOTE: Replace VAI_145 with your own Employee Code before committing.
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_074_CreateInvoiceLinePanel = function () {
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

        /* parent invoice context returned by GetPanelData */
        var parent = null;
        /* Monotonic token for GetPanelData (fetchData) requests. Bumped on every fetchData AND
           on clear(), and each fetch captures its value; a fetch's async success is IGNORED when
           the token has moved on. Guards the record-switch race: opening the AR Invoice window
           in NEW mode (e.g. from the VAS_064 widget) first positions the grid on an existing
           invoice (fetchData(existingId) in flight) then clears for the new record - without the
           token the stale response would repaint the previous invoice's lines on the new record. */
        var fetchSeq = 0;
        /* server-side line paging (20/page): current 0-based page, total saved lines, size */
        var linePage = 0, linesTotal = 0, linePageSize = 20;
        /* saved totals of every line NOT on the current page (invoice grand total minus this
           page). renderTotals adds the live current-page sum so the totals row reflects the
           WHOLE invoice, not just the loaded page, while still updating during edits. */
        var otherSub = 0, otherTax = 0, otherTcs = 0;
        /* dropdown catalogs */
        var uomList = [], taxList = [], taxRateById = {};
        /* AD_Column metadata cache (callout code + validation), keyed by column */
        var columnMeta = {};
        /* lower-cased ColumnName -> canonical (dictionary-cased) ColumnName. Used to
           canonicalise a DB-cased line.values key (PostgreSQL lowercases, Oracle uppercases)
           back to the dictionary case when priming the window context - so we never emit two
           case-variant keys for the same column (the server merges window context into a
           case-INSENSITIVE dictionary, and two variants throw "same key already added"). */
        var columnNameByLc = {};
        /* client-side line rows + reactive UI state */
        var lines = [];
        var rowCounter = 0;
        var editing = null;            // { rowId, field }
        var morePopoverFor = null;     // rowId
        // True only WHILE render() detaches/re-attaches the row whose catalog dropdown is
        // open (see the preserve block in render). Detaching a focused input fires a native
        // blur, whose handler (commitPrimary) would otherwise clear `editing` + rebuild the
        // row - discarding the in-progress product/charge search and tearing down the open
        // dropdown. This flag makes the primary blur handler ignore that transient blur so a
        // background save completing mid-search never wipes the loaded dropdown results.
        var suppressPrimaryBlur = false;
        /* catalog popover working state (for the row currently editing a primary) */
        var catalog = { results: [], highlight: 0, seq: 0, offset: 0, hasMore: true, loading: false, term: "", debounce: null };
        /* attribute picker + scan working state */
        var attrState = null, scanState = null;
        // Save serialization: only ONE SaveLines POST may be in flight for this invoice at a
        // time. Two concurrent saves would share a single server-side named DB transaction (and
        // race on the invoice's shared tax/total rows), so the second one failed. `saveInFlight`
        // guards the request; a Save issued while one is running SNAPSHOTS exactly the rows the
        // user asked to save into `pendingSaveIds` (a rowId set) and the in-flight save's
        // completion flushes THOSE rows - NOT any line the user added afterwards but never
        // Save-clicked. So the user can keep adding / editing / saving and each Save-clicked
        // batch persists back-to-back, while an in-progress unsaved line is left untouched.
        var saveInFlight = false, pendingSaveIds = {};

        var CATALOG_PAGE_SIZE = 50;
        var SEARCH_DEBOUNCE = 260;
        var TAB_ORDER = ["primary", "description", "quantity", "uom", "price", "tax", "more"];

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

        /* Upper bound for an amount MAGNITUDE - Int32.MaxValue (2,147,483,647), matching
           VAS_118 QuickJournal. Capped inclusive of its precision digits so a value can
           never overflow a 32-bit signed integer downstream. Negative amounts ARE allowed
           (credit memos / treat-as-discount), so only the magnitude is clamped - the sign
           is preserved. */
        var AMOUNT_MAX_VALUE = 2147483647;

        /* Clean an amount input's raw text: an optional leading '-' (only when allowNeg),
           digits and one decimal separator, the fraction capped to `prec` digits and the
           magnitude clamped to AMOUNT_MAX_VALUE. A lone '-' / '' is preserved so the user
           can keep typing. */
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
            // Clamp the magnitude to Int32.MaxValue (parseNum ignores the sign for this test).
            if (out !== "" && out !== decSep && Math.abs(parseNum(out)) > AMOUNT_MAX_VALUE) out = fmtAmtInput(AMOUNT_MAX_VALUE, p);
            return (neg ? "-" : "") + out;
        }

        function bindAmountInput($inp, prec, allowNeg) {
            $inp.attr({ inputmode: "decimal", autocomplete: "off" });
            $inp.on("keypress", function (e) {
                if (e.ctrlKey || e.metaKey || e.which === 0 || e.which === 8) return;
                var ch = String.fromCharCode(e.which);
                // Leading minus only, and only when negatives are allowed (e.g. price).
                if (ch === "-") { if (!allowNeg || this.selectionStart !== 0 || this.value.indexOf("-") !== -1) e.preventDefault(); return; }
                if (ch === decSep) { if (this.value.indexOf(decSep) !== -1) e.preventDefault(); return; }
                if (ch === grpSep) return;
                if (!/[0-9]/.test(ch)) e.preventDefault();
            });
            // Enforce precision + Int32 magnitude cap on every input event (typing, paste,
            // autofill) so an out-of-range value can never survive a keystroke.
            $inp.on("input", function () {
                var before = this.value, caret = this.selectionStart;
                var after = sanitizeAmount(before, prec, allowNeg);
                if (after !== before) {
                    $inp.val(after);
                    var np = Math.max(0, (caret == null ? after.length : caret) - (before.length - after.length));
                    try { this.setSelectionRange(np, np); } catch (e2) { }
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

        function icon(name, glyph) { return '<span class="vas-cil-icon" data-icon="' + name + '">' + (glyph || "") + "</span>"; }

        /* ---------- per-line calculation (mirrors the prototype formulas) ---------- */
        function taxRate(id) { return taxRateById[id] || 0; }
        /* Entered line amount (qty x price less discount). On a tax-inclusive invoice this
           is the GROSS (price already contains tax); otherwise it is the net. */
        function lineGross(line) {
            var v = line.values;
            var disc = (+v.Discount || 0) / 100;
            return (+v.QtyEntered || 0) * (+v.PriceEntered || 0) * (1 - disc);
        }
        /* Line tax. Tax-inclusive: EXTRACT it from the gross (gross*r/(100+r)) - mirrors
           the framework MTax.CalculateTax(amount, taxIncluded=true). Tax-exclusive: add
           it on top of the net (net*r/100). */
        function lineTaxAmount(line) {
            var rate = taxRate(line.values.C_Tax_ID);
            var gross = lineGross(line);
            if (parent && parent.IsTaxIncluded) return rate ? gross * rate / (100 + rate) : 0;
            return gross * rate / 100;
        }
        /* Surcharge amount (SurchargeAmt) - an extra tax the framework computes for a tax that
           has a Surcharge_Tax_ID (MInvoiceLine.SetTaxAmt / CalloutTax). It's a stored value
           (the client can't derive it from a rate), read case-insensitively. */
        function lineSurcharge(line) { return +lineVal(line, "SurchargeAmt") || 0; }
        /* Total line tax = base tax + surcharge (per request: totalTax = TaxAmt + SurchargeAmt). */
        function lineTaxTotal(line) { return lineTaxAmount(line) + lineSurcharge(line); }
        /* ---------- bottom-panel totals: use the SAVED line columns, not a live recompute ----------
           The invoice totals row reflects the actual stored C_InvoiceLine amounts:
             Subtotal = TaxBaseAmt, Tax = TaxAmt + SurchargeAmt, Total = TaxBaseAmt + TaxAmt + SurchargeAmt.
           These are the values the framework writes on save (tax already accounted for whether the
           price is tax-inclusive or exclusive), so the row updates after the record is saved and does
           NOT need recomputing on every amount edit. Read case-insensitively (PG lowercases keys);
           an unsaved/edited line contributes its last saved value (0 for a brand-new line). */
        function lineTaxBaseSaved(line) { return +lineVal(line, "TaxBaseAmt") || 0; }
        function lineTaxSaved(line) { return +lineVal(line, "TaxAmt") || 0; }
        function lineTaxTotalSaved(line) { return lineTaxSaved(line) + lineSurcharge(line); }
        /* Net (tax-excluded) line amount = the Subtotal contribution. Tax-inclusive: gross
           minus the extracted tax (incl. surcharge); tax-exclusive: the gross IS the net. */
        function lineSubtotal(line) {
            var gross = lineGross(line);
            return (parent && parent.IsTaxIncluded) ? gross - lineTaxTotal(line) : gross;
        }
        /* Per-row "Line Amount" = the line NET (tax base), so the column reconciles with the
           Subtotal total (which sums TaxBaseAmt). A clean SAVED line uses the framework's stored
           TaxBaseAmt - already correct for a tax-inclusive OR exclusive price, and exact (no
           rate-extraction rounding). A new / edited line has no fresh stored base, so it falls
           back to the live, tax-mode-aware calc (exclusive: gross; inclusive: gross - tax). */
        function lineAmount(line) {
            if (line.status === "saved" && !line.dirty) {
                var base = lineVal(line, "TaxBaseAmt");
                if (base != null && base !== "") return +base || 0;
            }
            return lineSubtotal(line);
        }

        /* ---------- lifecycle ---------- */
        this.init = function () {
            $root = $('<div class="vas-cil-root"></div>');
            $body = $('<div class="vas-cil-body"></div>');
            $emptyState = $('<div class="vas-cil-empty" style="display:none;"></div>');
            $emptyState.text(lbl("VAS_074_NoInvoice", "Select an invoice to add lines"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            buildShell();
            // Start hidden: the tab only appears once refreshPanelData loads a real invoice
            // (record_ID > 0). A new/unsaved parent never flashes the empty box.
            applyTabVisibility(false);
            $(document).on("mousedown.vascil", onDocMouseDown);
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy.css({ position: "absolute", width: "100%", height: "100%", "text-align": "center", "z-index": "999" });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }
        function showBusy(show) { if ($busy && $busy[0]) $busy[0].style.visibility = show ? "visible" : "hidden"; }

        /* Hide/show the WHOLE tab (framework "Invoice Lines" title bar + our root) when no
           invoice is selected. The live framework build appends
           <div class="vis-ad-w-p-ap-tp-body-head"> immediately BEFORE getRoot() into a
           shared content div, so the title bar is our root's IMMEDIATE PREVIOUS sibling -
           target only ours (other panels append their own head+root pairs in the same div).
           Toggling a class (display:none !important, not inline display) so the framework's
           own setSize()/.show() on resize / panel switch can't override it and flash the
           empty tab back. In the VIS2_0 tab path refreshPanelData fires on every record
           change (no :visible guard), so the tab reappears when a real record loads. */
        function applyTabVisibility(show) {
            if (!$root || !$root.length) return;
            $root.toggleClass("vas-cil-tab-hidden", !show);
            // The framework "Invoice Lines" title bar (our root's previous sibling) is
            // redundant with the panel's own header - hide it PERMANENTLY, regardless of
            // record/lines (never re-shown). The CSS :has() rule covers this too; this is
            // the fallback for browsers without :has support. Re-asserted on every render.
            var $head = $root.prev(".vis-ad-w-p-ap-tp-body-head");
            if (!$head.length) {
                // Older build fallback: head lives under .vis-ad-w-p-ap-tp-outerwrap.
                var $host = $root.closest(".vis-ad-w-p-ap-tp-outerwrap");
                if ($host.length) $head = $host.find(".vis-ad-w-p-ap-tp-o-b-head");
            }
            $head.addClass("vas-cil-tab-hidden");
        }

        this.fetchData = function (recordID, page) {
            // Framework calls fetchData(recordID) on record load -> reset to page 0; the
            // pager calls it with an explicit page. Server returns LinePageSize (20) rows.
            var reqPage = (typeof page === "number" && page >= 0) ? page : 0;
            // Claim the latest request token so a stale/out-of-order response (e.g. a prior
            // invoice's load that lands AFTER a clear() for a new record) is discarded below.
            var mySeq = ++fetchSeq;
            // A pager page change (explicit page arg) is OUR own AJAX, NOT a framework record
            // load - the framework paints no spinner for it, so show the per-panel busy overlay
            // ourselves. A framework record load (no page arg) already gets the framework's own
            // vis-apanel-busy, so we skip ours there to avoid a stacked double spinner.
            var isPageChange = (typeof page === "number");
            if (isPageChange) showBusy(true);
            // Tear down any open dialog FIRST. The framework calls fetchData to (re)load the
            // record - including after a save - and a still-open dialog's position:fixed
            // backdrop would otherwise be left orphaned over the page (morePopoverFor is
            // cleared below but the DOM node isn't), swallowing clicks/scroll ("scroll stops
            // working after save"). closeDialogs() removes #vasCilMore/#vasCilScan/#vasCilAttr;
            // also reset the reusable AttributeControl's own state.
            closeDialogs();
            try { if (window.VIS && VIS.AttributeControl && VIS.AttributeControl.close) VIS.AttributeControl.close(); } catch (e) { }
            // NOTE: no showBusy() here. The framework already paints its own
            // vis-apanel-busy / vis_widgetloader over the tab while it loads a record;
            // adding ours (identical markup) stacked a SECOND spinner on line load. The
            // per-panel $busy is kept for our own dialog AJAX (lot/serno/attribute/delete)
            // that the framework does not cover.
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetPanelData",
                type: "GET", dataType: "json", data: { C_Invoice_ID: recordID, AD_Window_ID: $self.AD_Window_ID || 0, page: reqPage },
                success: function (raw) {
                    // Superseded by a newer fetchData / clear() (record switched, or new-record
                    // mode) - drop this stale response so it can't repaint the previous invoice.
                    if (mySeq !== fetchSeq) { if (isPageChange) showBusy(false); return; }
                    var data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    parent = data || null;
                    linesTotal = (parent && parent.LinesTotal) || 0;
                    linePage = (parent && +parent.LinePage) || 0;
                    linePageSize = (parent && +parent.LinePageSize) || 20;
                    otherSub = (parent && +parent.OtherPagesSubtotal) || 0;
                    otherTax = (parent && +parent.OtherPagesTax) || 0;
                    otherTcs = (parent && +parent.OtherPagesTcs) || 0;
                    uomList = (parent && parent.UomList) || [];
                    taxList = (parent && parent.TaxList) || [];
                    taxRateById = {};
                    for (var i = 0; i < taxList.length; i++) taxRateById[taxList[i].C_Tax_ID] = +taxList[i].Rate || 0;
                    // Cache the AD_Column meta (callout code + validation) once per load.
                    columnMeta = {};
                    columnNameByLc = {};
                    var cols = (parent && parent.Columns) || [];
                    for (var c = 0; c < cols.length; c++) {
                        columnMeta[cols[c].ColumnName] = cols[c];
                        columnNameByLc[String(cols[c].ColumnName).toLowerCase()] = cols[c].ColumnName;
                    }
                    // A validated MLookup caches its list at first load; drop the cache on a
                    // new invoice so its val rule re-resolves against the new header context
                    // (e.g. a different C_BPartner_ID) instead of serving the prior invoice's list.
                    _mlookupCache = {};
                    lines = [];
                    if (parent && parent.Lines) for (var j = 0; j < parent.Lines.length; j++) lines.push(fromServerRow(parent.Lines[j]));
                    editing = null; morePopoverFor = null;
                    taxSummary = null;   // drop the prior record's breakdown -> fallback shows first
                    render();
                    if ($root && $root[0]) $root.scrollTop(0);
                    refreshSummary();    // then load this invoice's server tax breakdown
                    if (isPageChange) showBusy(false);
                },
                error: function (err) { console.log(err); if (isPageChange && mySeq === fetchSeq) showBusy(false); }
            });
        };

        this.clear = function () {
            // Invalidate any in-flight fetchData so its response can't repaint stale data over
            // the cleared (new-record) panel - the record-switch race described on fetchSeq.
            fetchSeq++;
            // Also tear down any open dialog so a fixed backdrop isn't orphaned over the page.
            closeDialogs();
            try { if (window.VIS && VIS.AttributeControl && VIS.AttributeControl.close) VIS.AttributeControl.close(); } catch (e) { }
            parent = null; lines = []; render();
        };

        function fromServerRow(r) {
            // Start from the full column bag (every C_InvoiceLine column) so the
            // line VO carries all columns the callout may read / write.
            var vals = {};
            // Canonicalize each DB-cased key (PostgreSQL lowercases, Oracle uppercases) to the
            // dictionary ColumnName so the line bag holds exactly ONE key per column. Without
            // this, an edit / callout writes a proper-cased key (e.g. "QtyInvoiced") ALONGSIDE
            // the loaded DB-cased one ("qtyinvoiced"), and BOTH are sent on save.
            if (r.Values) for (var k in r.Values) if (r.Values.hasOwnProperty(k)) {
                var canon = columnNameByLc[String(k).toLowerCase()] || k;
                vals[canon] = r.Values[k];
            }
            vals.C_InvoiceLine_ID = r.C_InvoiceLine_ID;
            vals.Line = r.Line;
            vals.M_Product_ID = r.M_Product_ID || 0;
            vals.C_Charge_ID = r.C_Charge_ID || 0;
            vals.M_AttributeSetInstance_ID = r.M_AttributeSetInstance_ID || 0;
            vals.QtyEntered = r.QtyEntered || 0;
            vals.C_UOM_ID = r.C_UOM_ID || 0;
            vals.PriceEntered = r.PriceEntered || 0;
            vals.C_Tax_ID = r.C_Tax_ID || 0;
            vals.Description = r.Description || "";
            if (vals.Discount == null) vals.Discount = 0;
            if (vals.Notes == null) vals.Notes = "";
            var line = {
                rowId: "r" + (++rowCounter), status: "saved", dirty: false, _priceOverride: false,
                _productType: r.ProductType || "",
                values: vals,
                display: {
                    productName: r.ProductName || "",
                    chargeName: r.ChargeName || "",
                    uomName: r.UOMName || "",
                    taxName: r.TaxName || "",
                    attrName: r.AttrName || "",
                    hasAttributeSet: r.M_Product_ID > 0 && (!!r.AttrName && !!r.HasAttributeSet)
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

        /* Panel-level Undo (Ctrl+Alt+Z): revert the "active" line - the row being edited,
           else the first selected unsaved row, else the newest unsaved row. Saved+dirty ->
           revert to the pristine snapshot; new (never-saved) -> remove the row. Same rule as
           the per-row ↺ affordance (renderMoreCell). */
        function undoActive() {
            if (!panelEditable()) return;
            var target = (editing && lineById(editing.rowId)) || null;
            if (!target || !(target.status === "new" || target.dirty)) {
                target = selectedLines().filter(function (l) { return l.status === "new" || l.dirty; })[0] || null;
            }
            if (!target) {
                for (var i = 0; i < lines.length; i++) if (lines[i].status === "new" || lines[i].dirty) { target = lines[i]; break; }
            }
            if (!target) { showToast(lbl("VAS_074_NothingToUndo", "Nothing to undo")); return; }
            if (target.status === "saved" && target._saved) undoLine(target); else discardNewLine(target);
        }

        /* Seed every C_InvoiceLine column (from the cached columnMeta) on a new
           line so the VO carries all columns; explicit defaults already set are
           preserved. */
        function seedAllColumns(v) {
            for (var col in columnMeta) {
                if (!columnMeta.hasOwnProperty(col)) continue;
                if (v[col] === undefined) v[col] = null;
            }
            if (parent) v.C_Invoice_ID = parent.C_Invoice_ID;
        }

        /* ---------- shell ---------- */
        function buildShell() {
            $body.empty();
            var $panel = $('<section class="vas-cil-panel" aria-label="Invoice Lines and Summary"></section>');

            var $header = $('<header class="vas-cil-panel__header"></header>');
            $header.append('<div><h2 class="vas-cil-panel__title">' + esc(lbl("VAS_074_InvoiceLinesSummary", "Invoice Lines & Summary")) +
                '</h2><p class="vas-cil-panel__subtitle">' + esc(lbl("VAS_074_LinesSubtitle", "Lines, navigation, and posting totals")) + "</p></div>");

            var $actions = $('<div class="vas-cil-panel__actions"></div>');
            // Scan hidden for now (handler/markup kept so it can be re-enabled by
            // dropping the vas-cil-is-hidden class).
            var $scanBtn = $('<button type="button" class="vas-cil-btn vas-cil-btn--outline vas-cil-is-hidden" data-action="open-scan">' + icon("scan-line", "▭") +
                "<span>" + esc(lbl("VAS_074_Scan", "Scan")) + "</span></button>");
            $addBtn = $('<button type="button" class="vas-cil-btn vas-cil-btn--outline" data-action="add-line" title="' + esc(lbl("VAS_074_AddLine", "Add line")) + ' (Ctrl+Alt+N)">' + icon("plus", "+") +
                "<span>" + esc(lbl("VAS_074_AddLine", "Add line")) + "</span></button>");
            // Icon + label span built ONCE here; renderHeaderButtons only updates the label
            // span's TEXT (never rebuilds innerHTML) so the label can't momentarily blank out.
            $saveBtn = $('<button type="button" class="vas-cil-btn vas-cil-btn--primary vas-cil-is-disabled" data-action="save-rows" title="' + esc(lbl("VAS_074_SaveRow", "Save row")) + ' (Ctrl+Alt+S)">' + icon("hard-drive", "💾") + '<span class="vas-cil-save-lbl"></span></button>');
            $deleteBtn = $('<button type="button" class="vas-cil-btn vas-cil-btn--danger vas-cil-is-disabled" data-action="delete-selected" title="' + esc(lbl("Delete", "Delete")) + ' (Ctrl+Alt+D)" disabled>' +
                icon("trash", "🗑") + "<span>" + esc(lbl("Delete", "Delete")) + ' <span class="vas-cil-sel-count"></span></span></button>');
            // Refresh re-loads the current invoice's panel data (current page) from the server.
            // A read action - stays enabled even on a completed/void/read-only invoice, so it is
            // NOT touched by renderHeaderButtons' lock logic.
            $refreshBtn = $('<button type="button" class="vas-cil-btn vas-cil-btn--outline" data-action="refresh-panel" title="' + esc(lbl("VAS_074_Refresh", "Refresh")) + ' (Ctrl+Alt+Q)">' + icon("refresh-cw", "⟳") +
                "<span>" + esc(lbl("VAS_074_Refresh", "Refresh")) + "</span></button>");
            $actions.append($scanBtn, $addBtn, $saveBtn, $deleteBtn, $refreshBtn);
            $header.append($actions);
            $panel.append($header);

            var $table = $('<div class="vas-cil-table" role="table"></div>');
            $table.append(buildHeadRow());
            $linesBody = $('<div class="vas-cil-tbody"></div>');
            $table.append($linesBody);
            $totalsRow = $('<div class="vas-cil-row vas-cil-row--totals"></div>');
            $table.append($totalsRow);
            $panel.append($table);
            // Server-side line pager (20/page): "X-Y of N" + prev/next.
            $pager = $('<div class="vas-cil-linepager" style="display:none;"></div>');
            $pager.on("click", "[data-act=lp-prev]", function () { gotoLinePage(linePage - 1); });
            $pager.on("click", "[data-act=lp-next]", function () { gotoLinePage(linePage + 1); });
            $panel.append($pager);
            $body.append($panel);

            $header.on("click", "[data-action=open-scan]", openScanDialog);
            $header.on("click", "[data-action=add-line]", function () { addLine(); });
            $header.on("click", "[data-action=refresh-panel]", function () { refreshPanel(); });
            // Save on mousedown (not click): mousedown fires BEFORE the focused cell
            // editor blurs, so we can flush that pending edit ourselves and the action
            // never gets lost to a blur/commit re-render happening between mousedown and
            // mouseup. preventDefault keeps focus from being stolen first.
            $saveBtn.on("mousedown", function (e) { e.preventDefault(); flushActiveEdit(); saveRows(); });
            // Keyboard activation (Enter / Space) fires click with detail 0 and no
            // mousedown - handle that here so the button stays accessible, without
            // double-firing on a real mouse click (detail >= 1, already handled above).
            $saveBtn.on("click", function (e) { if (e.detail === 0) { flushActiveEdit(); saveRows(); } });
            $deleteBtn.on("click", deleteSelected);
        }

        function buildHeadRow() {
            var $row = $('<div class="vas-cil-row vas-cil-row--head" role="row"></div>');
            $selectAll = $('<input type="checkbox" aria-label="' + esc(lbl("VAS_074_SelectAll", "Select all lines")) + '" />');
            $row.append($('<div class="vas-cil-cell vas-cil-cell--check" role="columnheader"></div>').append($selectAll));
            $selectAll.on("change", function () {
                if (selectedCount() === lines.length) clearSelection();
                else for (var i = 0; i < lines.length; i++) lines[i]._sel = true;
                render();
            });
            $row.append('<div class="vas-cil-cell" role="columnheader">' + esc(lbl("VAS_074_ProductCharge", "Product / Charge")) + "</div>");
            $row.append('<div class="vas-cil-cell" role="columnheader">' + esc(lbl("Description", "Description")) + "</div>");
            $row.append('<div class="vas-cil-cell vas-cil-cell--right" role="columnheader">' + esc(lbl("VAS_074_QtyUom", "Quantity / UOM")) + "</div>");
            $row.append('<div class="vas-cil-cell vas-cil-cell--right" role="columnheader">' + esc(lbl("Price", "Price")) + "</div>");
            $row.append('<div class="vas-cil-cell" role="columnheader">' + esc(lbl("Tax", "Tax")) + "</div>");
            $row.append('<div class="vas-cil-cell vas-cil-cell--right" role="columnheader">' + esc(lbl("VAS_074_LineAmount", "Line Amount")) + "</div>");
            $row.append('<div class="vas-cil-cell vas-cil-cell--more" role="columnheader" aria-label="' + esc(lbl("VAS_074_More", "More")) + '"></div>');
            return $row;
        }

        /* ---------- render ---------- */
        function render() {
            if (!parent || !parent.C_Invoice_ID) {
                // No invoice selected (record_ID <= 0): hide the whole tab - title bar and
                // content box - so there's no empty prompt or stray scrollbar.
                $emptyState.show(); $body.hide();
                applyTabVisibility(false);
                return;
            }
            applyTabVisibility(true);
            $emptyState.hide(); $body.show();
            // Read-only invoice: mark the panel so disabled controls (checkbox, "...")
            // show a not-allowed cursor via their (enabled) parent cell - a disabled
            // control ignores its own `cursor` in Chromium, so the cell shows it instead.
            $body.toggleClass("vas-cil-locked", !panelEditable());

            // Preserve the LIVE DOM of the row whose product/charge catalog is OPEN (the user is
            // mid-search). An EXTERNAL render - a background save completing, a callout, the
            // summary refresh - would otherwise rebuild that row -> resetCatalog -> tear down and
            // RELOAD the dropdown (flicker; a render/keystroke seq race sometimes left it empty).
            // detach() keeps the node's handlers, input value and open popover; we re-insert it
            // in place and restore focus + caret (detach blurs the input).
            var keepId = null, $keep = null, refocusEl = null, caretPos = null;
            if (editing && catalog.$pop && catalog.$pop.closest("body").length && lineById(editing.rowId)) {
                var $existing = $linesBody.find('[data-rowid="' + editing.rowId + '"]').first();
                if ($existing.length) {
                    var ae = document.activeElement;
                    if (ae && $.contains($existing[0], ae)) {
                        refocusEl = ae;
                        try { caretPos = ae.selectionStart; } catch (e) { }
                    }
                    keepId = editing.rowId;
                    // Detaching a focused input fires blur; suppress the primary blur handler
                    // so it doesn't commitPrimary() -> clear editing -> destroy the search. The
                    // flag is cleared after focus is restored below (async, to also cover
                    // browsers that dispatch the detach blur on a later tick).
                    suppressPrimaryBlur = true;
                    $keep = $existing.detach();
                }
            }

            $linesBody.empty();
            if (!lines.length) {
                $linesBody.append('<div class="vas-cil-emptyrow">' + esc(lbl("VAS_074_NoLines", "No lines yet - use Add line or Scan")) + "</div>");
            } else {
                for (var i = 0; i < lines.length; i++) {
                    if ($keep && lines[i].rowId === keepId) $linesBody.append($keep);
                    else $linesBody.append(renderRow(lines[i]));
                }
            }
            renderTotals();
            renderHeaderButtons();
            renderPager();

            // Restore focus + caret to the preserved input (detach blurred it).
            if (refocusEl) {
                try {
                    refocusEl.focus();
                    if (caretPos != null && refocusEl.setSelectionRange) refocusEl.setSelectionRange(caretPos, caretPos);
                } catch (e) { }
            }
            // Clear the blur-suppression after the current tick so any detach blur dispatched
            // synchronously (during detach) OR asynchronously (queued to a later tick) is
            // ignored; a genuine later user blur still commits normally.
            if (suppressPrimaryBlur) setTimeout(function () { suppressPrimaryBlur = false; }, 0);
        }

        /* Server-side line pager: "Showing X-Y of N" on the left, "‹ P of Q ›" on the
           right. Shown whenever the invoice has saved lines (even a single page). */
        function renderPager() {
            if (!$pager) return;
            var total = linesTotal || 0, size = linePageSize || 20;
            if (!total) { $pager.hide().empty(); return; }
            var pageCount = Math.max(1, Math.ceil(total / size));
            var start = linePage * size + 1;
            var end = Math.min(total, (linePage + 1) * size);
            var showing = lbl("VAS_074_Showing", "Showing") + " " + start + "–" + end + " " + lbl("VAS_074_Of", "of") + " " + total;
            var pageInfo = (linePage + 1) + " " + lbl("VAS_074_Of", "of") + " " + pageCount;
            $pager.html(
                '<span class="vas-cil-linepager__showing">' + esc(showing) + "</span>" +
                '<div class="vas-cil-linepager__nav">' +
                '<button type="button" class="vas-cil-attr-pagebtn" data-act="lp-prev"' + (linePage <= 0 ? " disabled" : "") +
                ' aria-label="' + esc(lbl("VAS_074_Prev", "Previous")) + '">' + icon("chevron-left", "‹") + "</button>" +
                '<span class="vas-cil-linepager__info">' + esc(pageInfo) + "</span>" +
                '<button type="button" class="vas-cil-attr-pagebtn" data-act="lp-next"' + (linePage >= pageCount - 1 ? " disabled" : "") +
                ' aria-label="' + esc(lbl("VAS_074_Next", "Next")) + '">' + icon("chevron-right", "›") + "</button>" +
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
            if (typeof res.OtherPagesTax === "number") otherTax = res.OtherPagesTax;
            if (typeof res.OtherPagesTcs === "number") otherTcs = res.OtherPagesTcs;
        }

        /* Load another page of saved lines. Guards unsaved work so a page change never
           silently discards a new/edited row. */
        function gotoLinePage(p) {
            if (!parent || !parent.C_Invoice_ID) return;
            var pageCount = Math.max(1, Math.ceil((linesTotal || 0) / (linePageSize || 20)));
            if (p < 0) p = 0; if (p > pageCount - 1) p = pageCount - 1;
            if (p === linePage) return;
            if (unsavedLines().length) { showToast(lbl("VAS_074_SavePageFirst", "Save or discard your changes before changing page")); return; }
            $self.fetchData(parent.C_Invoice_ID, p);
        }

        /* Reload the CURRENT invoice's panel data from the server, RESETTING to the first page -
           the Refresh button / Ctrl+Alt+Q. Flushes any pending cell edit first so it counts
           toward the unsaved-work guard, then blocks (like a page change) when unsaved lines
           exist so a reload never silently discards a new/edited row. Passing page 0 (an explicit
           number) both jumps to page 1 AND makes fetchData show the per-panel busy overlay
           (explicit page = our own AJAX, not a framework load). */
        function refreshPanel() {
            if (!parent || !parent.C_Invoice_ID) return;
            flushActiveEdit();
            if (unsavedLines().length) { showToast(lbl("VAS_074_SaveBeforeRefresh", "Save or discard your changes before refreshing")); return; }
            $self.fetchData(parent.C_Invoice_ID, 0);   // 0 = first page + triggers the busy overlay
        }

        // Cached server tax breakdown for the CURRENT invoice (from VAS/PoReceipt/GetTaxData,
        // the same contract VAS_InvoiceSummary uses). null until loaded / on a fresh record;
        // renderTotals() then falls back to a client-computed subtotal+tax so an unsaved
        // invoice still shows live totals. Refreshed on load, save and delete (refreshSummary).
        var taxSummary = null;

        /* Vertical invoice summary (Sub Total / per-tax lines / [TCS] / Grand Total), mirroring
           the VAS_InvoiceSummary design, right-aligned in the totals row. */
        function renderTotals() {
            $totalsRow.empty();
            // On a treat-as-discount invoice, the product/qty/uom are driven by the referenced
            // original line (picked in the "..." Additional Info modal), so show a left-side
            // hint telling the user where to select it.
            var html = "";
            if (isTreatAsDiscount()) {
                html += '<div class="vas-cil-totals-note">' +
                    esc(lbl("VAS_074_TreatAsDiscountHint", "Please select the Original Invoice details in the Additional Info section to select the product")) +
                    '</div>';
            }
            html += '<div class="vas-cil-summary">';
            if (taxSummary && taxSummary.length) {
                var h = taxSummary[0];
                var sym = h.CurSymbol || "";
                var prec = (h.stdPrecision != null && h.stdPrecision >= 0) ? h.stdPrecision : precision();
                html += summaryRow(lbl("VAS_SubTotal", "Sub Total"), sym, h.TotalLines, prec, false);
                for (var i = 0; i < taxSummary.length; i++) {
                    if (!taxSummary[i].TaxName) continue;
                    html += summaryRow(taxSummary[i].TaxName, sym, taxSummary[i].TaxAmt, prec, false);
                }
                if (+h.TCSAmount) html += summaryRow(lbl("VAS_TCSTotal", "TCS Total"), sym, h.TCSAmount, prec, false);
                html += summaryRow(lbl("GrandTotal", "Grand Total"), sym, h.GrandTotal, prec, true);
            } else {
                // Fallback (no server breakdown yet): saved totals of the OTHER pages + the live
                // sum of this page, so a new/unsaved invoice still shows a grand total.
                var sub = otherSub, tax = otherTax, tcs = otherTcs, p = precision();
                for (var j = 0; j < lines.length; j++) { sub += lineTaxBaseSaved(lines[j]); tax += lineTaxTotalSaved(lines[j]); tcs += lineTcs(lines[j]); }
                html += summaryRow(lbl("VAS_SubTotal", "Sub Total"), "", sub, p, false);
                html += summaryRow(lbl("Tax", "Tax"), "", tax, p, false);
                if (tcs) html += summaryRow(lbl("VA106_TaxCollectedAtSource", "TCS"), "", tcs, p, false);
                html += summaryRow(lbl("GrandTotal", "Grand Total"), "", sub + tax + tcs, p, true);
            }
            $totalsRow.html(html + "</div>");
        }
        // One "Label: value" line of the summary. sym is the currency symbol ("" -> none).
        function summaryRow(label, sym, amount, prec, grand) {
            var val = (sym ? sym + " " : "") + (+amount || 0).toLocaleString(window.navigator.language,
                { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return '<div class="vas-cil-summary-row' + (grand ? " vas-cil-summary-row--grand" : "") + '">' +
                '<span class="vas-cil-summary-label">' + esc(label) + ":</span>" +
                '<span class="vas-cil-summary-val">' + esc(val) + "</span></div>";
        }
        /* (Re)load the server tax breakdown for the current invoice and repaint the summary.
           Same endpoint/contract as VAS_InvoiceSummary; called on load, save and delete. */
        function refreshSummary() {
            var id = parent && parent.C_Invoice_ID;
            if (!(id > 0)) { taxSummary = null; renderTotals(); return; }
            try {
                VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetTaxData",
                    { InvoiceID: id }, function (data) {
                        taxSummary = (data && data.length) ? data : null;
                        renderTotals();
                    });
            } catch (e) { if (window.console) console.log(e); }
        }
        /* Per-line TCS amount (VA106_TCSAmount) - 0 when the module isn't installed or
           the column isn't set; read case-insensitively (PG lowercases the key). */
        function lineTcs(line) { return +lineVal(line, "VA106_TCSAmount") || 0; }

        /* The panel is editable only while the invoice can still take line changes -
           server-computed IsEditable = !Processed && DocStatus NOT IN (CO, CL, VO, RE).
           When false the whole panel is read-only: no Add / Save / Delete and no cell edit. */
        function panelEditable() { return !!(parent && parent.IsEditable); }

        function renderHeaderButtons() {
            var n = unsavedLines().length;
            var locked = !panelEditable();
            // Add and Save are BOTH always visible (no swap). Swapping them on the same
            // toolbar slot caused a click that committed an edit (which flipped the
            // dirty count) to land on the other button mid-press, so Save needed two
            // clicks. Save is only muted (not hidden) when there is nothing to save.
            // When the doc is completed/void/reversed/closed (locked) Add + Save are
            // fully disabled (native `disabled` blocks the mousedown/click handlers).
            $addBtn.removeClass("vas-cil-is-hidden").prop("disabled", locked).toggleClass("vas-cil-is-disabled", locked);
            $saveBtn.removeClass("vas-cil-is-hidden").prop("disabled", locked).toggleClass("vas-cil-is-disabled", locked || n === 0);
            // Update ONLY the label text (icon span stays put). Guard against a blank/space
            // message so the label never renders empty (the "Save button text disappears" bug -
            // VIS.Msg.getMsg can return a blank string once the message cache loads).
            var saveLbl = lbl("VAS_074_SaveRow", "Save row");
            if (!saveLbl || !saveLbl.trim()) saveLbl = "Save row";
            if (n > 1) saveLbl += lbl("VAS_074_PluralS", "s");
            if (n > 0) saveLbl += " (" + n + ")";
            $saveBtn.find(".vas-cil-save-lbl").text(saveLbl);
            var sc = selectedCount();
            $deleteBtn.prop("disabled", sc === 0 || locked).toggleClass("vas-cil-is-disabled", sc === 0 || locked);
            $deleteBtn.find(".vas-cil-sel-count").text(sc > 0 ? "(" + sc + ")" : "");
            if ($selectAll) $selectAll.prop("checked", lines.length > 0 && sc === lines.length).prop("disabled", locked);
        }

        function renderRow(line) {
            var v = line.values;
            var $row = $('<div class="vas-cil-row vas-cil-row--line" role="row" data-rowid="' + line.rowId + '"></div>');
            if (line._sel) $row.addClass("is-selected");
            if (line.status === "new" || line.dirty) $row.addClass("is-unsaved");

            // Read-only invoice (completed/void/reversed/closed): the row can't be
            // deleted, so its selection checkbox is disabled too.
            var cb = $('<input type="checkbox" />').prop("checked", !!line._sel).prop("disabled", !panelEditable());
            cb.on("change", function () { line._sel = this.checked; renderHeaderButtons(); $row.toggleClass("is-selected", this.checked); });
            $row.append($('<div class="vas-cil-cell vas-cil-cell--check" role="cell"></div>').append(cb));

            // Clicking anywhere on the row toggles its selection checkbox, so the whole
            // record is easy to pick (e.g. for delete). Clicks that land on an interactive
            // control - the editable field inputs, the UOM / Tax selects, the "..." / attr
            // buttons and links, or the checkbox itself - keep their own behaviour (enter
            // edit / open control) and must NOT also toggle selection.
            $row.on("click", function (e) {
                if (!panelEditable()) return;   // checkbox is disabled on a locked invoice
                if ($(e.target).closest("input, select, textarea, button, a, [role=button], .vas-cil-attr-link").length) return;
                line._sel = !line._sel;
                cb.prop("checked", line._sel);
                $row.toggleClass("is-selected", line._sel);
                renderHeaderButtons();
            });

            $row.append(renderPrimaryCell(line));
            $row.append(renderEditableCell(line, "description", v.Description, lbl("VAS_074_AddDescription", "Add description…"), { maxLength: colFieldLength("Description") }));
            $row.append(renderQtyUomCell(line));
            $row.append(renderEditableCell(line, "price", v.PriceEntered, "0" + decSep + "00", { align: "right", amount: true, maxLength: colFieldLength("PriceEntered") }));
            $row.append(renderTaxCell(line));

            var amt = lineAmount(line);
            // Show any NON-ZERO amount (negative amounts are valid on credit / treat-as-
            // discount lines); only a genuine 0 stays blank. `amt` is truthy for negatives.
            $row.append($('<div class="vas-cil-cell vas-cil-cell--right" role="cell"></div>')
                .append('<span class="vas-cil-amt">' + (amt ? esc(fmtMoney(amt)) : "") + "</span>"));

            $row.append(renderMoreCell(line));
            // Per-row inline validation error (set by validateUnsaved on Save) - shown as a
            // red label on the row itself instead of a global toast, so each failing record
            // in a multi-row save is flagged in place.
            if (line._error) {
                $row.addClass("is-invalid")
                    .append('<div class="vas-cil-row-error" role="alert">' + esc(line._error) + "</div>");
            }
            // Re-apply the per-row spinner after a re-render so an in-flight save (or
            // callout) keeps its indicator even if render() rebuilds the rows (e.g. the
            // user added a new line while this row was still saving).
            if (line._busy || line._saving) $row.addClass("is-busy").append(rowSpinHtml(line._saving ? lbl("VAS_074_Saving", "Saving…") : lbl("VAS_074_Calculating", "Calculating…")));
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
            var $i = $('<input type="text" readonly tabindex="-1" draggable="false" class="vas-cil-cell-edit__input vas-cil-cell-disp" />');
            $i.val(text || "");
            if (opts.placeholder) $i.attr("placeholder", opts.placeholder);
            if (opts.align === "right") $i.css("text-align", "right");
            if (opts.cls) $i.addClass(opts.cls);
            $i.attr("title", text || "");
            if (editable && !opts.readOnly) $i.on("click", function () { startEdit(line, field); });
            else {
                $i.addClass("vas-cil-cell-disp--ro");
                // A COLUMN-level read-only field (e.g. C_UOM_ID ReadOnlyLogic true) is rendered
                // DISABLED, not just readonly: `readonly` has no effect on a <select> and a plain
                // readonly text cell still looks editable, so disable + grey it so it's clearly
                // locked and browser-enforced. (A whole-invoice lock keeps the lighter readonly
                // look; that path leaves opts.readOnly unset.)
                if (opts.readOnly) $i.prop("disabled", true);
            }
            return $i;
        }

        function renderPrimaryCell(line) {
            var editable = parent && parent.IsEditable;
            var pField = primaryField(line);
            var isEditing = editing && editing.rowId === line.rowId && (editing.field === "product" || editing.field === "charge");
            var cell = $('<div class="vas-cil-cell" role="cell"></div>');
            var wrap = $('<div class="vas-cil-cell-edit"></div>');
            if (isEditing) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditing) {
                var inner = $('<div style="position:relative"></div>');
                wrap.append(inner);
                var $inp = $('<input type="text" class="vas-cil-cell-edit__input" />');
                // Seed from the committed display name; but if nothing is committed yet (a new /
                // cleared line) keep any IN-PROGRESS typed keyword. The keyword lives in
                // catalog.term (set on every keystroke), NOT in line.display, so a render()
                // triggered mid-typing - e.g. a background save completing while the user types a
                // product keyword on a new line - would otherwise blank the input and drop the
                // search (the "entered keyword gets rolled back" bug).
                var primaryInit = editing.field === "product" ? line.display.productName : line.display.chargeName;
                if (!primaryInit && catalog.term) primaryInit = catalog.term;
                $inp.val(primaryInit);
                $inp.attr("placeholder", editing.field === "product" ? lbl("VAS_074_SearchProduct", "Search product…") : lbl("VAS_074_SearchCharge", "Search charge…"));
                $inp.on("input", function () { scheduleCatalog($(this).val(), inner, line, $inp); });
                $inp.on("blur", function (e) {
                    // Ignore the transient blur fired when render() detaches this row to
                    // preserve its open dropdown (e.g. a background save completing mid-search)
                    // - committing here would clear `editing` and destroy the loaded results.
                    if (suppressPrimaryBlur) return;
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
                wrap.append(dispInput(line, pField, pv, { placeholder: lbl("VAS_074_AddProductCharge", "Add product / charge…"), readOnly: isTreatAsDiscount() }));
                // For a product carrying (or able to carry) an attribute set, show the
                // attribute-set-instance description as a clickable sub-line under the
                // product. Clicking it opens the attribute control instead of editing
                // the product name (so the click must not bubble to the cell handler).
                if (line.values.M_Product_ID > 0 && (line.display.attrName || line.display.hasAttributeSet)) {
                    var hasAttr = !!line.display.attrName;
                    var attrTxt = hasAttr ? line.display.attrName : lbl("VAS_074_SetAttribute", "Set attribute…");
                    var $attr = $('<span class="vas-cil-attr-link"></span>').text(attrTxt).attr("title", attrTxt);
                    if (!hasAttr) $attr.addClass("vas-cil-attr-link--empty");
                    // Clickable only when the invoice is editable AND the product actually
                    // carries an attribute set (M_AttributeSet_ID > 0). On a read-only invoice,
                    // or on a saved line whose product has no attribute set defined (e.g. the
                    // set was removed after the line was created but the old ASI description
                    // still shows), the attribute is informational only - not a link (no click,
                    // no pointer cursor / hover underline).
                    if (editable && productHasAttributeSet(line) && !isTreatAsDiscount()) {
                        // Open on mousedown + preventDefault (like Undo/Save): a plain click
                        // while a cell editor is focused blurs -> commits -> re-renders the row,
                        // destroying this element before mouseup so the FIRST click is eaten
                        // (the two-clicks-to-open bug). preventDefault keeps focus so the row
                        // isn't rebuilt; the click(detail===0) branch preserves keyboard/synthetic
                        // activation without double-firing on a real mouse click.
                        $attr.on("mousedown", function (e) { e.preventDefault(); e.stopPropagation(); openAttrDialog(line); });
                        $attr.on("click", function (e) { if (e.detail === 0) { e.stopPropagation(); openAttrDialog(line); } });
                    }
                    else $attr.addClass("vas-cil-attr-link--disabled");
                    wrap.append($attr);
                }
            }
            return cell;
        }

        function renderEditableCell(line, field, value, placeholder, opts) {
            opts = opts || {};
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-cil-cell' + (opts.align === "right" ? " vas-cil-cell--right" : "") + '" role="cell"></div>');
            var wrap = $('<div class="vas-cil-cell-edit"></div>');
            var isEditing = editing && editing.rowId === line.rowId && editing.field === field;
            if (isEditing) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditing) {
                var $inp = $('<input type="text" class="vas-cil-cell-edit__input" />');
                $inp.val(opts.amount ? fmtAmtInput(value, field === "quantity" ? 2 : precision()) : (value || ""));
                $inp.attr("placeholder", placeholder || "");
                if (opts.maxLength > 0) $inp.attr("maxlength", opts.maxLength);   // AD_Column.FieldLength cap
                if (opts.align === "right") $inp.css("text-align", "right");
                // Amount inputs: cap magnitude to Int32.MaxValue; allow a negative value for
                // everything except quantity (qty is coerced non-negative in commitField).
                if (opts.amount) bindAmountInput($inp, field === "quantity" ? 2 : precision(), field !== "quantity");
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
            var cell = $('<div class="vas-cil-cell vas-cil-cell--right" role="cell"></div>');
            var wrap = $('<div class="vas-cil-cell-edit"></div>');
            var editQty = editing && editing.rowId === line.rowId && editing.field === "quantity";
            var uomRO = isColumnReadOnly(line, "C_UOM_ID");   // AD_Column.ReadOnlyLogic / IsReadOnly
            var editUom = editing && editing.rowId === line.rowId && editing.field === "uom" && !uomRO;
            if (editQty || editUom) wrap.addClass("is-editing");
            cell.append(wrap);

            // quantity (top)
            if (editQty) {
                var $q = $('<input type="text" class="vas-cil-cell-edit__input" inputmode="decimal" />').val(fmtAmtInput(v.QtyEntered, 2)).css("text-align", "right");
                var qLen = colFieldLength("QtyEntered"); if (qLen > 0) $q.attr("maxlength", qLen);   // AD_Column.FieldLength cap
                bindAmountInput($q, 2, false);   // quantity: precision 2, non-negative, Int32 cap
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
                    { align: "right", placeholder: lbl("VAS_074_Qty", "Qty"), cls: "vas-cil-qtyval", readOnly: isColumnReadOnly(line, "QtyEntered") }));
            }

            // UOM (bottom) — real editable C_UOM dropdown, options filtered to this
            // line's context (C_UOM_ID AD_Val_Rule). Render with what is cached now,
            // then refine in place once the per-row list arrives.
            if (editUom) {
                var $sel = $('<select class="vas-cil-cell-edit__select"></select>');
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
                    { align: "right", placeholder: lbl("VAS_074_Uom", "UOM"), cls: "vas-cil-uomsub vas-cil-cell-disp--sub", readOnly: uomRO }));
            }
            return cell;
        }

        function renderTaxCell(line) {
            var v = line.values;
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-cil-cell" role="cell"></div>');
            var wrap = $('<div class="vas-cil-cell-edit"></div>');
            var isEditing = editing && editing.rowId === line.rowId && editing.field === "tax";
            if (isEditing) wrap.addClass("is-editing");
            cell.append(wrap);

            if (isEditing) {
                // Tax options filtered to this line's context (C_Tax_ID AD_Val_Rule);
                // refined in place once the per-row list arrives.
                var $sel = $('<select class="vas-cil-cell-edit__select"></select>');
                fillTaxOptions($sel, line);
                ensureRowLookups(line, function () {
                    if (editing && editing.rowId === line.rowId && editing.field === "tax" && $sel.closest("body").length)
                        fillTaxOptions($sel, line);
                });
                $sel.on("change", function () { setTax(line, parseInt($sel.val(), 10), $sel.find("option:selected").text()); });
                $sel.on("blur", function () { editing = null; render(); });
                $sel.on("keydown", function (e) {
                    e.stopPropagation();
                    if (e.key === "Tab") { e.preventDefault(); advanceField(line, "tax", parseInt($sel.val(), 10), e.shiftKey ? -1 : 1); return; }
                    if (e.key === "Enter") $sel.blur();
                    if (e.key === "Escape") { editing = null; render(); }
                });
                wrap.append($sel);
                setTimeout(function () { $sel.focus(); }, 0);
            } else {
                wrap.append(dispInput(line, "tax", line.display.taxName || "", { placeholder: "—", cls: "vas-cil-taxval" }));
            }
            return cell;
        }

        function renderMoreCell(line) {
            var editable = parent && parent.IsEditable;
            var cell = $('<div class="vas-cil-cell vas-cil-cell--more" role="cell" style="position:relative"></div>');
            // Undo affordance (↺). On a SAVED row with unsaved edits it reverts the row to
            // its last pristine snapshot; on a NEW (never-saved) row it removes the row
            // entirely (client-only discard - a new line has no snapshot to revert to).
            var canUndoEdits = line.status === "saved" && line.dirty && line._saved;
            var canDiscardNew = line.status === "new";
            if (editable && !line._saving && (canUndoEdits || canDiscardNew)) {
                var undoTitle = canDiscardNew ? lbl("VAS_074_UndoNewLine", "Undo (remove line)") : lbl("VAS_074_UndoChanges", "Undo changes");
                var $undo = $('<button type="button" class="vas-cil-undo-btn" title="' + esc(undoTitle) + '">' + icon("rotate-ccw", "↺") + "</button>");
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
            var $btn = $('<button type="button" class="vas-cil-more-btn" title="' + esc(lbl("VAS_074_More", "More")) + '">' + icon("more-horizontal", "⋯") + "</button>");
            if (morePopoverFor === line.rowId) $btn.addClass("is-open");
            // Blue "..." when the line already has additional-info values (visual cue).
            if (hasAdditionalInfo(line)) $btn.addClass("has-values");
            // Keep the "..." enabled even on a read-only invoice so the blue "has values" cue
            // shows and the user can still OPEN the modal to VIEW the additional info (its
            // fields render read-only per the framework's column read-only logic).
            $btn.prop("disabled", false);
            // Open the additional-fields MODAL. mousedown + preventDefault (like Undo/Save): a
            // plain click while a cell editor is focused blurs -> commits -> re-renders the row
            // and destroys this button before mouseup, eating the first click (two-clicks bug).
            // Keyboard is handled by the keydown below (Enter/Space), so no click branch here.
            $btn.on("mousedown", function (e) { e.preventDefault(); e.stopPropagation(); openMoreDialog(line); });
            // Keyboard: Tab continues the row's tab chain (forward -> save,
            // Shift+Tab -> Tax). Enter / Space open the modal explicitly - relying on the
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

        /* Additional-fields modal: Discount + Notes + every dynamic ("...") tab field.
           A true dialog (backdrop) rather than an inline popover. Discount / Notes keep
           their ids so commitMorePopover() reads them; dynamic fields commit on change. */
        function openMoreDialog(line) {
            closeDialogs();
            morePopoverFor = line.rowId;
            var primaryName = line.display.productName || line.display.chargeName ||
                (lbl("VAS_074_Line", "Line") + " " + line.values.Line);
            var backdrop = $('<div class="vas-cil-dialog-backdrop" id="vasCilMore"></div>');
            var dialog = $('<div class="vas-cil-dialog"></div>');
            dialog.html(
                '<header class="vas-cil-dialog__header"><div class="vas-cil-dialog__header-row">' +
                '<h3 class="vas-cil-dialog__title">' + esc(primaryName) + " - " + esc(lbl("VAS_074_AdditionalInfo", "Additional Info")) + "</h3>" +
                '<button type="button" class="vas-cil-dialog__close" data-act="close-more" aria-label="' + esc(lbl("VAS_074_Close", "Close")) + '" title="' + esc(lbl("VAS_074_Close", "Close")) + '">' + icon("x", "✕") + "</button>" +
                "</div></header>" +
                '<div class="vas-cil-dialog__body vas-cil-more-body vas-cil-more-grid" id="vasCilMoreBody"></div>' +
                '<footer class="vas-cil-dialog__footer vas-cil-dialog__footer--end">' +
                '<button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="close-more">' + esc(lbl("VAS_074_Done", "Done")) + "</button></footer>");
            backdrop.append(dialog);
            $("body").append(backdrop);

            var $body = dialog.find("#vasCilMoreBody");

            // Close ONLY via the Done button - not on backdrop click (so an outside
            // click, e.g. on the framework lookup popup, doesn't dismiss the modal).
            // Return focus to the row's "..." button so Tab continues the row's chain
            // (Tab off "..." saves the row) without the user re-grabbing the mouse.
            function done() {
                // Block close while a conditionally-mandatory curated field (e.g. Capital/Expense
                // on an Asset-Related line) is still empty - show the message in the modal and
                // keep it open until the required value is set.
                var miss = firstMissingDynMandatory(line);
                if (miss) { showMoreDialogError(miss); return; }
                commitMorePopover(); closeDialogs(); render(); focusMoreBtn(line);
            }
            dialog.on("click", "[data-act=close-more]", done);
            // Enter / Space on the focused Done button must close the dialog. A native
            // <button> would do this itself, but the framework shell swallows Enter (its
            // global handler preventDefaults it), so wire it explicitly - same pattern as
            // the row "..." button. stopPropagation keeps the key from bubbling to that
            // global handler.
            dialog.on("keydown", "[data-act=close-more]", function (e) {
                if (e.key === "Enter" || e.key === " " || e.key === "Spacebar" || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault(); e.stopPropagation(); done();
                }
            });
            // Field-group Show More/Less: collapse/expand the fields under a section header.
            dialog.on("click", "[data-act=fldgrp-toggle]", function (e) {
                e.preventDefault(); e.stopPropagation();
                toggleFieldGroup($(this).closest(".vas-cil-fldgrp"));
            });

            // Building the curated fields is heavy (each FK builds a native Vienna
            // control + lookup, synchronously). Paint the dialog with a spinner FIRST,
            // then build on the next tick so the user sees a busy indicator instead of a
            // frozen panel while the controls load.
            $body.removeClass("vas-cil-more-grid").addClass("vas-cil-dialog__body--loading")
                .html('<div class="vas-cil-dialog-loading"><span class="vas-cil-dialog-spin" aria-label="' + esc(lbl("VAS_074_Loading", "Loading…")) + '"></span></div>');
            setTimeout(function () {
                if (morePopoverFor !== line.rowId || !$body.parent().length) return;   // closed meanwhile
                $body.removeClass("vas-cil-dialog__body--loading").addClass("vas-cil-more-grid").empty();
                // Prime the window context with this line before building the FK controls,
                // so each control's AD_Val_Rule (e.g. C_Withholding_ID) validates against the
                // current line/header and actually loads data.
                primeLineContext(line);
                // Curated "Additional Info" fields (callout / read-only / validation / val-rule
                // honoured per control); shown 2-up via the .vas-cil-more-grid layout.
                appendDynFields(line, $body);
                applyFieldGroups($body);   // inject the collapsible section headers
                if (!$body.children("[data-col]:not(.vas-cil-dyn-hidden)").length)
                    $body.append('<p class="vas-cil-empty-message">' + esc(lbl("VAS_074_NoAdditionalInfo", "No additional info for this line")) + "</p>");
                $body.find("[data-col]:not(.vas-cil-dyn-hidden)").find("input,select,textarea").first().focus();
            }, 0);
        }

        /* Show a blocking validation message in the open "..." modal (above the footer) and
           focus the offending field, so the modal refuses to close until it's set. */
        function showMoreDialogError(miss) {
            var $dialog = $("#vasCilMore .vas-cil-dialog");
            if (!$dialog.length) return;
            var $err = $dialog.find(".vas-cil-more-error");
            if (!$err.length) {
                $err = $('<div class="vas-cil-more-error" role="alert"></div>');
                $dialog.find(".vas-cil-dialog__footer").before($err);
            }
            $err.text(miss.msg);
            var $fld = $("#vasCilMoreBody").children('[data-col="' + miss.col + '"]');
            if ($fld.length && $fld[0].scrollIntoView) $fld[0].scrollIntoView({ block: "nearest" });
            setTimeout(function () { $fld.find("input,select,textarea").first().focus(); }, 0);
        }

        /* Dismiss the "..." modal's blocking validation message (on any value change - it
           re-validates on the next close attempt). */
        function clearMoreDialogError() {
            $("#vasCilMore .vas-cil-dialog").find(".vas-cil-more-error").remove();
        }

        /* ---------- edit / commit ---------- */
        function startEdit(line, field) {
            if (!panelEditable()) return;             // completed/void/reversed/closed -> read-only
            if (line._saving) return;                 // row is being saved - locked until it returns
            // Product / Charge cell has no FIELD_COL mapping, so gate the treat-as-discount
            // lock here (Qty / UOM go through fieldReadOnly below via isColumnReadOnly).
            if ((field === "product" || field === "charge") && isTreatAsDiscount()) return;
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
            else if (field === "price") { if (!sameVal(v.PriceEntered, value)) { v.PriceEntered = value; line._priceOverride = true; changed = true; } }
            if (changed) markDirty(line);
            // A quantity change re-runs the line callout (quantity price-breaks + amounts,
            // attribute-aware) - same as UOM.
            if (changed && field === "quantity" && v.M_Product_ID > 0) runCallout(line, "QtyEntered");
            // A manual price change re-runs the line callout too (CalloutInvoice.amt) so the
            // line net / tax amounts recompute from the entered price - same mechanism as a
            // product change. The PriceEntered branch of `amt` keeps the entered price
            // (PriceActual = PriceEntered) and only recomputes amounts, so the manual
            // override is preserved (line._priceOverride stays set).
            else if (changed && field === "price" && v.M_Product_ID > 0) runCallout(line, "PriceEntered");
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

        function setTax(line, taxId, taxName) {
            if (sameVal(line.values.C_Tax_ID, taxId)) return;   // unchanged - no dirty / callout
            line.values.C_Tax_ID = taxId;
            line.display.taxName = taxId > 0 ? taxName : "";
            markDirty(line);
            // Re-run the line callout so tax amount / line total recompute server-side
            // (honours the chosen C_Tax_ID; a manual price override is preserved).
            runCallout(line, "C_Tax_ID");
        }

        /* ---------- per-row context-filtered lookups (AD_Val_Rule) ----------
         * The UOM / Tax (and catalog) controls load only values valid in the
         * current line's context: each column's AD_Val_Rule is resolved server-side
         * against the line's values + invoice header + session context. The filtered
         * UOM + tax lists are cached on the line, keyed by product / charge, so the
         * dropdown refines the moment the list arrives and is reused while context
         * is unchanged; it falls back to the panel-load lists until then. */

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
        function rowTaxList(line) {
            return (line._lk && line._lk.sig === rowLookupSig(line) && line._lk.tax) ? line._lk.tax : taxList;
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
        function fillTaxOptions($sel, line) {
            var v = line.values, list = rowTaxList(line);
            $sel.empty();
            $sel.append($("<option></option>").attr("value", 0).text(lbl("VAS_074_NoTax", "—")));
            for (var i = 0; i < list.length; i++) {
                var o = $("<option></option>").attr("value", list[i].C_Tax_ID).text(list[i].Name);
                if (list[i].C_Tax_ID === v.C_Tax_ID) o.prop("selected", true);
                $sel.append(o);
            }
        }

        /* Fetch (once, then cache) the UOM + tax lists valid for THIS line's context.
           cb() runs when the cache is ready (immediately when already cached). */
        function ensureRowLookups(line, cb) {
            if (!parent || !parent.C_Invoice_ID) { if (cb) cb(); return; }
            var sig = rowLookupSig(line);
            if (line._lk && line._lk.sig === sig) {
                if (line._lk.loaded) { if (cb) cb(); return; }
                if (line._lk.loading) { if (cb) line._lk.cbs.push(cb); return; }
            }
            line._lk = { sig: sig, loading: true, loaded: false, uom: null, tax: null, cbs: cb ? [cb] : [] };
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetLookupData",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ C_Invoice_ID: parent.C_Invoice_ID, RowValues: compactCtx(line.values) }) },
                success: function (raw) {
                    var d = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!line._lk || line._lk.sig !== sig) return;   // context moved on
                    line._lk.uom = (d && d.UomList) || null;
                    line._lk.tax = (d && d.TaxList) || null;
                    if (line._lk.tax) for (var i = 0; i < line._lk.tax.length; i++) taxRateById[line._lk.tax[i].C_Tax_ID] = +line._lk.tax[i].Rate || 0;
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
            var $d = $("#vasCilDisc-" + morePopoverFor), $n = $("#vasCilNotes-" + morePopoverFor);
            if ($d.length) { line.values.Discount = parseNum($d.val()); line.dirty = true; }
            if ($n.length) { line.values.Notes = $n.val(); line.dirty = true; }
        }

        /* Tab order: primary -> description -> quantity -> uom -> price -> tax -> "..." -> new row */
        function advanceField(line, currentField, value, direction) {
            // Commit the current field's value (UOM / Tax / the "..." button do not carry one).
            if (currentField === "product" || currentField === "charge") commitPrimary(line, currentField, value, true);
            else if (currentField !== "uom" && currentField !== "tax" && currentField !== "more") commitField(line, currentField, value);

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
                var $b = $linesBody.find('[data-rowid="' + line.rowId + '"] .vas-cil-more-btn');
                if ($b.length) $b.focus();
            }, 0);
        }

        /* ---------- line operations ---------- */
        function addLine() {
            if (!parent || !parent.IsEditable) { showToast(lbl("VAS_074_InvoiceNotEditable", "This invoice cannot take new lines")); return; }
            var maxLine = 0;
            for (var i = 0; i < lines.length; i++) maxLine = Math.max(maxLine, lines[i].values.Line || 0);
            var line = {
                rowId: "r" + (++rowCounter), status: "new", dirty: true, _priceOverride: false,
                values: {
                    C_InvoiceLine_ID: 0, Line: maxLine + 10, M_Product_ID: 0, C_Charge_ID: 0, M_AttributeSetInstance_ID: 0,
                    QtyEntered: 0, C_UOM_ID: 0, PriceEntered: 0, C_Tax_ID: 0, Discount: 0, Notes: "", Description: ""
                },
                display: { productName: "", chargeName: "", uomName: "", taxName: "", attrName: "", hasAttributeSet: false }
            };
            seedAllColumns(line.values);
            lines.unshift(line);
            // On a treat-as-discount invoice the product / qty / uom are locked and populated
            // from the referenced line (picked via the "..." modal), so do NOT auto-open the
            // product search editor - just render the new (locked) row and focus its "..."
            // button so the reference modal is one keystroke away.
            if (isTreatAsDiscount()) {
                editing = null;
                render();
                setTimeout(function () {
                    var $b = $linesBody.find('[data-rowid="' + line.rowId + '"] .vas-cil-more-btn');
                    if ($b.length) $b.focus();
                }, 0);
                return;
            }
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
            inner.find(".vas-cil-catalog-popover").remove();
            catalog.$pop = $('<div class="vas-cil-catalog-popover"></div>');
            catalog.$pop.on("scroll", function () {
                var el = this;
                if (catalog.hasMore && !catalog.loading && el.scrollTop + el.clientHeight >= el.scrollHeight - 40) loadCatalogPage(inner, line, $inp, false);
            });
            catalog.$pop.on("mousedown", ".vas-cil-catalog-popover__item", function (e) {
                e.preventDefault();
                commitCatalogItem(line, catalog.results[+$(this).attr("data-idx")]);
            });
            catalog.$pop.on("mouseenter", ".vas-cil-catalog-popover__item", function () { setHighlight(+$(this).attr("data-idx")); });
            // Show a loading row so the dropdown isn't an empty bordered box (which read
            // as a stray second line under the input) before the first results arrive.
            catalog.$pop.html('<div class="vas-cil-catalog__hint">' + esc(lbl("VAS_074_Loading", "Loading…")) + "</div>");
            inner.append(catalog.$pop);
            positionCatalog();
            loadCatalogPage(inner, line, $inp, true);
        }

        /* Place the dropdown so it is NEVER clipped by the panel root (which is
           overflow-y:auto, a hard clip box). Open below by default; flip ABOVE only when the
           list can't fit below AND there is more room above. Either way the popover's
           max-height is clamped to the space actually available on the chosen side WITHIN
           the root, so it stays fully visible and every row is reachable via internal scroll
           (fixes: flipped-up list clipped at the top, top rows unreachable). Re-measured as
           rows load. The --above modifier attaches it flush over the input (see CSS). */
        var CATALOG_MAX_PX = 260;   // keep in sync with .vas-cil-catalog-popover max-height (16.25em)
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
            catalog.$pop.toggleClass("vas-cil-catalog-popover--above", above);
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
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/SearchCatalog",
                type: "GET", dataType: "json",
                data: { C_Invoice_ID: parent.C_Invoice_ID, query: catalog.term, pageSize: CATALOG_PAGE_SIZE, offset: catalog.offset, rowContext: JSON.stringify(compactCtx(line.values)) },
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
                catalog.$pop.html('<div class="vas-cil-catalog__hint">' + esc(lbl("VAS_074_NoMatches", "No matches")) + "</div>");
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
            var blabel = it.Kind === "C" ? lbl("VAS_074_Charge", "Charge") : lbl("VAS_074_Product", "Product");
            // Name + type only (the key/value line is intentionally omitted to keep
            // the dropdown rows compact). Full "name (key)" stays in the tooltip.
            return '<button type="button" class="vas-cil-catalog-popover__item" data-catalog-item="true" data-idx="' + idx +
                '" title="' + esc(it.DisplayName + (it.SearchKey ? " (" + it.SearchKey + ")" : "")) + '">' +
                '<span class="vas-cil-catalog-popover__name">' + esc(it.DisplayName) + "</span>" +
                '<span class="vas-cil-badge vas-cil-badge--' + badge + '">' + esc(blabel) + "</span></button>";
        }

        /* Move the highlight by class only (no rebuild) and keep it in view. */
        function setHighlight(idx) {
            if (!catalog.$pop) return;
            var n = catalog.results.length; if (!n) return;
            idx = Math.max(0, Math.min(idx, n - 1));
            catalog.highlight = idx;
            var $items = catalog.$pop.children(".vas-cil-catalog-popover__item");
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
         * mTab / mField / ctx shims backed by THIS line + the parent invoice
         * context - the same GridTab contract the framework callout expects.
         * Whatever the callout writes via mTab.setValue lands in line.values.
         * If the framework callout isn't on the page we fall back to the server
         * CalcLine/RunCallout endpoint. No MInvoice is rebuilt and the callout
         * code is read from cache, not re-queried each change. */
        var ID_COLS = { M_Product_ID: 1, C_Charge_ID: 1, C_UOM_ID: 1, C_Tax_ID: 1, M_AttributeSetInstance_ID: 1, C_Currency_ID: 1 };
        var PANEL_COLS = {
            M_Product_ID: 1, C_Charge_ID: 1, M_AttributeSetInstance_ID: 1, QtyEntered: 1, QtyInvoiced: 1,
            C_UOM_ID: 1, PriceEntered: 1, PriceActual: 1, PriceList: 1, PriceLimit: 1, C_Tax_ID: 1,
            C_Currency_ID: 1, Discount: 1, LineNetAmt: 1, Description: 1, PrintDescription: 1, C_Invoice_ID: 1
        };
        // FALLBACK ONLY. The Compiere callout configured in the dictionary
        // (AD_Column.Callout -> columnMeta[col].Callout) ALWAYS takes priority (see
        // runCallout); this map is used ONLY when a pricing / amount column has NO callout
        // configured, so Attribute / Qty / UOM / Tax still recompute the SAME way a Product
        // change does (the real client CalloutInvoice). CalloutInvoice.Qty branches on the
        // changed column (mField.getColumnName()) and re-fetches prices via MInvoice/GetPrices
        // INCLUDING the M_AttributeSetInstance_ID, so attribute-level prices apply;
        // CalloutInvoice.Amt recomputes the line/tax amounts for a chosen tax (Tax would
        // RE-DETERMINE C_Tax_ID and overwrite the user's pick, so use Amt). To override any
        // of these, just set AD_Column.Callout on the column - no code change needed.
        var DEFAULT_CALLOUTS = {
            M_AttributeSetInstance_ID: "ViennaAdvantage.Model.CalloutInvoice.Qty",
            QtyEntered: "ViennaAdvantage.Model.CalloutInvoice.Qty; ViennaAdvantage.Model.CalloutInvoice.Amt",
            C_UOM_ID: "VAdvantage.Model.CalloutInvoice.qty; VAdvantage.Model.CalloutInvoice.amt",
            C_Tax_ID: "VAdvantage.Model.CalloutInvoice.amt;VAdvantage.Model.CalloutTax.SetTaxExemptReason",
            PriceEntered: "VAdvantage.Model.CalloutInvoice.amt;"
        };
        /* The callout string to run for a column: the Compiere callout(s) configured in the
           dictionary (AD_Column.Callout -> columnMeta[col].Callout) ALWAYS win; only when a
           column has none do we fall back to DEFAULT_CALLOUTS. The value may be a single
           callout or a ';'-separated chain across one or more classes (e.g. CalloutInvoice
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
            if (!(v.QtyEntered > 0)) { v.QtyEntered = 1; v.QtyInvoiced = 1; }

            // Resolve the callout(s) to run - dictionary AD_Column.Callout first, else the
            // DEFAULT_CALLOUTS fallback (see columnCalloutStr).
            var calloutStr = columnCalloutStr(column);
            var chain = calloutStr ? resolveCallouts(calloutStr) : null;
            if (chain && chain.length) {
                // The framework callout runs synchronously (blocking sync AJAX), so
                // paint the row busy indicator first, then run on the next tick so
                // the spinner is actually visible while the callout executes.
                setRowBusy(line, true);
                setTimeout(function () {
                    try {
                        runRealCallout(line, column, chain);
                        sanitizeLine(line);
                        applyCalloutReadback(line);
                    } finally {
                        line._busy = false;
                        render();
                        if (done) done();
                    }
                }, 0);
                return;
            }
            // Async server fallback - show the busy indicator until it returns.
            setRowBusy(line, true);
            runCalloutServer(line, column, function () { line._busy = false; render(); if (done) done(); });
        }

        function rowSpinHtml(label) { return '<span class="vas-cil-row-spin" aria-label="' + esc(label || "") + '"></span>'; }

        /* Toggle a per-row spinner immediately (no full re-render, so it paints before a
           synchronous callout blocks the thread). Used for both callouts and per-row save. */
        function setRowBusy(line, on, label) {
            line._busy = on;
            var $r = $linesBody.find('[data-rowid="' + line.rowId + '"]');
            $r.toggleClass("is-busy", on);
            $r.find(".vas-cil-row-spin").remove();
            if (on) $r.append(rowSpinHtml(label || lbl("VAS_074_Calculating", "Calculating…")));
        }

        /* Parse "Ns.Class.Method;Ns2.Class2.Method2" into resolvable callout
           instances + methods. Each token's namespace decides WHERE its class is
           resolved (see resolveCalloutCtor): ViennaAdvantage / VAdvantage map to the
           client VIS runtime, every OTHER Area prefix (VAS, VAFAM, VA###, ...) resolves
           in that module's own global registry. Method match is case-insensitive
           (AD_Column may store product / Product). */
        function resolveCallouts(calloutStr) {
            var out = [];
            var parts = String(calloutStr).split(";");
            for (var i = 0; i < parts.length; i++) {
                var token = parts[i].trim();
                if (!token) continue;
                var seg = token.split(".");
                if (seg.length < 2) continue;
                var method = seg.pop();
                var cls = seg.pop();
                var ctor = resolveCalloutCtor(seg, cls);   // seg = the namespace before the class
                if (typeof ctor !== "function") continue;
                var inst;
                try { inst = new ctor(); } catch (e) { continue; }
                var fn = inst[method];
                if (typeof fn !== "function") {
                    for (var k in inst) { if (typeof inst[k] === "function" && k.toLowerCase() === method.toLowerCase()) { fn = inst[k]; break; } }
                }
                if (typeof fn === "function") out.push({ inst: inst, fn: fn });
            }
            return out;
        }

        /* Resolve a callout class constructor HONOURING its declared namespace, mirroring
           the framework GridTab.processCallout / Utility.getFunctionByName.
           ns = the dotted segments before the class name (e.g. ["VAFAM","Model"]).
           ViennaAdvantage / VAdvantage are the .NET names for the client VIS runtime, so
           their first segment is remapped to VIS (-> VIS.Model.*). Any OTHER prefix is a
           separate module Area whose callout classes live under its OWN global - e.g. VAS
           registers VAS.Model.* (VAS_CalloutOpportunity.js), VAFAM registers
           VAFAM.Model.VAFAM_CalloutAsset - so we must look THERE, not in VIS.Model. A bare
           "Class.Method" (no namespace) defaults to the VIS.Model registry. */
        function resolveCalloutCtor(ns, cls) {
            if (!ns || ns.length === 0) {
                return (VIS && VIS.Model && typeof VIS.Model[cls] === "function") ? VIS.Model[cls] : null;
            }
            var path = ns.slice();
            if (path[0] === "VAdvantage" || path[0] === "ViennaAdvantage") path[0] = "VIS";
            var scope = (typeof window !== "undefined") ? window : null;
            for (var j = 0; j < path.length && scope; j++) scope = scope[path[j]];
            return (scope && typeof scope[cls] === "function") ? scope[cls] : null;
        }

        function runRealCallout(line, column, chain) {
            var mTab = makeMTab(line);
            var mField = makeMField(column, line);
            var ctxShim = makeCalloutCtx(line);
            var value = line.values[column];
            for (var i = 0; i < chain.length; i++) {
                try { chain[i].fn.call(chain[i].inst, ctxShim, ($self.windowNo || 0), mTab, mField, value, null); }
                catch (e) { if (window.console) console.log("VAS_074 callout error", e); }
            }
        }

        /* mTab shim: getValue / setValue (+ SetValue alias for a framework typo)
           over line.values; findColumn tells the callout which optional columns
           this panel carries. */
        function makeMTab(line) {
            return {
                getValue: function (col) { return fieldGet(line, col); },
                setValue: function (col, val) { line.values[col] = val; },
                SetValue: function (col, val) { line.values[col] = val; },
                // The panel carries the full C_InvoiceLine column set (columnMeta),
                // so the callout may set any real column.
                findColumn: function (col) { return (columnMeta[col] || PANEL_COLS[col]) ? 0 : -1; },
                // CalloutInvoice reads mTab.getField(col).getDisplayType() / .value and
                // may call field UI mutators - return a GridField shim (was missing,
                // which crashed the callout with "mTab.getField is not a function").
                // Mirror a real GridTab: only ACTUAL tab fields (AD_Field on the window tab)
                // have a GridField; a merged-only table column (e.g. SurchargeAmt, present in
                // columnMeta via MergeAllColumns but not a field on this tab) returns null, so
                // callouts that gate on `getField(col) != null` (e.g. CalloutInvoice's "reset
                // SurchargeAmt to 0") skip it exactly as on the standard invoice window.
                getField: function (col) {
                    var fm = columnMeta[col];
                    return (fm && fm.IsTabField) ? makeFieldShim(line, col) : null;
                },
                // The panel's grid is the C_InvoiceLine tab; CalloutTax.SetTaxExemptReason
                // (and other callouts) branch on the tab's key column.
                getKeyColumnName: function () { return "C_InvoiceLine_ID"; }
            };
        }
        function makeMField(column, line) { return makeFieldShim(line, column); }

        /* Minimal GridField shim backing one column on the line: value / getValue /
           setValue, the dictionary display type, and no-op UI mutators a callout may
           call (setError / setReadOnly / setMandatory / setDisplayed / ...). */
        function makeFieldShim(line, col) {
            var m = columnMeta[col];
            return {
                value: fieldGet(line, col),
                getValue: function () { return fieldGet(line, col); },
                setValue: function (v) { line.values[col] = v; this.value = v; },
                getColumnName: function () { return col; },
                getDisplayType: function () { return (m && m.AD_Reference_ID) || 0; },
                getAD_Reference_ID: function () { return (m && m.AD_Reference_ID) || 0; },
                isMandatory: function () { return !!(m && m.IsMandatory); },
                isReadOnly: function () { return m ? !m.IsUpdateable : false; },
                setError: function () { }, setMandatory: function () { }, setReadOnly: function () { },
                setDisplayed: function () { }, setDisplayLength: function () { },
                setBackgroundColor: function () { }, setInputMandatory: function () { }
            };
        }

        function fieldGet(line, col) {
            if (col === "C_Invoice_ID") return parent.C_Invoice_ID;
            if (col === "QtyInvoiced") {
                var qi = lineVal(line, "QtyInvoiced");
                return (qi == null || qi === "") ? (lineVal(line, "QtyEntered") || 0) : qi;
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
                    case "C_Invoice_ID": return parent.C_Invoice_ID || 0;
                    case "M_Product_ID": return line.values.M_Product_ID || 0;
                    case "C_Charge_ID": return line.values.C_Charge_ID || 0;
                    case "C_UOM_ID": return line.values.C_UOM_ID || 0;
                    case "M_AttributeSetInstance_ID": return line.values.M_AttributeSetInstance_ID || 0;
                    case "M_PriceList_ID": return parent.M_PriceList_ID || 0;
                    case "C_BPartner_ID": return parent.C_BPartner_ID || 0;
                    case "C_BPartner_Location_ID": return parent.C_BPartner_Location_ID || 0;
                    case "AD_Org_ID": return parent.AD_Org_ID || 0;
                    case "AD_Client_ID": return parent.AD_Client_ID || 0;
                    case "M_Warehouse_ID": return parent.M_Warehouse_ID || 0;
                    case "DateInvoiced": return dateStr(parent.DateInvoiced);
                    case "IsSOTrx": return parent.IsSOTrx ? "Y" : "N";
                    default: return (scratch[key] != null) ? scratch[key] : "";
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
            v.PriceEntered = parseNum(v.PriceEntered);
            // The panel's Qty column IS QtyEntered (user-entered, in the entered UOM). The
            // framework callout ALSO writes QtyInvoiced = QtyEntered converted to the product
            // base UOM (the purchasing-UOM multiply). Do NOT copy QtyInvoiced back onto
            // QtyEntered: that replaced the user's qty, and because the next callout (e.g. an
            // attribute change) reconverts, it compounded the multiplication. QtyInvoiced is
            // recomputed server-side on save (MInvoiceLine.beforeSave for PO), so keep it only
            // as a numeric scratch value for any callout that reads it back.
            if (v.QtyInvoiced != null && v.QtyInvoiced !== "") v.QtyInvoiced = parseNum(v.QtyInvoiced);
        }

        /* Refresh display labels + product/charge exclusivity after the callout. */
        function applyCalloutReadback(line) {
            var v = line.values, d = line.display;
            if (v.M_Product_ID > 0) d.chargeName = "";
            else if (v.C_Charge_ID > 0) d.productName = "";
            d.uomName = uomName(v.C_UOM_ID) || d.uomName;
            d.taxName = v.C_Tax_ID > 0 ? (taxName(v.C_Tax_ID) || d.taxName) : "";
            line.dirty = true;
        }
        function uomName(id) { for (var i = 0; i < uomList.length; i++) if (uomList[i].C_UOM_ID === id) return uomList[i].Name; return ""; }
        function taxName(id) { for (var i = 0; i < taxList.length; i++) if (taxList[i].C_Tax_ID === id) return taxList[i].Name; return ""; }

        /* Server fallback: the original CalcLine/RunCallout path, used only when
           the framework callout isn't loaded on the page. */
        function runCalloutServer(line, trigger, done) {
            var v = line.values;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/RunCallout",
                type: "GET", dataType: "json",
                data: {
                    C_Invoice_ID: parent.C_Invoice_ID, TriggerColumn: trigger,
                    M_Product_ID: v.M_Product_ID || 0, C_Charge_ID: v.C_Charge_ID || 0,
                    M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0,
                    QtyEntered: v.QtyEntered || 0, C_UOM_ID: v.C_UOM_ID || 0,
                    PriceEntered: v.PriceEntered || 0, PriceOverride: !!line._priceOverride,
                    C_Tax_ID: v.C_Tax_ID || 0, Discount: v.Discount || 0
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
                if (col === "PriceEntered" && line._priceOverride) continue;
                // Case-insensitive write: a saved line keys columns in DB case (PG lowercases,
                // e.g. "pricelist") which won't match the dictionary-cased "PriceList" the
                // callout returns, so a direct v[col]= would silently drop PriceList etc.
                setLineVal(line, col, vals[col]);
            }
            if (disp.uomName != null) d.uomName = disp.uomName;
            if (disp.taxName != null) d.taxName = disp.taxName;
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
         * line values first, then the invoice header / document context. */
        /* On a "treat as discount" invoice (header C_Invoice.TreatAsDiscount) EVERY line's
           product / attribute set / UOM / quantity are driven by the referenced original
           line (picked via Ref_InvoiceLineOrg_ID in the "..." modal) and must NOT be
           selected / changed directly on the grid - they are copied and locked. */
        var REF_DISCOUNT_LOCKED_COLS = { M_Product_ID: 1, QtyEntered: 1, C_UOM_ID: 1, M_AttributeSetInstance_ID: 1 };
        function isTreatAsDiscount() {
            return !!(parent && parent.TreatAsDiscount);
        }

        function isColumnReadOnly(line, col) {
            // Treat-as-discount invoice: product / ASI / UOM / qty come from the referenced
            // line and are read-only on the grid (checked first so it holds even without
            // column meta).
            if (REF_DISCOUNT_LOCKED_COLS[col] && isTreatAsDiscount()) return true;
            // C_UOM_ID is locked once the line is saved (C_InvoiceLine_ID > 0): the unit of
            // measure must not change on an existing invoice line. Checked before the meta
            // lookup so it holds even when column meta is absent.
            if (col === "C_UOM_ID" && line && line.values && (line.values.C_InvoiceLine_ID || 0) > 0) return true;
            var m = columnMeta[col];
            if (!m) return false;
            if (m.IsReadOnly) return true;
            return !!(m.ReadOnlyLogic && evalLogic(line, m.ReadOnlyLogic));
        }
        var FIELD_COL = { uom: "C_UOM_ID", tax: "C_Tax_ID", quantity: "QtyEntered", price: "PriceEntered", description: "Description" };
        function fieldReadOnly(line, field) {
            var col = FIELD_COL[field];
            return col ? isColumnReadOnly(line, col) : false;
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
           else from the current line then the invoice header. A not-found login token
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
                case "C_Invoice_ID": return String((parent && parent.C_Invoice_ID) || 0);
                case "M_PriceList_ID": return String((parent && parent.M_PriceList_ID) || 0);
                case "C_BPartner_ID": return String((parent && parent.C_BPartner_ID) || 0);
                case "C_BPartner_Location_ID": return String((parent && parent.C_BPartner_Location_ID) || 0);
                case "C_Currency_ID": return String((parent && parent.C_Currency_ID) || 0);
                case "AD_Org_ID": return String((parent && parent.AD_Org_ID) || 0);
                case "AD_Client_ID": return String((parent && parent.AD_Client_ID) || 0);
                case "IsSOTrx": return (parent && parent.IsSOTrx) ? "Y" : "N";
                case "IsTaxIncluded": return (parent && parent.IsTaxIncluded) ? "Y" : "N";
                // Header flag kept in context so a line field's DisplayLogic / ReadOnlyLogic
                // (e.g. @TreatAsDiscount@='Y' on the reference group) resolves correctly - it
                // is NOT a C_InvoiceLine column, so it never comes from line.values.
                case "TreatAsDiscount": return (parent && parent.TreatAsDiscount) ? "Y" : "N";
                case "Processed": return (parent && parent.Processed) ? "Y" : "N";
                case "DocStatus": return (parent && parent.DocStatus) || "";
                default: return "";
            }
        }
        /* Substitute @[$#]Token@; an unresolved token is left as-is so the tuple can
           detect it and apply the caller's default. */
        function resolveLogicTokens(line, s) {
            return String(s).replace(/@([#$\w]+)@/g, function (whole, k) {
                var val = logicCtxVal(line, k);
                return (val === null || val === undefined) ? whole : val;
            });
        }
        /* Evaluate one comparison; an unresolved token (still contains '@') yields
           <dflt> so display logic can default to visible and read-only to editable. */
        function evalLogicTuple(line, tuple, dflt) {
            tuple = (tuple || "").trim();
            if (!tuple) return true;
            var m = tuple.match(/^(.*?)(=|!|\^|<|>)(.*)$/);
            if (!m) return true;
            var lv = resolveLogicTokens(line, m[1]).trim().replace(/^['"]|['"]$/g, "");
            var rv = resolveLogicTokens(line, m[3]).trim().replace(/^['"]|['"]$/g, "");
            if (lv.indexOf("@") >= 0 || rv.indexOf("@") >= 0) return dflt;   // unresolved
            var ln = parseFloat(lv), rn = parseFloat(rv);
            var numeric = lv !== "" && rv !== "" && !isNaN(ln) && !isNaN(rn);
            switch (m[2]) {
                case "=": return numeric ? ln === rn : lv === rv;
                case "!": case "^": return numeric ? ln !== rn : lv !== rv;
                case "<": return numeric ? ln < rn : lv < rv;
                case ">": return numeric ? ln > rn : lv > rv;
            }
            return true;
        }
        /* <dflt> (default false) is used for any tuple whose tokens can't be resolved. */
        function evalLogic(line, logic, dflt) {
            if (dflt === undefined) dflt = false;
            var parts = String(logic).match(/[^&|]+|[&|]/g);
            if (!parts) return dflt;
            var result = null, op = null;
            for (var i = 0; i < parts.length; i++) {
                var t = parts[i].trim();
                if (t === "&") { op = "&"; continue; }
                if (t === "|") { op = "|"; continue; }
                if (!t) continue;
                var val = evalLogicTuple(line, t, dflt);
                if (result === null) result = val;
                else if (op === "|") result = result || val;
                else result = result && val;
            }
            return result === null ? dflt : result;
        }

        /* ---------- dynamic "more" fields (tab fields not on the panel grid) ----------
         * Renders the C_InvoiceLine tab fields that are NOT one of the fixed panel
         * columns as proper controls in the "..." popover, by AD_Reference_ID. Each
         * honours read-only logic (isColumnReadOnly), runs the column callout on change
         * when one is configured, enforces AD_Val_Rule (FK lookups, server-side) and is
         * covered by the mandatory check in validateLine. Values live in line.values and
         * persist through the existing generic column save. */
        /* ---------- VAFAM asset-related business rule (VAFAM module) ----------
         * When a line is flagged Asset Related (VAFAM_IsAssetRelated = Y):
         *   - Capital Expense (VAFAM_CapitalExpense) becomes MANDATORY (red asterisk +
         *     enforced in validateLine).
         * When the flag is cleared back to N:
         *   - Capital Expense AND Asset (A_Asset_ID) are cleared (and marked touched so the
         *     nulls persist on save), and their controls are rebuilt to show the cleared state.
         */
        var VAFAM_ASSET_RELATED_COL = "VAFAM_IsAssetRelated";
        var VAFAM_CAPITAL_EXPENSE_COL = "VAFAM_CapitalExpense";
        var VAFAM_ASSET_ID_COL = "A_Asset_ID";

        /* Is this line currently flagged Asset Related? (YesNo, read case-insensitively.) */
        function isAssetRelated(line) {
            var v = lineVal(line, VAFAM_ASSET_RELATED_COL);
            return (v === true || v === "Y" || v === "true" || v === 1 || v === "1");
        }

        /* Effective mandatory flag for a curated modal field: the dictionary IsMandatory,
           OR the conditional rule that Capital Expense is mandatory while the line is Asset
           Related. Used both for the control's asterisk and for validateLine. */
        function dynMandatory(line, m) {
            if (m.IsMandatory) return true;
            if (m.ColumnName === VAFAM_CAPITAL_EXPENSE_COL) return isAssetRelated(line);
            return false;
        }

        /* Clear a curated modal field's value (to null), drop its cached FK label, and mark
           it dirty + touched so the intentional null persists on save (ApplyExtraColumns). */
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

        /* Rebuild one curated field's control in the open modal (preserving field order) so a
           value clear / mandatory-asterisk toggle shows immediately. No-op when the field's
           modal isn't open or the column isn't present. */
        function rebuildDynField(line, col) {
            var $b = $("#vasCilMoreBody");
            if (!$b.length || morePopoverFor !== line.rowId) return;
            var m = columnMeta[col];
            if (!m) return;
            var $old = $b.children('[data-col="' + col + '"]');
            if (!$old.length) return;
            $old.replaceWith(buildDynField(line, m));
            applyDynDisplay(line, $b);
        }

        /* Apply the asset-related rule after VAFAM_IsAssetRelated changes: clear the dependent
           fields when no longer related, then rebuild both dependent controls (Capital Expense's
           asterisk toggles with the flag; Asset may have just been cleared). */
        function applyAssetRelatedRule(line) {
            if (!isAssetRelated(line)) {
                clearDynValue(line, VAFAM_CAPITAL_EXPENSE_COL);
                clearDynValue(line, VAFAM_ASSET_ID_COL);
            }
            rebuildDynField(line, VAFAM_CAPITAL_EXPENSE_COL);
            rebuildDynField(line, VAFAM_ASSET_ID_COL);
        }

        /* First visible, editable, (conditionally-)mandatory curated field that is still empty -
           {col, name, msg} or null. Covers dictionary-mandatory fields AND the conditional rule
           (Capital/Expense on an Asset-Related line). Shared by validateLine (Save) and the
           "..." modal's close guard so both enforce the same requirement. */
        function firstMissingDynMandatory(line) {
            var dyn = additionalInfoColumns(line);
            for (var d = 0; d < dyn.length; d++) {
                var dm = dyn[d];
                // A field hidden by DisplayLogic can't be set by the user - don't require it.
                if (!dynFieldVisible(line, dm)) continue;
                if (!dynMandatory(line, dm) || isColumnReadOnly(line, dm.ColumnName)) continue;
                var dv = lineVal(line, dm.ColumnName);
                var isFk = (dm.AD_Reference_ID === 18 || dm.AD_Reference_ID === 19 || dm.AD_Reference_ID === 30 || dm.AD_Reference_ID === 13);
                var empty = isFk ? !(+dv > 0) : (dv == null || String(dv).trim() === "");
                if (empty) return { col: dm.ColumnName, name: dm.Name || dm.ColumnName, msg: lbl("VAS_074_FieldRequired", "Required") + ": " + (dm.Name || dm.ColumnName) };
            }
            return null;
        }

        /* Curated "Additional Info" columns shown in the "..." modal, in this order.
           To add a field, append its column name here (with an optional `when`
           condition). A column missing from the dictionary (e.g. a module's columns
           when the module isn't installed) is silently skipped. */
        var ADDITIONAL_INFO_FIELDS = [
            { col: "AD_OrgTrx_ID" },
            { col: "C_Project_ID" },
            { col: "C_Campaign_ID" },
            { col: "C_Activity_ID" },
            { col: "C_Withholding_ID" },
            { col: "WithholdingAmt" },
            { col: "C_RevenueRecognition_ID", when: "svcExpenseOrCharge" },
            { col: "RevenueStartDate", when: "svcExpenseOrCharge" },
            { col: "VAFAM_IsAssetRelated", when: "vafam" },
            { col: "VAFAM_CapitalExpense", when: "vafam" },
            { col: "A_Asset_ID", when: "vafam" },
            { col: "VA106_TaxCollectedAtSource_ID", when: "va106_" },
            { col: "VA106_TCSAmount", when: "va106_" },
            // "Treat as Discount Reference" group - only when the invoice header's
            // TreatAsDiscount flag is set (AP credit-memo treated as a discount). Picking
            // Ref_InvoiceLineOrg_ID copies the referenced line's product / ASI / UOM / qty
            // onto this line (see setDyn -> applyRefLineDetail).
            { col: "Ref_InvoiceOrg_ID", when: "treatasdiscount" },
            { col: "Ref_InvoiceLineOrg_ID", when: "treatasdiscount" },
            { col: "M_Warehouse_ID", when: "treatasdiscount" }
        ];

        /* Whether a conditional group applies to this line. */
        function dynCondMet(line, when) {
            if (!when) return true;
            if (when === "svcExpenseOrCharge") {
                if (line.values.C_Charge_ID > 0) return true;          // charge line
                var pt = line._productType || "";
                return pt === "S" || pt === "E";                        // Service / Expense product
            }
            if (when === "vafam") return !!columnMeta["VAFAM_IsAssetRelated"];   // module installed
            // Treat-as-discount reference group: gated by the invoice header flag (kept in
            // parent context on load). The columns must also exist in the dictionary
            // (additionalInfoColumns already drops any missing column meta).
            if (when === "treatasdiscount") return !!(parent && parent.TreatAsDiscount);
            // VA106 (Tax Collected at Source) - shown only when the module is installed
            // (the TCS column is present in the dictionary). The field's own
            // AD_Field.DisplayLogic (e.g. sales-only) is still applied separately.
            if (when === "va106_") return !!columnMeta["VA106_TaxCollectedAtSource_ID"];
            return true;
        }

        /* Curated CANDIDATE column metas for this line (skips missing columns + unmet
           `when` conditions - module presence / product type - keeps list order). NOTE:
           DisplayLogic is NOT applied here: the field is still BUILT, and its show/hide is
           decided AFTER buildDynField by applyDynDisplay (per the design - build the control
           first, then apply display logic), so a control keeps its state and just toggles
           visibility instead of being dropped/rebuilt. */
        function additionalInfoColumns(line) {
            var out = [];
            for (var i = 0; i < ADDITIONAL_INFO_FIELDS.length; i++) {
                var spec = ADDITIONAL_INFO_FIELDS[i];
                var m = columnMeta[spec.col];
                if (!m) continue;
                if (!dynCondMet(line, spec.when)) continue;
                out.push(m);
            }
            return out;
        }

        /* Does any curated "Additional Info" field carry a value? Used to tint the row's
           "..." button blue so the user can see a line has extra data without opening the
           modal. Empty FK/number = 0, blank text/date, No checkbox all count as "no value". */
        function hasAdditionalInfo(line) {
            var cols = additionalInfoColumns(line);
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

        /* Whether a built field is visible per its AD_Field.DisplayLogic. Default SHOW when
           the logic is empty or a token can't be resolved ($Element_record tokens). */
        function dynFieldVisible(line, m) {
            return !(m && m.DisplayLogic && !evalLogic(line, m.DisplayLogic, true));
        }

        /* Apply DisplayLogic to the ALREADY-BUILT modal fields: toggle each field's
           visibility (display:none via .vas-cil-dyn-hidden) instead of adding/removing DOM,
           so a control - and its native lookup - is preserved across toggles. Returns the
           number of currently-visible fields (so the caller can show the empty message). */
        function applyDynDisplay(line, $body) {
            var visible = 0;
            $body.children("[data-col]").each(function () {
                var m = columnMeta[$(this).attr("data-col")];
                var show = dynFieldVisible(line, m);
                $(this).toggleClass("vas-cil-dyn-hidden", !show);
                if (show) visible++;
            });
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
            // No overflow clip on the body - an FK dropdown would be cut off.
        }

        /* ---------- collapsible field groups (Additional Info modal) ----------
           Reusable full-row section headers injected before an anchor field. A group owns
           the sibling fields between its header and the next header; the header's
           Show More/Less toggle collapses/expands them. Add an object here to place another
           group anywhere in the modal: `anchor` = the ColumnName of the group's first field
           (the header is inserted before its [data-col] wrapper), `key`/`def` = the group
           title (AD_Message key + English fallback), `collapsed` = initial state. */
        var MORE_FIELD_GROUPS = [
            { anchor: "AD_OrgTrx_ID", key: "VAS_074_GrpDimension", def: "Dimension", collapsed: false },
            { anchor: "C_Withholding_ID", key: "VAS_074_GrpReferences", def: "References", collapsed: false },
            { anchor: "Ref_InvoiceOrg_ID", key: "VAS_074_GrpTreatAsDiscount", def: "Treat as Discount Reference", collapsed: false }
        ];
        // Per-anchor collapsed state; persists across refreshMoreDialog and re-opens so a
        // user's expand/collapse choice survives value-change reconciles within the session.
        var moreGroupCollapsed = {};

        // Idempotently (re)insert every group header before its anchor field, then apply the
        // collapse state. Safe to call after any (re)build of #vasCilMoreBody.
        function applyFieldGroups($body) {
            if (!$body || !$body.length) return;
            $body.children(".vas-cil-fldgrp").remove();   // drop prior headers first
            for (var g = 0; g < MORE_FIELD_GROUPS.length; g++) {
                var grp = MORE_FIELD_GROUPS[g];
                var $anchor = $body.children('[data-col="' + grp.anchor + '"]');
                if (!$anchor.length) continue;            // group's lead field absent -> skip
                var collapsed = (grp.anchor in moreGroupCollapsed) ? moreGroupCollapsed[grp.anchor] : !!grp.collapsed;
                $anchor.before(
                    '<div class="vas-cil-fldgrp" data-grp="' + grp.anchor + '"' + (collapsed ? ' data-collapsed="1"' : "") + '>' +
                    '<span class="vas-cil-fldgrp-name">' + esc(lbl(grp.key, grp.def)) + "</span>" +
                    '<button type="button" class="vas-cil-fldgrp-toggle" data-act="fldgrp-toggle">' +
                    '<span class="vas-cil-fldgrp-txt">' + esc(collapsed ? lbl("VAS_074_ShowMore", "Show More") : lbl("VAS_074_ShowLess", "Show Less")) + "</span>" +
                    '<svg class="vas-cil-fldgrp-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 15 12 9 18 15"/></svg>' +
                    "</button></div>");
            }
            applyGroupCollapse($body);
        }

        // Hide/show each group's members (the wrappers between its header and the next
        // header) per the header's collapsed state. Never force-shows a DisplayLogic-hidden
        // field - .vas-cil-dyn-hidden stays authoritative; we only add/remove our own class.
        function applyGroupCollapse($body) {
            $body.children(".vas-cil-fldgrp").each(function () {
                var $hdr = $(this);
                $hdr.nextUntil(".vas-cil-fldgrp").toggleClass("vas-cil-grp-collapsed", $hdr.attr("data-collapsed") === "1");
            });
        }

        // Flip one group's collapsed state (from its Show More/Less button).
        function toggleFieldGroup($hdr) {
            if (!$hdr || !$hdr.length) return;
            var collapsed = $hdr.attr("data-collapsed") !== "1";
            $hdr.attr("data-collapsed", collapsed ? "1" : "0");
            moreGroupCollapsed[$hdr.attr("data-grp")] = collapsed;
            $hdr.find(".vas-cil-fldgrp-txt").text(collapsed ? lbl("VAS_074_ShowMore", "Show More") : lbl("VAS_074_ShowLess", "Show Less"));
            applyGroupCollapse($hdr.closest("#vasCilMoreBody"));
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
            // Mandatory field (dictionary IsMandatory OR the conditional Capital/Expense-on-
            // Asset-Related rule): colour its label with the framework mandatory colour so the
            // requirement reads at a glance. Recomputed on every (re)build, so it toggles with
            // the Asset-Related flag.
            $field.toggleClass("vas-cil-dyn-mandatory", dynMandatory(line, m));
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
        function getCachedMLookUp(vctx, windowNo, adColumnId, displayType) {
            var key = (adColumnId || 0) + "_" + displayType;
            if (!_mlookupCache[key]) {
                _mlookupCache[key] = VIS.MLookupFactory.getMLookUp(vctx, windowNo, adColumnId, displayType);
            }
            return _mlookupCache[key];
        }

        /* Prime the framework window context (VIS.context @ this panel's windowNo) with the
           invoice HEADER + the CURRENT line's column values, so a modal FK control's
           AD_Val_Rule resolves its @tokens@ against THIS line before the MLookup loads.
           Without this the panel never populates the line-tab context (it bypasses the
           framework GridTab), so a validated lookup like C_Withholding_ID parses its
           validation against empty tokens (e.g. `... AND C_BPartner_ID=`) and loads no
           data. A validated MLookup caches its list at first load (getData2 refreshes only
           when !allLoaded), so this MUST run before the control is built - callers prime
           right before appendDynFields / refreshMoreDialog / on a value change (setDyn).
           Header keys are set first, then the line's own values (current-row wins);
           login / accounting-element tokens (parent.LoginContext, @#Tok@ / @$Tok@) are set
           as #global context so element-gated rules resolve too. */
        function primeLineContext(line) {
            if (!window.VIS) return;
            var ctx = VIS.context || (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx());
            if (!ctx || typeof ctx.setContext !== "function") return;
            var wn = $self.windowNo || 0;
            // Stage into a case-insensitive bag FIRST, then write each column exactly once.
            // The framework merges this window context into a case-INSENSITIVE dictionary on
            // the server, so emitting a DB-cased line key (e.g. "c_invoice_id") alongside a
            // dictionary-cased one ("C_Invoice_ID") would collide -> "same key already added".
            // Keying by lower-case (canonical dictionary name preferred) collapses variants;
            // line values are staged AFTER the header so the current-row value wins.
            var bag = {};   // lc -> { name, value }
            function coerce(val) {
                if (val === true || val === false) return val ? "Y" : "N";
                if (val instanceof Date) return dateStr(val);
                if (val == null) return "";
                return String(val);
            }
            function stage(col, val) {
                if (col == null) return;
                var lc = String(col).toLowerCase();
                // Canonicalise to the dictionary's ColumnName so the key matches whatever the
                // framework already set for this window (also dictionary-cased).
                var name = columnNameByLc[lc] || col;
                bag[lc] = { name: name, value: coerce(val) };
            }
            if (parent) {
                stage("C_Invoice_ID", parent.C_Invoice_ID || 0);
                stage("C_BPartner_ID", parent.C_BPartner_ID || 0);
                stage("C_BPartner_Location_ID", parent.C_BPartner_Location_ID || 0);
                stage("M_PriceList_ID", parent.M_PriceList_ID || 0);
                stage("C_Currency_ID", parent.C_Currency_ID || 0);
                stage("AD_Org_ID", parent.AD_Org_ID || 0);
                stage("AD_Client_ID", parent.AD_Client_ID || 0);
                stage("M_Warehouse_ID", parent.M_Warehouse_ID || 0);
                stage("DateInvoiced", parent.DateInvoiced);
                stage("IsSOTrx", !!parent.IsSOTrx);
                stage("IsTaxIncluded", !!parent.IsTaxIncluded);
                stage("TreatAsDiscount", !!parent.TreatAsDiscount);   // treat-as-discount ref pickers' val rules
                stage("Processed", !!parent.Processed);
                stage("DocStatus", parent.DocStatus || "");
            }
            // Current line's own C_InvoiceLine columns (current-row context) - staged last.
            var v = line.values || {};
            for (var k in v) { if (v.hasOwnProperty(k)) stage(k, v[k]); }
            // Organization for a line's lookups / AD_Val_Rule must be the INVOICE's org, not
            // the login org. A new/unsaved line carries AD_Org_ID = null (seedAllColumns),
            // which the loop above would stage as "" - clobbering the header org and making a
            // validated framework lookup fall back to the login organization. Re-assert the
            // invoice org last so @AD_Org_ID@ resolves to the invoice's org.
            if (parent && (parent.AD_Org_ID || 0) > 0) stage("AD_Org_ID", parent.AD_Org_ID);
            // Write each column once.
            for (var key in bag) {
                if (!bag.hasOwnProperty(key)) continue;
                try { ctx.setContext(wn, bag[key].name, bag[key].value); } catch (e) { }
            }
            // Login / accounting-element tokens (@#Global@ / @$Element_*@) as #global context.
            // These live in a separate namespace (own prefix), so no case-merge concern here.
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
            catch (e) { if (window.console) console.log("VAS_074 vienna ctrl error " + col, e); return null; }
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
            } catch (e) { if (window.console) console.log("VAS_074 vienna wrap error " + col, e); return null; }
        }

        /* Case-insensitive read of a line value - the DB result may key columns in a
           different case than the dictionary ColumnName (PostgreSQL lowercases unquoted
           identifiers, Oracle uppercases), so a direct line.values[col] can miss. */
        function lineVal(line, col) {
            var v = line.values;
            if (v[col] !== undefined) return v[col];
            var lc = String(col).toLowerCase();
            for (var k in v) { if (v.hasOwnProperty(k) && k.toLowerCase() === lc) return v[k]; }
            return undefined;
        }
        /* Case-insensitive write - update the existing (DB-cased) key when present so a
           change doesn't leave a stale duplicate that could win on save. */
        function setLineVal(line, col, value) {
            var v = line.values;
            if (v[col] === undefined) {
                var lc = String(col).toLowerCase();
                for (var k in v) { if (v.hasOwnProperty(k) && k.toLowerCase() === lc) { v[k] = value; return; } }
            }
            v[col] = value;
        }

        /* True when the line's product actually carries an attribute set (M_AttributeSet_ID > 0).
           Reads the raw server flag VASCILDISP_HasAttrSet (= COALESCE(p.M_AttributeSet_ID, 0))
           off the line's value bag, case-insensitively via lineVal - a saved line's keys are
           DB-cased (PostgreSQL lowercases the alias to "vascildisp_hasattrset", Oracle uppercases
           it). This is authoritative: unlike line.display.hasAttributeSet it is NOT OR'd with an
           existing ASI description, so a line whose product no longer has an attribute set (but
           still carries an old ASI, so AttrName is set) correctly reports false. Falls back to the
           display flag when the raw column isn't on the line - e.g. a brand-new (unsaved) line,
           which has no server projection but carries the pure product flag in display. */
        function productHasAttributeSet(line) {
            var raw = lineVal(line, "VASCILDISP_HasAttrSet");
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
                $l.append($('<option value=""></option>').text(lbl("VAS_074_SelectOption", "Select")));
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
                markDirty(line);   // genuine change only
                // Remember every modal field the user actually edited so a CLEAR (null / 0 /
                // empty) persists on save. The save bag carries the whole column set, so the
                // server can't tell a user-cleared FK/dimension from a naturally-empty column;
                // TouchedCols tells it which nulls/zeros are intentional (see ApplyExtraColumns).
                if (!line._dynTouched) line._dynTouched = {};
                line._dynTouched[col] = true;
            }
            // VAFAM asset-related rule: toggling Asset Related makes Capital Expense mandatory
            // (asterisk) or clears Capital Expense + Asset. Runs regardless of any column callout.
            if (String(col).toLowerCase() === VAFAM_ASSET_RELATED_COL.toLowerCase()) applyAssetRelatedRule(line);
            // Treat-as-discount: picking the referenced original invoice line copies its
            // product / ASI / UOM / qty onto this line (then locks them). Fires on a genuine
            // change to a real line id, independent of any AD_Column.Callout on the column.
            if (!sameVal(prev, value) && String(col).toLowerCase() === "ref_invoicelineorg_id") {
                var refLineId = parseInt(value, 10) || 0;
                if (refLineId > 0) fetchRefLineDetail(line, refLineId);
            }
            clearMoreDialogError();   // any field change dismisses the blocking close-error banner
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

        /* ---------- treat-as-discount reference sync ----------
           When the user picks Ref_InvoiceLineOrg_ID, fetch the referenced original invoice
           line and copy its product / attribute-set / UOM / entered quantity onto this line
           so the discount line stays consistent with the line it references. On a treat-as-
           discount invoice these fields are read-only anyway (isTreatAsDiscount -> isColumnReadOnly). */
        function fetchRefLineDetail(line, refLineId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetRefInvoiceLineDetail",
                type: "GET", dataType: "json", data: { C_InvoiceLine_ID: refLineId },
                success: function (raw) {
                    showBusy(false);
                    // still on the same reference the user just picked?
                    if ((parseInt(lineVal(line, "Ref_InvoiceLineOrg_ID"), 10) || 0) !== refLineId) return;
                    var d = (typeof raw === "string") ? (raw ? jQuery.parseJSON(raw) : null) : raw;
                    if (!d) { showToast(lbl("VAS_074_RefLineNotFound", "Referenced invoice line not found")); return; }
                    applyRefLineDetail(line, d);
                },
                error: function (e) { console.log(e); showBusy(false); }
            });
        }

        /* Copy the referenced line's product / ASI / UOM / qty onto this line (values +
           display), mark dirty, and repaint the grid row + open modal. Pricing is NOT
           recomputed - the user enters the discount amount manually. The core columns
           (M_Product_ID / M_AttributeSetInstance_ID / C_UOM_ID / QtyEntered) persist via
           buildRowPayload's top-level fields, so no _dynTouched flag is needed for them. */
        function applyRefLineDetail(line, d) {
            var v = line.values, disp = line.display || (line.display = {});
            var asi = parseInt(d.M_AttributeSetInstance_ID, 10) || 0;
            v.M_Product_ID = parseInt(d.M_Product_ID, 10) || 0;
            v.C_Charge_ID = 0;
            v.M_AttributeSetInstance_ID = asi;
            var uom = parseInt(d.C_UOM_ID, 10) || 0;
            if (uom > 0) v.C_UOM_ID = uom;
            v.QtyEntered = parseNum(d.QtyEntered);
            disp.productName = d.ProductName || "";
            disp.chargeName = "";
            disp.attrName = d.AttrName || "";
            disp.hasAttributeSet = asi > 0;
            if (d.UomName) disp.uomName = d.UomName;
            line._productType = d.ProductType || "";
            markDirty(line);
            render();   // product / qty / uom now shown locked
            if (morePopoverFor === line.rowId) refreshMoreDialog(line);
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
            var $b = $("#vasCilMoreBody");
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
            var $b = $("#vasCilMoreBody");
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
            $b.children(".vas-cil-empty-message").remove();
            if (!visible)
                $b.append('<p class="vas-cil-empty-message">' + esc(lbl("VAS_074_NoAdditionalInfo", "No additional info for this line")) + "</p>");
            applyFieldGroups($b);   // re-place headers after the nodes were reordered
        }

        /* Resolve and cache an FK value's display label (existing value caption). */
        function ensureRefLabel(line, m, $input) {
            var col = m.ColumnName, v = lineVal(line, col);
            if (!v || +v <= 0) return;
            if (line._dynDisp && line._dynDisp[col] != null) { $input.val(line._dynDisp[col]); return; }
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetRefLookup",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ C_Invoice_ID: parent.C_Invoice_ID, ColumnName: col, Id: +v }) },
                success: function (raw) {
                    var rows = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw; rows = rows || [];
                    if (rows.length) { setDynDisplay(line, col, rows[0].Name); if ($input && $input.closest("body").length) $input.val(rows[0].Name); }
                },
                error: function () { }
            });
        }

        function buildFkControl(line, m) {
            var col = m.ColumnName;
            var $wrap = $('<div class="vas-cil-fk" style="position:relative"></div>');
            var $i = $('<input type="text" class="vas-cil-field__input" autocomplete="off" />').val(dynDisplay(line, m));
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
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetRefLookup",
                type: "POST", dataType: "json",
                data: { payload: JSON.stringify({ C_Invoice_ID: parent.C_Invoice_ID, ColumnName: m.ColumnName, Query: term, PageSize: CATALOG_PAGE_SIZE, Offset: 0, RowValues: compactCtx(line.values) }) },
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
            fk.$pop = $('<div class="vas-cil-catalog-popover vas-cil-fk-popover"></div>');
            if (!rows.length) {
                fk.$pop.html('<div class="vas-cil-catalog__hint">' + esc(lbl("VAS_074_NoMatches", "No matches")) + "</div>");
            } else {
                for (var i = 0; i < rows.length; i++) {
                    fk.$pop.append($('<button type="button" class="vas-cil-catalog-popover__item" data-fk-item="true"></button>')
                        .text(rows[i].Name).attr("data-id", rows[i].Id).attr("data-name", rows[i].Name));
                }
            }
            fk.$pop.on("mousedown", ".vas-cil-catalog-popover__item", function (e) {
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
            // A line is identified by exactly one of product / charge.
            if (v.M_Product_ID <= 0 && v.C_Charge_ID <= 0)
                return { col: "M_Product_ID", msg: lbl("VAS_074_ProductChargeRequired", "Select a product or charge") };
            // Mandatory columns from AD_Column metadata.
            var checks = [
                { col: "QtyEntered", empty: !(v.QtyEntered > 0) },
                { col: "C_UOM_ID", empty: !(v.C_UOM_ID > 0) },
                { col: "PriceEntered", empty: (v.PriceEntered == null || v.PriceEntered === "") },
                { col: "C_Tax_ID", empty: !(v.C_Tax_ID > 0) },
                { col: "Description", empty: !(v.Description && String(v.Description).trim()) }
            ];
            for (var i = 0; i < checks.length; i++) {
                if (isMandatory(checks[i].col) && checks[i].empty)
                    return { col: checks[i].col, msg: lbl("VAS_074_FieldRequired", "Required") + ": " + checks[i].col };
            }
            // Qty must be positive whenever a line carries a product/charge.
            if (!(v.QtyEntered > 0))
                return { col: "QtyEntered", msg: lbl("VAS_074_QtyRequired", "Quantity must be greater than zero") };
            // Mandatory dynamic ("...") fields (skip read-only / hidden - the user can't set
            // them). Includes the conditional Capital/Expense-on-Asset-Related rule.
            var miss = firstMissingDynMandatory(line);
            if (miss) return { col: miss.col, msg: miss.msg };
            return null;
        }

        /* Validate every unsaved line and record its error ON THE LINE (line._error) so the
           row renders its own red message - instead of a single toast that only named the
           first failure. Repaints so every offending record in a multi-row save is flagged
           in place; returns false when any line is invalid. */
        function validateUnsaved(batch) {
            // Validate only the rows about to be saved (batch) - a deferred flush must not be
            // blocked by an unrelated, still-being-entered line that wasn't Save-clicked.
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
                var $first = $linesBody.find(".vas-cil-row--line.is-invalid").first();
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
            // raw VASCILDISP_HasAttrSet flag off the line (case-insensitive), not the AttrName-
            // conflated display flag - see productHasAttributeSet.
            if (!productHasAttributeSet(line)) return;
            closeDialogs();
            VIS.AttributeControl.open({
                M_Product_ID: line.values.M_Product_ID,
                M_AttributeSetInstance_ID: line.values.M_AttributeSetInstance_ID,
                productName: line.display.productName,
                // true -> open straight on the New-attribute form; false -> instance list.
                // Set this per your own requirement.
                newAttribute: true,
                // Default state of the instance list's "Show All (include zero and (-ve) qty)"
                // checkbox for THIS screen. true -> show all; false -> only QtyOnHand > 0.
                showAll: true,
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
                // Picker dismissed without choosing an attribute -> don't leave focus stranded
                // on the removed dialog; continue the row's tab chain on the next column (the
                // description field - the same landing spot as a product with no attribute set).
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
                        out.push({
                            code: val.Code || val.Name, label: val.Name, spec: attr.Name, priceDelta: "—", availability: "—",
                            M_Attribute_ID: attr.M_Attribute_ID, M_AttributeValue_ID: val.M_AttributeValue_ID
                        });
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
            var backdrop = $('<div class="vas-cil-dialog-backdrop" id="vasCilAttr"></div>');
            var dialog = $('<div class="vas-cil-dialog vas-cil-dialog--wide"></div>');
            dialog.html(
                '<header class="vas-cil-dialog__header">' +
                '<div class="vas-cil-dialog__header-row"><h3 class="vas-cil-dialog__title" id="vasCilAttrTitle">' + esc(lbl("VAS_074_SelectAttribute", "Select attribute")) + "</h3>" +
                '<button type="button" class="vas-cil-btn vas-cil-btn--outline-pill vas-cil-is-hidden" data-act="attr-back">' + icon("arrow-left", "←") + "<span>" + esc(lbl("VAS_074_Back", "Back")) + "</span></button>" +
                (attrState.info && attrState.info.IsCanCreate ?
                    '<button type="button" class="vas-cil-btn vas-cil-btn--outline-pill" data-act="attr-create">' + icon("plus", "+") + "<span>" + esc(lbl("VAS_074_NewAttribute", "New attribute")) + "</span></button>" : "") + "</div>" +
                '<div class="vas-cil-dialog__type"><span class="vas-cil-badge vas-cil-badge--product">' + esc(lbl("VAS_074_Product", "Product")) + '</span><p class="vas-cil-dialog__primary-name">' + esc(attrState.product) + "</p></div>" +
                '<div class="vas-cil-search-input" id="vasCilAttrSearchRow">' + icon("search", "🔍") + '<input type="text" id="vasCilAttrSearch" placeholder="' + esc(lbl("VAS_074_AttrSearch", "Search attribute values")) + '" /></div>' +
                "</header>" +
                '<div class="vas-cil-dialog__body vas-cil-dialog__body--fixed">' +
                '<div id="vasCilAttrList"' + (attrState.info && attrState.info.IsCanEdit ? ' class="vas-cil-attr-grid--editable"' : "") + '><div class="vas-cil-attr-grid__head"><div></div><div>' + esc(lbl("VAS_074_Code", "Code")) + "</div><div>" + esc(lbl("Description", "Description")) +
                "</div><div>" + esc(lbl("GuaranteeDate", "Guarantee Date")) + "</div><div>" + esc(lbl("M_Locator_ID", "Locator")) + '</div><div class="vas-cil-attr-h-right">' + esc(lbl("QtyOnHand", "On Hand")) + "</div>" +
                (attrState.info && attrState.info.IsCanEdit ? "<div>" + esc(lbl("VAS_074_Edit", "Edit")) + "</div>" : "") +
                '</div><div class="vas-cil-attr-grid__body" id="vasCilAttrRows"></div></div>' +
                '<div id="vasCilAttrCreate" class="vas-cil-is-hidden">' + attrCreateForm() + "</div>" +
                "</div>" +
                '<footer class="vas-cil-dialog__footer vas-cil-dialog__footer--end">' +
                '<div id="vasCilAttrListFoot">' +
                '<div class="vas-cil-attr-pager"><button type="button" class="vas-cil-attr-pagebtn" data-act="attr-prev" aria-label="' + esc(lbl("VAS_074_Prev", "Previous")) + '">' + icon("chevron-left", "‹") + '</button>' +
                '<span class="vas-cil-attr-pageinfo" id="vasCilAttrPageInfo"></span>' +
                '<button type="button" class="vas-cil-attr-pagebtn" data-act="attr-next" aria-label="' + esc(lbl("VAS_074_Next", "Next")) + '">' + icon("chevron-right", "›") + "</button></div>" +
                '<button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="attr-ok">' + esc(lbl("VAS_074_OK", "OK")) + "</button></div>" +
                '<div id="vasCilAttrCreateFoot" class="vas-cil-is-hidden"><button type="button" class="vas-cil-btn vas-cil-btn--ghost" data-act="attr-cancel">' + esc(lbl("VAS_074_Cancel", "Cancel")) +
                '</button><button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="attr-submit" disabled>' + esc(lbl("VAS_074_AddAttribute", "Add attribute")) + "</button></div></footer>");
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
            dialog.find("#vasCilAttrSearch").on("input", function () { attrState.search = $(this).val(); attrState.page = 0; renderAttrRows(); });
            renderAttr();
            setTimeout(function () { dialog.find("#vasCilAttrSearch").focus(); }, 0);
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
            var html = '<div class="vas-cil-attr-form vas-cil-more-grid">';
            var any = false;
            var attrs = (info.Attributes || []).filter(function (a) { return a.IsInstanceAttribute; });
            for (var i = 0; i < attrs.length; i++) {
                var a = attrs[i];
                any = true;
                var id = "vasCilAttrF_" + a.M_Attribute_ID;
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
            if (info.IsLot) { any = true; html += attrField(attrTextInput("vasCilAttrLot", true), lbl("VAS_074_Lot", "Lot"), attrFieldBtn("attr-newlot", lbl("VAS_074_NewLot", "New")), false); }
            if (info.IsSerNo) { any = true; html += attrField(attrTextInput("vasCilAttrSerNo", true), lbl("VAS_074_SerialNo", "Serial No"), attrFieldBtn("attr-genserno", lbl("VAS_074_Generate", "Generate")), false); }
            if (info.IsGuaranteeDate) { any = true; html += attrField('<input type="date" id="vasCilAttrGuarantee" placeholder=" " data-placeholder="" />', lbl("VAS_074_GuaranteeDate", "Guarantee Date"), null, false); }
            html += "</div>";
            if (!any) html = '<p class="vas-cil-empty-message">' + esc(lbl("VAS_074_NoInstanceAttr", "No instance attributes to capture")) + "</p>";
            return html + '<p class="vas-cil-form-error vas-cil-is-hidden" id="vasCilAttrCreateError"></p>';
        }

        /* Wrap a control (HTML string) in the framework borderless floating-label field
           structure. `btnHtml` (optional) adds a trailing utility action via
           .input-group-append (the control then carries data-hasbtn so vis-input-wrap
           reserves room for it). The framework CSS renders the floating <label>; the
           mandatory asterisk is added manually for these non-dictionary controls. */
        function attrField(ctrlHtml, caption, btnHtml, mandatory) {
            var cap = esc(caption) + (mandatory ? ' <em class="vas-cil-req">*</em>' : "");
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
            return '<button type="button" class="vas-cil-attr-fieldbtn" data-act="' + act + '">' + esc(label) + "</button>";
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
                    if (r && r.Name != null) { $("#vasCilAttrLot").val(r.Name); attrState.newLotId = r.Key; }
                    else showToast(lbl("VAS_074_LotFailed", "Could not create lot"));
                },
                error: function (e) { console.log(e); showBusy(false); showToast(lbl("VAS_074_LotFailed", "Could not create lot")); }
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
                    if (r != null && String(r).length) $("#vasCilAttrSerNo").val(r);
                    else showToast(lbl("VAS_074_SerNoFailed", "Could not generate serial number"));
                },
                error: function (e) { console.log(e); showBusy(false); showToast(lbl("VAS_074_SerNoFailed", "Could not generate serial number")); }
            });
        }

        function renderAttr() {
            var d = $("#vasCilAttr");
            var isCreate = attrState.mode === "create";
            d.find("#vasCilAttrSearchRow").toggleClass("vas-cil-is-hidden", isCreate);
            d.find("#vasCilAttrTitle").toggleClass("vas-cil-is-hidden", isCreate);
            d.find("[data-act=attr-back]").toggleClass("vas-cil-is-hidden", !isCreate);
            d.find("[data-act=attr-create]").toggleClass("vas-cil-is-hidden", isCreate);
            d.find("#vasCilAttrList").toggleClass("vas-cil-is-hidden", isCreate);
            d.find("#vasCilAttrCreate").toggleClass("vas-cil-is-hidden", !isCreate);
            d.find("#vasCilAttrListFoot").toggleClass("vas-cil-is-hidden", isCreate);
            d.find("#vasCilAttrCreateFoot").toggleClass("vas-cil-is-hidden", !isCreate);
            if (isCreate) {
                // Submit button reflects edit (update existing) vs add (new) mode.
                d.find("[data-act=attr-submit]").text(attrState.editAsi ? lbl("VAS_074_UpdateAttribute", "Update attribute") : lbl("VAS_074_AddAttribute", "Add attribute"));
                updateAttrError(); updateAttrSubmit();
                setTimeout(function () { d.find("#vasCilAttrCreate").find("input, select").first().focus(); }, 0);
            }
            else renderAttrRows();
        }

        var ATTR_PAGE_SIZE = 20;
        function renderAttrRows() {
            var body = $("#vasCilAttrRows"); body.empty();
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
            if (!matched.length) { body.append('<p class="vas-cil-empty-message">' + esc(lbl("VAS_074_NoMatches", "No matches")) + "</p>"); return; }
            // Row is a div (not a <button>) so the per-row Edit button can nest legally.
            visible.forEach(function (o) {
                var $row = $('<div class="vas-cil-attr-grid__row" role="button" tabindex="0"></div>');
                var key = optionKey(o);
                if (key === attrState.selected) $row.addClass("is-selected");
                var editCell = (canEdit && o.M_AttributeSetInstance_ID > 0) ?
                    '<span class="vas-cil-attr-edit"><button type="button" class="vas-cil-attr-editbtn" data-act="attr-edit" data-key="' + esc(key) +
                    '" title="' + esc(lbl("VAS_074_Edit", "Edit")) + '">' + icon("pencil", "✎") + "</button></span>" : (canEdit ? "<span></span>" : "");
                $row.html('<span class="vas-cil-attr-radio">' + (key === attrState.selected ? '<span class="vas-cil-attr-radio__dot"></span>' : "") + "</span>" +
                    '<span class="vas-cil-attr-code">' + esc(o.code) + '</span><span class="vas-cil-attr-label">' + esc(o.label) + "</span>" +
                    '<span class="vas-cil-attr-spec">' + esc(o.spec) + '</span><span class="vas-cil-attr-delta">' + esc(o.locator || "—") + "</span>" +
                    '<span class="vas-cil-attr-avail">' + esc(o.availability) + "</span>" + editCell);
                // Ignore clicks on the Edit button - the row's re-render would detach it
                // before the delegated attr-edit handler runs, so let that handler take it.
                $row.on("click", function (e) { if ($(e.target).closest("[data-act=attr-edit]").length) return; attrState.selected = key; renderAttrRows(); });
                $row.on("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); attrState.selected = key; renderAttrRows(); } });
                body.append($row);
            });
        }

        /* Update the list pager: "start-end of total" + prev/next enabled state. */
        function renderAttrPager(total, pageCount, start, shown) {
            var info = $("#vasCilAttrPageInfo");
            if (!info.length) return;
            if (!total) { info.text(lbl("VAS_074_NoRecords", "No records")); }
            else { info.text((start + 1) + "-" + (start + shown) + " " + lbl("VAS_074_Of", "of") + " " + total); }
            $("#vasCilAttr [data-act=attr-prev]").prop("disabled", attrState.page <= 0);
            $("#vasCilAttr [data-act=attr-next]").prop("disabled", attrState.page >= pageCount - 1);
        }

        /* Switch the dialog into create/edit mode. Rebuilds the create form fresh (so a
           prior edit's prefilled values don't linger) and, when an existing instance is
           passed, autofills it for editing. editAsi != null -> the submit UPDATES that
           M_AttributeSetInstance in place; null -> creates a new one. */
        function openCreateForm(editAsi) {
            attrState.editAsi = editAsi || null;
            attrState.mode = "create";
            attrState.error = "";
            $("#vasCilAttrCreate").html(attrCreateForm());   // fresh, empty controls
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
            if (o.lot) $("#vasCilAttrLot").val(o.lot);
            if (o.serno) $("#vasCilAttrSerNo").val(o.serno);
            if (o.guaranteeDate) $("#vasCilAttrGuarantee").val(o.guaranteeDate);
            var asi = parseInt(o.M_AttributeSetInstance_ID, 10) || 0;
            if (asi <= 0) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/GetInstanceValues",
                type: "GET", dataType: "json", data: { M_AttributeSetInstance_ID: asi },
                success: function (raw) {
                    showBusy(false);
                    if (attrState.mode !== "create" || !attrState.editAsi || attrState.editAsi.M_AttributeSetInstance_ID !== asi) return;  // moved on
                    var vals = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!vals || !vals.length) return;
                    for (var i = 0; i < vals.length; i++) {
                        var v = vals[i], $el = $("#vasCilAttrF_" + v.M_Attribute_ID);
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
            var el = $("#vasCilAttrCreateError");
            el.text(attrState.error).toggleClass("vas-cil-is-hidden", !attrState.error);
        }
        // The dynamic form validates on submit, so the button stays enabled.
        function updateAttrSubmit() { $("#vasCilAttr [data-act=attr-submit]").prop("disabled", false); }
        function attrErr(name) { attrState.error = lbl("VAS_074_FieldRequired", "Required") + ": " + name; updateAttrError(); }

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
                showToast(lbl("VAS_074_AttrNotPersisted", "New attribute is display-only until it exists in the product attribute set"));
                return;
            }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/SaveAttribute",
                type: "POST", dataType: "json",
                data: {
                    payload: JSON.stringify({
                        M_Product_ID: line.values.M_Product_ID, Lot: "", SerNo: "", GuaranteeDate: "",
                        Values: [{ M_Attribute_ID: sel.M_Attribute_ID, ValueType: "L", M_AttributeValue_ID: sel.M_AttributeValue_ID, DisplayValue: sel.label }]
                    })
                },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.M_AttributeSetInstance_ID > 0) {
                        line.values.M_AttributeSetInstance_ID = res.M_AttributeSetInstance_ID;
                        line.display.attrName = sel.code; markDirty(line);
                        closeDialogs(); editing = { rowId: line.rowId, field: "description" };
                        runCallout(line, "M_AttributeSetInstance_ID");
                    } else { showToast(lbl("VAS_074_AttrSaveFailed", "Could not save attribute")); }
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_074_AttrSaveFailed", "Could not save attribute")); }
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
                var $el = $("#vasCilAttrF_" + a.M_Attribute_ID);
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
            var lot = info.IsLot ? ($("#vasCilAttrLot").val() || "") : "";
            var serno = info.IsSerNo ? ($("#vasCilAttrSerNo").val() || "") : "";
            var guarantee = info.IsGuaranteeDate ? ($("#vasCilAttrGuarantee").val() || "") : "";
            if (lot) labelParts.push(lbl("VAS_074_Lot", "Lot") + ": " + lot);
            if (serno) labelParts.push(lbl("VAS_074_SerialNo", "Serial No") + ": " + serno);
            if (!values.length && !lot && !serno && !guarantee) {
                attrState.error = lbl("VAS_074_NothingEntered", "Enter at least one attribute value"); updateAttrError(); return;
            }

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/SaveAttribute",
                type: "POST", dataType: "json",
                // editAsi set -> UPDATE that instance in place; else create a new one.
                data: {
                    payload: JSON.stringify({
                        M_Product_ID: line.values.M_Product_ID,
                        M_AttributeSetInstance_ID: (attrState.editAsi && attrState.editAsi.M_AttributeSetInstance_ID) || 0,
                        Lot: lot, SerNo: serno, GuaranteeDate: guarantee, Values: values
                    })
                },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.M_AttributeSetInstance_ID > 0) {
                        line.values.M_AttributeSetInstance_ID = res.M_AttributeSetInstance_ID;
                        line.display.attrName = res.Description || labelParts.join(", "); markDirty(line);
                        closeDialogs(); editing = { rowId: line.rowId, field: "description" };
                        runCallout(line, "M_AttributeSetInstance_ID");
                    } else { showToast(lbl("VAS_074_AttrSaveFailed", "Could not save attribute")); }
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_074_AttrSaveFailed", "Could not save attribute")); }
            });
        }

        /* ---------- scan dialog ---------- */
        function openScanDialog() {
            closeDialogs();
            scanState = { rows: [], input: "", error: "" };
            var backdrop = $('<div class="vas-cil-dialog-backdrop" id="vasCilScan"></div>');
            var dialog = $('<div class="vas-cil-dialog vas-cil-dialog--wide"></div>');
            dialog.html(
                '<header class="vas-cil-dialog__header">' +
                '<div class="vas-cil-dialog__header-row"><div class="vas-cil-dialog__title-block">' + icon("scan-line", "▭") + '<h3 class="vas-cil-dialog__title">' + esc(lbl("VAS_074_QuickAddBarcode", "Quick add by barcode")) + "</h3></div>" +
                '<p class="vas-cil-dialog__hint">' + esc(lbl("VAS_074_ScanToReviewSubmit", "Scan to add · review · submit")) + "</p></div>" +
                '<p class="vas-cil-dialog__sub">' + esc(lbl("VAS_074_ScanHint", "Scan a product barcode; items collect below. Duplicate scans increase quantity.")) + "</p>" +
                '<div class="vas-cil-scan-input"><button type="button" class="vas-cil-scan-input__icon" data-act="sim-scan" title="' + esc(lbl("VAS_074_SimulateScan", "Simulate scan")) + '">' + icon("scan-line", "▭") + "</button>" +
                '<input type="text" id="vasCilScanInput" placeholder="' + esc(lbl("VAS_074_ListeningScans", "Listening for scans… (type a code + Enter)")) + '" autocomplete="off" /></div>' +
                '<p class="vas-cil-dialog__error vas-cil-is-hidden" id="vasCilScanError"></p></header>' +
                '<div class="vas-cil-dialog__body vas-cil-dialog__body--fixed">' +
                '<div class="vas-cil-scan-empty" id="vasCilScanEmpty"><div class="vas-cil-scan-empty__badge">' + icon("scan-line", "▭") + "</div>" +
                '<p class="vas-cil-scan-empty__title">' + esc(lbl("VAS_074_ScanToBegin", "Scan a barcode to begin")) + '</p><p class="vas-cil-scan-empty__hint">e.g. PRD-BLW-001 · CHG-INS-001</p></div>' +
                '<div class="vas-cil-scan-grid vas-cil-is-hidden" id="vasCilScanGrid"><div class="vas-cil-scan-grid__head"><div>' + esc(lbl("VAS_074_Code", "Code")) + "</div><div>" + esc(lbl("VAS_074_ProductCharge", "Product / Charge")) +
                "</div><div>" + esc(lbl("VAS_074_Status", "Status")) + "</div><div>" + esc(lbl("VAS_074_Qty", "Qty")) + '</div><div></div></div><div class="vas-cil-scan-grid__body" id="vasCilScanRows"></div></div></div>' +
                '<footer class="vas-cil-dialog__footer"><p class="vas-cil-dialog__summary" id="vasCilScanSummary">' + esc(lbl("VAS_074_NoScans", "No scans yet")) + "</p>" +
                '<div class="vas-cil-dialog__actions"><button type="button" class="vas-cil-btn vas-cil-btn--ghost" data-act="close-scan">' + esc(lbl("VAS_074_Cancel", "Cancel")) +
                '</button><button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="submit-scan" disabled>' + esc(lbl("VAS_074_AddLines", "Add lines")) + "</button></div></footer>");
            backdrop.append(dialog);
            $("body").append(backdrop);

            backdrop.on("mousedown", function (e) { if (e.target === backdrop[0]) closeDialogs(); });
            var $field = dialog.find("#vasCilScanInput");
            $field.on("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); handleScan($field.val()); $field.val(""); } });
            dialog.on("click", "[data-act=sim-scan]", function () { $field.focus(); });
            dialog.on("click", "[data-act=close-scan]", closeDialogs);
            dialog.on("click", "[data-act=submit-scan]", submitScan);
            dialog.on("click", ".vas-cil-scan-del", function () { var id = $(this).attr("data-id"); scanState.rows = scanState.rows.filter(function (r) { return r.id !== id; }); renderScanRows(); });
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
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/ScanLookup",
                type: "GET", dataType: "json", data: { C_Invoice_ID: parent.C_Invoice_ID, code: code },
                success: function (raw) {
                    var it = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    scanState.rows.push({ id: "s" + (++rowCounter), code: code, item: (it && it.RecordId > 0) ? it : null, qty: "1" });
                    scanState.error = (it && it.RecordId > 0) ? "" : lbl("VAS_074_NoMatchFor", "No catalog match for") + ' "' + code + '"';
                    renderScanRows();
                },
                error: function (err) { console.log(err); }
            });
        }

        function renderScanRows() {
            var d = $("#vasCilScan"); if (!d.length) return;
            var err = d.find("#vasCilScanError");
            err.toggleClass("vas-cil-is-hidden", !scanState.error).text(scanState.error);
            var empty = d.find("#vasCilScanEmpty"), grid = d.find("#vasCilScanGrid");
            if (!scanState.rows.length) { empty.removeClass("vas-cil-is-hidden"); grid.addClass("vas-cil-is-hidden"); }
            else {
                empty.addClass("vas-cil-is-hidden"); grid.removeClass("vas-cil-is-hidden");
                var body = d.find("#vasCilScanRows"); body.empty();
                scanState.rows.forEach(function (r) {
                    var matched = !!r.item;
                    var badge = matched ? (r.item.Kind === "C" ? "charge" : "product") : "danger";
                    var blabel = matched ? (r.item.Kind === "C" ? lbl("VAS_074_Charge", "Charge") : lbl("VAS_074_Product", "Product")) : lbl("VAS_074_Unknown", "Unknown");
                    var row = $('<div class="vas-cil-scan-grid__row' + (matched ? "" : " is-unknown") + '"></div>');
                    row.html('<span class="vas-cil-scan-grid__code">' + esc(r.code) + '</span><span class="vas-cil-scan-grid__name">' + esc(matched ? r.item.DisplayName : "—") +
                        '</span><span class="vas-cil-badge vas-cil-badge--' + badge + '">' + esc(blabel) + "</span>" +
                        '<div class="vas-cil-qty-stepper"><button type="button" data-qminus="' + r.id + '" ' + (!matched || parseInt(r.qty, 10) <= 1 ? "disabled" : "") + ">−</button>" +
                        '<input type="text" inputmode="numeric" data-qinput="' + r.id + '" value="' + esc(r.qty) + '" ' + (matched ? "" : "disabled") + ' />' +
                        '<button type="button" data-qplus="' + r.id + '" ' + (matched ? "" : "disabled") + ">+</button></div>" +
                        '<button type="button" class="vas-cil-icon-btn--danger vas-cil-scan-del" data-id="' + r.id + '">' + icon("trash", "🗑") + "</button>");
                    body.append(row);
                });
            }
            var matched = scanState.rows.filter(function (r) { return !!r.item; });
            var units = matched.reduce(function (a, r) { return a + (parseInt(r.qty || "0", 10) || 0); }, 0);
            var unknown = scanState.rows.length - matched.length;
            d.find("#vasCilScanSummary").text(!scanState.rows.length ? lbl("VAS_074_NoScans", "No scans yet")
                : matched.length + " " + lbl("VAS_074_Matched", "matched") + " · " + units + " " + lbl("VAS_074_Units", "unit(s)") + (unknown > 0 ? " · " + unknown + " " + lbl("VAS_074_Unknown", "unknown") : ""));
            d.find("[data-act=submit-scan]").prop("disabled", matched.length === 0).text(lbl("VAS_074_AddLines", "Add lines") + (matched.length ? " (" + matched.length + ")" : ""));
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
                    values: {
                        C_InvoiceLine_ID: 0, Line: maxLine, M_Product_ID: r.item.Kind === "C" ? 0 : r.item.RecordId, C_Charge_ID: r.item.Kind === "C" ? r.item.RecordId : 0,
                        M_AttributeSetInstance_ID: 0, QtyEntered: parseInt(r.qty || "1", 10) || 1, C_UOM_ID: 0, PriceEntered: 0, C_Tax_ID: 0, Discount: 0, Notes: "", Description: ""
                    },
                    display: { productName: r.item.Kind === "C" ? "" : r.item.DisplayName, chargeName: r.item.Kind === "C" ? r.item.DisplayName : "", uomName: "", taxName: "", attrName: "", hasAttributeSet: !!r.item.HasAttributeSet }
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
            if (!parent || !parent.IsEditable) { showToast(lbl("VAS_074_InvoiceNotEditable", "This invoice cannot take new lines")); return; }
            if (unsavedLines().length) saveRows();   // failure already messaged; stay put
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
            return {
                C_InvoiceLine_ID: v.C_InvoiceLine_ID || 0, RowKey: l.rowId, Line: v.Line || 0, M_Product_ID: v.M_Product_ID || 0, C_Charge_ID: v.C_Charge_ID || 0,
                M_AttributeSetInstance_ID: v.M_AttributeSetInstance_ID || 0, QtyEntered: v.QtyEntered || 0, C_UOM_ID: v.C_UOM_ID || 0,
                PriceEntered: v.PriceEntered || 0, C_Tax_ID: v.C_Tax_ID || 0, Discount: v.Discount || 0, Description: v.Description || "",
                Values: v, TouchedCols: l._dynTouched ? Object.keys(l._dynTouched) : []
            };
        }

        /* Save the currently-unsaved lines as a non-blocking batch. Each saved row shows
           its OWN per-row spinner (like a callout) instead of a panel-wide overlay, and
           is locked from editing until the save returns - so the user can keep working
           (Add line, edit other rows) while the save is in flight. */
        function saveRows(done, restrictIds) {
            commitMorePopover();
            if (!panelEditable()) { if (done) done(false); return; }   // read-only when doc completed/void/reversed/closed
            var batch = unsavedLines();
            // A DEFERRED flush saves ONLY the rows the user had actually Save-clicked (snapshotted
            // in restrictIds), never a line added afterwards while the prior save was running.
            if (restrictIds) batch = batch.filter(function (l) { return restrictIds[l.rowId]; });
            if (!batch.length || !parent) { if (done) done(false); return; }
            if (!validateUnsaved(batch)) { if (done) done(false); return; }   // AD_Column mandatory validation (only the rows being saved)
            // Serialize: never run a second SaveLines while one is in flight (they'd share a
            // server transaction and race on the invoice tax/totals). Defer instead - snapshot
            // exactly THESE rows so the in-flight save's completion flushes them; a line the user
            // adds later but never Save-clicks is not swept in. Rows stay editable until flushed.
            if (saveInFlight) { batch.forEach(function (l) { pendingSaveIds[l.rowId] = true; }); if (done) done(false); return; }
            saveInFlight = true;
            var rows = batch.map(buildRowPayload);
            // Lock + show a per-row spinner on each saving row.
            batch.forEach(function (l) { l._saving = true; setRowBusy(l, true, lbl("VAS_074_Saving", "Saving…")); });
            renderHeaderButtons();   // the batch no longer counts as "unsaved" -> Save mutes
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/SaveLines",
                type: "POST", dataType: "json", data: { payload: JSON.stringify({ C_Invoice_ID: parent.C_Invoice_ID, AD_Window_ID: $self.AD_Window_ID || 0, Page: linePage, Lines: rows }) },
                success: function (raw) {
                    saveInFlight = false;
                    batch.forEach(function (l) { l._saving = false; });
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.Success) { applyLinePaging(res); mergeSavedLines(batch, res.Lines); showToast(lbl("VAS_074_LinesSaved", "Lines saved")); refreshSummary(); if (done) done(true); }
                    else { batch.forEach(function (l) { setRowBusy(l, false); }); showServerSaveErrors(batch, res); if (done) done(false); }
                    flushPendingSave();   // save any line(s) queued while this was in flight
                },
                error: function (err) { saveInFlight = false; console.log(err); batch.forEach(function (l) { l._saving = false; setRowBusy(l, false); }); showServerSaveErrors(batch, null); if (done) done(false); flushPendingSave(); }
            });
        }

        /* Flush the rows the user Save-clicked while a prior save was in flight (snapshotted in
           pendingSaveIds). Called from the in-flight save's success AND error handlers, so those
           rows persist after the current save settles - restricted to the snapshotted rowIds so a
           line added afterwards but never Save-clicked is NOT saved. */
        function flushPendingSave() {
            var ids = pendingSaveIds;
            pendingSaveIds = {};
            for (var k in ids) { if (ids.hasOwnProperty(k)) { saveRows(null, ids); return; } }
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
                if (l) { l._error = errs[i].Message || lbl("VAS_074_SaveFailed", "Save failed"); mapped++; }
            }
            if (!mapped) {
                // No per-line info (e.g. a batch-level exception) - still show it in place on
                // the first row, and toast as a safety net.
                var detail = (res && res.ErrorDetail) ? res.ErrorDetail : "";
                var msg = lbl((res && res.ErrorKey) || "VAS_074_SaveFailed", "Save failed") + (detail ? " - " + detail : "");
                if (batch.length) batch[0]._error = msg; else showToast(msg);
            }
            render();
            var $first = $linesBody.find(".vas-cil-row--line.is-invalid").first();
            if ($first.length && $first[0].scrollIntoView) $first[0].scrollIntoView({ block: "nearest" });
        }

        /* Match a server LineSaveError back to its client line: by RowKey (the client rowId),
           then by C_InvoiceLine_ID (existing lines), then by Line number. */
        function findBatchLine(batch, e) {
            var i;
            if (e && e.RowKey) { for (i = 0; i < batch.length; i++) if (batch[i].rowId === e.RowKey) return batch[i]; }
            if (e && e.C_InvoiceLine_ID > 0) { for (i = 0; i < batch.length; i++) if ((batch[i].values.C_InvoiceLine_ID || 0) === e.C_InvoiceLine_ID) return batch[i]; }
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
                var id = l.values.C_InvoiceLine_ID || 0;
                if (id > 0 && !inBatch(l) && l.dirty) dirtyById[id] = l;
            });
            // Brand-new client lines not part of this batch (added during the save).
            var newKeep = lines.filter(function (l) { return (l.values.C_InvoiceLine_ID || 0) <= 0 && !inBatch(l); });
            var merged = (serverRows || []).map(function (r) {
                return dirtyById[r.C_InvoiceLine_ID] || fromServerRow(r);
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
                var id = l.values.C_InvoiceLine_ID || 0;
                if (id > 0 && l.dirty) dirtyById[id] = l;   // edited-but-unsaved existing line
            });
            var newKeep = lines.filter(function (l) { return (l.values.C_InvoiceLine_ID || 0) <= 0; });
            var merged = (serverRows || []).map(function (r) {
                return dirtyById[r.C_InvoiceLine_ID] || fromServerRow(r);
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
            sel.forEach(function (l) { if (l.values.C_InvoiceLine_ID > 0) ids.push(l.values.C_InvoiceLine_ID); else localOnly.push(l); });
            localOnly.forEach(function (l) { var i = lines.indexOf(l); if (i >= 0) lines.splice(i, 1); });
            if (!ids.length) { render(); return; }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_074_CreateInvoiceLinePanel/DeleteLines",
                type: "POST", dataType: "json", data: { payload: JSON.stringify({ C_Invoice_ID: parent.C_Invoice_ID, AD_Window_ID: $self.AD_Window_ID || 0, Page: linePage, LineIds: ids }) },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.Success) { applyLinePaging(res); reloadLinesKeepingUnsaved(res.Lines); showToast(lbl("VAS_074_LinesDeleted", "Lines deleted")); refreshSummary(); }
                    else showToast(lbl((res && res.ErrorKey) || "VAS_074_DeleteFailed", "Delete failed"));
                },
                error: function (err) { console.log(err); showBusy(false); showToast(lbl("VAS_074_DeleteFailed", "Delete failed")); }
            });
        }

        /* ---------- misc ---------- */
        function showToast(msg) {
            if (VIS && VIS.ADD && typeof VIS.ADD.Notification === "function") { VIS.ADD.Notification(msg); return; }
            var $t = $('<div class="vas-cil-toast"></div>').text(msg);
            $("body").append($t);
            setTimeout(function () { $t.addClass("vas-cil-toast--show"); }, 10);
            setTimeout(function () { $t.removeClass("vas-cil-toast--show"); setTimeout(function () { $t.remove(); }, 300); }, 2600);
        }

        function closeDialogs() { $("#vasCilAttr, #vasCilScan, #vasCilMore").remove(); attrState = null; scanState = null; morePopoverFor = null; }

        function onDocMouseDown(e) {
            // The additional-fields modal manages its own outside-click (backdrop); no
            // inline popover to dismiss here anymore.
        }

        // Escape closes the top-most open dialog / popover (bubble phase).
        $(document).on("keydown.vascil", function (e) {
            if (e.key !== "Escape") return;
            if (scanState) { closeDialogs(); return; }
            if (attrState) { closeDialogs(); return; }
            // The Additional-Info modal closes ONLY via its Done button (Escape ignored).
        });

        // Ctrl+Alt+ N/S/D/Z/Q action shortcuts (N=add, S=save, D=delete, Z=undo, Q=refresh).
        // Bound in the CAPTURE phase (3rd arg true) so they
        // fire BEFORE the grid cell editors' own keydown handlers, which stopPropagation() to
        // keep keys from the framework - otherwise the shortcut is swallowed while a product /
        // qty / price / UOM / tax control has focus (e.g. right after Add line focuses the
        // product cell). Matching is case-INSENSITIVE (e.key lowered, e.code fallback) so Caps
        // Lock / Shift / keyboard layout don't matter. Fires only while this panel is the
        // visible/active tab, an invoice is loaded, and no dialog is open (finish it first).
        function panelShortcutKeydown(e) {
            if (!e.ctrlKey || !e.altKey || e.shiftKey || e.metaKey) return;
            if (!parent || !$root || !$root.is(":visible")) return;
            if (attrState || scanState || morePopoverFor) return;
            // Also bail while any vas-cil dialog is open in the DOM - covers VIS.AttributeControl
            // (#vasCilAttr), which keeps its own state, not the panel's attrState.
            if (document.getElementById("vasCilAttr") || document.getElementById("vasCilScan") || document.getElementById("vasCilMore")) return;
            var k = (e.key || "").toLowerCase(), code = e.code, act = null;
            if (k === "n" || code === "KeyN") act = function () { addLine(); };
            else if (k === "s" || code === "KeyS") act = function () { flushActiveEdit(); saveRows(); };
            else if (k === "d" || code === "KeyD") act = function () {
                if (!selectedCount()) { showToast(lbl("VAS_074_SelectRowToDelete", "Select a row to delete")); return; }
                deleteSelected();
            };
            else if (k === "z" || code === "KeyZ") act = function () { undoActive(); };
            else if (k === "q" || code === "KeyQ") act = function () { refreshPanel(); };
            else return;   // not one of ours - let it through
            e.preventDefault();
            e.stopPropagation();
            act();
        }
        this._shortcutFn = panelShortcutKeydown;
        document.addEventListener("keydown", panelShortcutKeydown, true);

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_074_CreateInvoiceLinePanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") this.table_ID = curTab.getAD_Table_ID();
        if (curTab && typeof curTab.getAD_Window_ID === "function") this.AD_Window_ID = curTab.getAD_Window_ID();
        this.init();
    };

    VAS.VAS_074_CreateInvoiceLinePanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    VAS.VAS_074_CreateInvoiceLinePanel.prototype.sizeChanged = function (width) { this.panelWidth = width; };

    VAS.VAS_074_CreateInvoiceLinePanel.prototype.dispose = function () {
        $(document).off("mousedown.vascil").off("keydown.vascil");
        if (this._shortcutFn) { document.removeEventListener("keydown", this._shortcutFn, true); this._shortcutFn = null; }
        $("#vasCilAttr, #vasCilScan, .vas-cil-toast").remove();
        this.record_ID = 0; this.table_ID = 0; this.windowNo = 0;
        this.curTab = null; this.selectedRow = null; this.panelWidth = null;
    };

})(VAS, jQuery);
