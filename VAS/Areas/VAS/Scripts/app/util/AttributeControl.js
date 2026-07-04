/**
 * Reusable product Attribute (M_AttributeSetInstance) picker control.
 *
 *   VIS.AttributeControl.open(opts)
 *
 * A self-contained dialog (list of existing instances + a "New attribute" create/edit
 * form, with Lot / Serial No / Guarantee Date) that any panel can use. It manages its
 * own DOM (mounted on <body> as #vasCilAttr), state and lifecycle, and reports the chosen
 * instance back through opts.onApply - it does NOT know about the host's row/line model.
 *
 * opts:
 *   M_Product_ID            (int, required)   product whose attribute set drives the form
 *   M_AttributeSetInstance_ID (int)           the currently-selected instance (pre-selected
 *                                             in the list; an unchanged OK is a no-op)
 *   newAttribute            (bool)            true -> open directly on the "New attribute"
 *                                             create form; false/omitted -> the instance list
 *   showAll                 (bool)            initial state of the "Show All (include zero and
 *                                             (-ve) qty)" checkbox in the instance list. true
 *                                             (default) -> no stock filter; false -> only rows
 *                                             with QtyOnHand > 0. Caller sets it per screen.
 *   productName             (string)          shown in the dialog header
 *   onApply(result)         (fn)              called when the user picks/creates an instance.
 *                                             result = { M_AttributeSetInstance_ID, description }
 *                                             (M_AttributeSetInstance_ID === 0 -> display-only)
 *   contextUrl              (string)          AJAX base (default VIS.Application.contextUrl)
 *   endpoints               ({getAttributes, getInstanceValues, saveAttribute})
 *                                             shared controller action paths (defaults below)
 *   lbl,esc,icon,showBusy,showToast,dateStr,fmtMoney,parseNum
 *                                             host helpers; sensible defaults are used when
 *                                             a consumer does not pass its own.
 *
 * Styling reuses the host's vas-cil-* dialog classes (treated as the shared style); the
 * framework PAttributes/* endpoints (GetAttributeData / CreateLot / GetSerNo) are shared.
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.AttributeControl = VIS.AttributeControl || {};

    /* ---------- default helpers (overridden by opts when the host passes its own) ---------- */
    function defEsc(s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }
    function defLbl(key, def) {
        try { if (VIS.Msg && typeof VIS.Msg.getMsg === "function") { var m = VIS.Msg.getMsg(key); if (m && m !== key) return m; } } catch (e) { }
        return def != null ? def : key;
    }
    function defIcon(name, glyph) { return '<span class="vas-cil-icon" data-icon="' + name + '">' + (glyph || "") + "</span>"; }
    function defBusy() { }
    function defToast(msg) { try { if (VIS.viewManager && VIS.viewManager.ShowMessage) { VIS.viewManager.ShowMessage(msg); return; } } catch (e) { } if (window.console) console.log(msg); }
    function defDateStr(d) { return d == null ? "" : String(d).substring(0, 10); }
    function defMoney(n) { return String(+n || 0); }
    function defParseNum(s) { var n = parseFloat(String(s == null ? "" : s).replace(/[^0-9.\-]/g, "")); return isNaN(n) ? 0 : n; }

    var ATTR_PAGE_SIZE = 20;
    // Reference length for a String attribute (AttributeValueType "StringMax40").
    var ATTR_STRING_MAX_LEN = 40;

    /* per-open config + state */
    var cfg = null;   // { L,E,IC,BUSY,TOAST,DSTR,MONEY,PNUM, contextUrl, ep, onApply }
    var st = null;    // { M_Product_ID, M_AttributeSetInstance_ID, productName, info, options, selected, mode, search, page, error, editAsi, newLotId }

    VIS.AttributeControl.open = function (opts) {
        opts = opts || {};
        if (!(parseInt(opts.M_Product_ID, 10) > 0)) return;
        var base = (typeof opts.contextUrl === "string") ? opts.contextUrl
            : ((window.VIS && VIS.Application && VIS.Application.contextUrl) || "");
        var ep = opts.endpoints || {};
        cfg = {
            L: opts.lbl || defLbl,
            E: opts.esc || defEsc,
            IC: opts.icon || defIcon,
            BUSY: opts.showBusy || defBusy,
            TOAST: opts.showToast || defToast,
            DSTR: opts.dateStr || defDateStr,
            MONEY: opts.fmtMoney || defMoney,
            PNUM: opts.parseNum || defParseNum,
            contextUrl: base,
            ep: {
                getAttributes: ep.getAttributes || "VAS_AttributeControl/GetProductAttributes",
                getInstanceValues: ep.getInstanceValues || "VAS_AttributeControl/GetInstanceValues",
                saveAttribute: ep.saveAttribute || "VAS_AttributeControl/SaveAttribute"
            },
            onApply: (typeof opts.onApply === "function") ? opts.onApply : function () { }
        };
        closeDialog();
        st = {
            M_Product_ID: parseInt(opts.M_Product_ID, 10) || 0,
            M_AttributeSetInstance_ID: parseInt(opts.M_AttributeSetInstance_ID, 10) || 0,
            productName: opts.productName || "",
            // opts.newAttribute === true -> open straight on the "New attribute" create form;
            // false / omitted -> the existing-instance list (default). Caller decides the rule.
            newAttribute: !!opts.newAttribute,
            // "Show All (include zero and (-ve) qty)" checkbox default. Caller sets it per
            // screen; omitted -> true (no QtyOnHand filter, preserves the prior behaviour).
            showAll: (opts.showAll !== undefined) ? !!opts.showAll : true,
            options: [], selected: "", mode: "list", search: "", page: 0, error: ""
        };
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + cfg.ep.getAttributes,
            type: "GET", dataType: "json", data: { M_Product_ID: st.M_Product_ID },
            success: function (raw) {
                cfg.BUSY(false);
                var info = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                st.info = info || { Attributes: [] };
                st.options = loadExistingInstances();
                // Pre-select (and page to) the row matching the current instance so reopening
                // shows the chosen value selected by default. When the line has NO attribute yet
                // (cur == 0) pre-select NOTHING - do NOT default to the first row (request #1).
                var cur = st.M_AttributeSetInstance_ID;
                var selIdx = cur > 0 ? indexOfAsi(st.options, cur) : -1;
                st.selected = selIdx >= 0 ? optionKey(st.options[selIdx]) : "";
                st.page = selIdx > 0 ? Math.floor(selIdx / ATTR_PAGE_SIZE) : 0;
                buildDialog();
                // newAttribute === true: jump straight to the create/edit form.
                //  - a value is ALREADY selected -> open THAT instance in EDIT mode (prefilled
                //    values + "Update attribute"), when the role may edit;
                //  - nothing selected -> a blank New-attribute form, when the role may create.
                // (If neither applies, stay on the existing-instance list.)
                if (st.newAttribute) {
                    var curAsi = st.M_AttributeSetInstance_ID;
                    var curIdx = curAsi > 0 ? indexOfAsi(st.options, curAsi) : -1;
                    if (curIdx >= 0) {
                        if (st.info && st.info.IsCanEdit) editExistingInstance(st.options[curIdx]);
                    } else if (st.info && st.info.IsCanCreate) {
                        openCreateForm(null);
                    }
                }
            },
            error: function (err) { console.log(err); cfg.BUSY(false); }
        });
    };

    /* close + dispose the control's own dialog */
    function closeDialog() { $("#vasCilAttr").remove(); st = null; }
    VIS.AttributeControl.close = closeDialog;

    // Stable selection identity for a list row - unique PER ROW (rowKey), so two rows of
    // the same ASI (different locators) don't select together.
    function optionKey(o) { return (o && (o.rowKey || o.key || o.code)) || ""; }
    // First row index whose instance matches the given M_AttributeSetInstance_ID.
    function indexOfAsi(options, asi) {
        for (var i = 0; i < options.length; i++) {
            if ((parseInt(options[i].M_AttributeSetInstance_ID, 10) || 0) === asi) return i;
        }
        return -1;
    }

    /* Loads the product's existing M_AttributeSetInstance rows through the framework
       PAttributes/GetAttributeData endpoint (it builds its own SELECT and appends our WHERE,
       binding @M_Product_ID - so we pass a WHERE clause + ORDER BY, NOT a full SELECT).
       Returns [] when the framework data context is unavailable. */
    function loadExistingInstances() {
        var pid = st.M_Product_ID;
        if (pid <= 0) return [];
        try {
            if (!(window.VIS && VIS.secureEngine && VIS.dataContext && typeof VIS.dataContext.getJSONData === "function")) return [];
            // "Show All" unchecked -> only rows with stock on hand (QtyOnHand > 0), matching the
            // framework pattributeinstance grid's optional filter; checked -> include zero/-ve qty.
            var where = "patr.M_Product_ID=@M_Product_ID AND patr.M_AttributeSetInstance_ID != 0"
                + (st.showAll ? "" : " AND s.QtyOnHand > 0")
                + " ORDER BY asi.GuaranteeDate, QtyOnHand DESC";
            var rows = VIS.dataContext.getJSONData(cfg.contextUrl + "PAttributes/GetAttributeData",
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
                    rowKey: "r" + i,
                    key: "ASI:" + r.M_AttributeSetInstance_ID,
                    code: r.Lot || r.SerNo || ("#" + r.M_AttributeSetInstance_ID),
                    label: r.Description || "",
                    spec: r.GuaranteeDate ? cfg.DSTR(r.GuaranteeDate) : "",
                    locator: r.Value || "",
                    availability: cfg.MONEY(qoh),
                    qtyOnHand: qoh,
                    M_AttributeSetInstance_ID: r.M_AttributeSetInstance_ID,
                    M_Locator_ID: +r.M_Locator_ID || 0,
                    lot: r.Lot || "", serno: r.SerNo || "", guaranteeDate: r.GuaranteeDate ? cfg.DSTR(r.GuaranteeDate) : ""
                });
            }
        }
        return out;
    }

    /* Reload the existing-instance list after the "Show All" toggle changes. getJSONData is
       synchronous, so this re-queries, re-resolves the current selection, resets to page 1
       and repaints the rows in place. */
    function reloadInstances() {
        cfg.BUSY(true);
        st.options = loadExistingInstances();
        var cur = st.M_AttributeSetInstance_ID;
        var selIdx = cur > 0 ? indexOfAsi(st.options, cur) : -1;
        st.selected = selIdx >= 0 ? optionKey(st.options[selIdx]) : "";
        st.page = 0;
        cfg.BUSY(false);
        renderAttrRows();
    }

    function buildDialog() {
        var L = cfg.L, E = cfg.E, IC = cfg.IC;
        var backdrop = $('<div class="vas-cil-dialog-backdrop" id="vasCilAttr"></div>');
        var dialog = $('<div class="vas-cil-dialog vas-cil-dialog--wide"></div>');
        dialog.html(
            '<header class="vas-cil-dialog__header">' +
            '<div class="vas-cil-dialog__header-row"><h3 class="vas-cil-dialog__title" id="vasCilAttrTitle">' + E(L("VAS_074_SelectAttribute", "Select attribute")) + "</h3>" +
            '<button type="button" class="vas-cil-btn vas-cil-btn--outline-pill vas-cil-is-hidden" data-act="attr-back">' + IC("arrow-left", "←") + "<span>" + E(L("VAS_074_Back", "Back")) + "</span></button>" +
            (st.info && st.info.IsCanCreate ?
                '<button type="button" class="vas-cil-btn vas-cil-btn--outline-pill" data-act="attr-create">' + IC("plus", "+") + "<span>" + E(L("VAS_074_NewAttribute", "New attribute")) + "</span></button>" : "") + "</div>" +
            '<div class="vas-cil-dialog__type"><span class="vas-cil-badge vas-cil-badge--product">' + E(L("VAS_074_Product", "Product")) + '</span><p class="vas-cil-dialog__primary-name">' + E(st.productName) + "</p></div>" +
            '<div class="vas-cil-attr-listctrls" id="vasCilAttrListCtrls">' +
            '<div class="vas-cil-search-input" id="vasCilAttrSearchRow">' + IC("search", "🔍") + '<input type="text" id="vasCilAttrSearch" placeholder="' + E(L("VAS_074_AttrSearch", "Search attribute values")) + '" /></div>' +
            '<label class="vas-cil-attr-showall" id="vasCilAttrShowAllRow"><input type="checkbox" id="vasCilAttrShowAll"' + (st.showAll ? " checked" : "") + ' /><span>' + E(L("VAS_074_ShowAll", "Show All (include zero and (-ve) qty)")) + "</span></label>" +
            "</div>" +
            "</header>" +
            '<div class="vas-cil-dialog__body vas-cil-dialog__body--fixed">' +
            '<div id="vasCilAttrList"' + (st.info && st.info.IsCanEdit ? ' class="vas-cil-attr-grid--editable"' : "") + '><div class="vas-cil-attr-grid__head"><div></div><div>' + E(L("VAS_074_Code", "Code")) + "</div><div>" + E(L("Description", "Description")) +
            "</div><div>" + E(L("GuaranteeDate", "Guarantee Date")) + "</div><div>" + E(L("M_Locator_ID", "Locator")) + '</div><div class="vas-cil-attr-h-right">' + E(L("QtyOnHand", "On Hand")) + "</div>" +
            (st.info && st.info.IsCanEdit ? "<div>" + E(L("VAS_074_Edit", "Edit")) + "</div>" : "") +
            '</div><div class="vas-cil-attr-grid__body" id="vasCilAttrRows"></div></div>' +
            '<div id="vasCilAttrCreate" class="vas-cil-is-hidden">' + attrCreateForm() + "</div>" +
            "</div>" +
            '<footer class="vas-cil-dialog__footer vas-cil-dialog__footer--end">' +
            '<div id="vasCilAttrListFoot">' +
            '<div class="vas-cil-attr-pager"><button type="button" class="vas-cil-attr-pagebtn" data-act="attr-prev" aria-label="' + E(L("VAS_074_Prev", "Previous")) + '">' + IC("chevron-left", "‹") + '</button>' +
            '<span class="vas-cil-attr-pageinfo" id="vasCilAttrPageInfo"></span>' +
            '<button type="button" class="vas-cil-attr-pagebtn" data-act="attr-next" aria-label="' + E(L("VAS_074_Next", "Next")) + '">' + IC("chevron-right", "›") + "</button></div>" +
            // Error (left, red) fills the middle so the buttons stay grouped on the right.
            '<span class="vas-cil-foot-error"></span>' +
            '<div class="vas-cil-dialog__actions">' +
            '<button type="button" class="vas-cil-btn vas-cil-btn--ghost" data-act="attr-listclose">' + E(L("VAS_074_Cancel", "Cancel")) + "</button>" +
            '<button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="attr-ok">' + E(L("VAS_074_OK", "OK")) + "</button></div></div>" +
            // No Cancel in create mode - the header "Back" button already returns to the list.
            '<div id="vasCilAttrCreateFoot" class="vas-cil-is-hidden">' +
            '<span class="vas-cil-foot-error"></span>' +
            '<div class="vas-cil-dialog__actions">' +
            '<button type="button" class="vas-cil-btn vas-cil-btn--primary" data-act="attr-submit" disabled>' + E(L("VAS_074_AddAttribute", "Add attribute")) + "</button></div></div></footer>");
        backdrop.append(dialog);
        $("body").append(backdrop);

        // Do NOT close on outside/backdrop click; only OK / Cancel close.
        dialog.on("click", "[data-act=attr-create]", function () { openCreateForm(null); });
        dialog.on("click", "[data-act=attr-back],[data-act=attr-cancel]", function () { st.mode = "list"; st.editAsi = null; st.error = ""; renderAttr(); });
        dialog.on("click", "[data-act=attr-ok]", commit);
        // List-mode Cancel closes the whole control (there is no backdrop-click close).
        dialog.on("click", "[data-act=attr-listclose]", function () { closeDialog(); });
        dialog.on("click", "[data-act=attr-submit]", submitNew);
        dialog.on("click", "[data-act=attr-edit]", function (e) {
            e.stopPropagation();
            var key = $(this).attr("data-key");
            var o = st.options.filter(function (x) { return optionKey(x) === key; })[0];
            if (o) editExistingInstance(o);
        });
        dialog.on("click", "[data-act=attr-newlot]", createNewLot);
        dialog.on("click", "[data-act=attr-genserno]", generateSerNo);
        dialog.on("click", "[data-act=attr-prev]", function () { if (st.page > 0) { st.page--; renderAttrRows(); } });
        dialog.on("click", "[data-act=attr-next]", function () { st.page++; renderAttrRows(); });
        dialog.find("#vasCilAttrSearch").on("input", function () { st.search = $(this).val(); st.page = 0; renderAttrRows(); });
        dialog.find("#vasCilAttrShowAll").on("change", function () { st.showAll = this.checked; reloadInstances(); });
        renderAttr();
        setTimeout(function () { dialog.find("#vasCilAttrSearch").focus(); }, 0);
    }

    /* Dynamic create-mode form, one control per INSTANCE attribute (List/Number/Text) plus
       Lot / Serial No / Guarantee Date when the set captures them. */
    function attrCreateForm() {
        var L = cfg.L, E = cfg.E;
        var info = st.info || { Attributes: [] };
        var html = '<div class="vas-cil-attr-form vas-cil-more-grid">';
        var any = false;
        var attrs = (info.Attributes || []).filter(function (a) { return a.IsInstanceAttribute; });
        for (var i = 0; i < attrs.length; i++) {
            var a = attrs[i];
            any = true;
            var id = "vasCilAttrF_" + a.M_Attribute_ID;
            var ctrl;
            if (a.ValueType === "L") {
                // Mandatory: a hidden+disabled placeholder is selected initially so NO real
                // value is auto-picked AND no selectable blank row shows in the open dropdown -
                // the user MUST choose (validation enforces it). Optional: a normal blank row
                // the user can leave empty.
                ctrl = '<select id="' + id + '" placeholder=" " data-placeholder="">'
                    + (a.IsMandatory ? '<option value="" disabled selected hidden></option>' : '<option value=""> </option>');
                for (var v = 0; v < (a.Values || []).length; v++) {
                    var val = a.Values[v];
                    // Show only the value NAME in the dropdown (the Code/value prefix is not
                    // wanted); fall back to the code only when the name is empty.
                    ctrl += '<option value="' + val.M_AttributeValue_ID + '">' + E(val.Name || val.Code) + "</option>";
                }
                ctrl += "</select>";
            } else if (a.ValueType === "N") {
                // Number attribute -> a real numeric input. step="any" so decimals are valid
                // (default step=1 flags non-integers); the browser's numeric keypad follows.
                ctrl = '<input type="number" step="any" id="' + id + '" placeholder=" " data-placeholder="" />';
            } else {
                // String attributes are AttributeValueType "StringMax40" - the reference length
                // is a fixed 40 chars, so cap the input client-side (the framework rejects longer
                // values on save). Kept as a named constant so a schema change is one edit.
                ctrl = '<input type="text" maxlength="' + ATTR_STRING_MAX_LEN + '" id="' + id + '" placeholder=" " data-placeholder="" />';
            }
            html += attrField(ctrl, a.Name, null, a.IsMandatory);
        }
        if (info.IsLot) { any = true; html += attrField(attrTextInput("vasCilAttrLot", true), L("VAS_074_Lot", "Lot"), attrFieldBtn("attr-newlot", L("VAS_074_NewLot", "New")), false); }
        if (info.IsSerNo) { any = true; html += attrField(attrTextInput("vasCilAttrSerNo", true), L("VAS_074_SerialNo", "Serial No"), attrFieldBtn("attr-genserno", L("VAS_074_Generate", "Generate")), false); }
        if (info.IsGuaranteeDate) {
            any = true;
            // Autofill the guarantee date for a NEW instance from the product/attribute-set
            // config (today + GuaranteeDays, else today) - request #3. On EDIT leave it blank
            // here; prefillCreateForm sets it from the existing instance.
            var gval = (!st.editAsi && info.GuaranteeDateDefault) ? info.GuaranteeDateDefault : "";
            html += attrField('<input type="date" id="vasCilAttrGuarantee" value="' + E(gval) + '" placeholder=" " data-placeholder="" />', L("VAS_074_GuaranteeDate", "Guarantee Date"), null, false);
        }
        html += "</div>";
        if (!any) html = '<p class="vas-cil-empty-message">' + E(L("VAS_074_NoInstanceAttr", "No instance attributes to capture")) + "</p>";
        return html;   // save/validation errors render in the footer (.vas-cil-foot-error), not the body
    }

    function attrField(ctrlHtml, caption, btnHtml, mandatory) {
        var cap = cfg.E(caption) + (mandatory ? ' <em class="vas-cil-req">*</em>' : "");
        var inner = '<div class="vis-control-wrap">' + ctrlHtml + "<label>" + cap + "</label></div>";
        if (btnHtml) return '<div class="input-group vis-input-wrap">' + inner + '<div class="input-group-append">' + btnHtml + "</div></div>";
        return '<div class="input-group vis-input-wrap">' + inner + "</div>";
    }
    function attrTextInput(id, hasBtn) {
        return '<input type="text" id="' + id + '" placeholder=" " data-placeholder=""' + (hasBtn ? ' data-hasbtn=" "' : "") + " />";
    }
    function attrFieldBtn(act, label) {
        return '<button type="button" class="vas-cil-attr-fieldbtn" data-act="' + act + '">' + cfg.E(label) + "</button>";
    }

    /* Create a new lot via the framework PAttributes/CreateLot endpoint. */
    function createNewLot() {
        var pid = st.M_Product_ID, asi = st.M_AttributeSetInstance_ID;
        if (pid <= 0) return;
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + "PAttributes/CreateLot",
            type: "GET", dataType: "json", data: { mAttributeSetInstanceId: asi, mProductId: pid },
            success: function (res) {
                cfg.BUSY(false);
                var r = (res && typeof res.result !== "undefined") ? res.result : res;
                if (r && r.Name != null) { $("#vasCilAttrLot").val(r.Name); st.newLotId = r.Key; }
                else cfg.TOAST(cfg.L("VAS_074_LotFailed", "Could not create lot"));
            },
            error: function (e) { console.log(e); cfg.BUSY(false); cfg.TOAST(cfg.L("VAS_074_LotFailed", "Could not create lot")); }
        });
    }
    /* Generate the next serial number via the framework PAttributes/GetSerNo endpoint. */
    function generateSerNo() {
        var pid = st.M_Product_ID, asi = st.M_AttributeSetInstance_ID;
        if (pid <= 0) return;
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + "PAttributes/GetSerNo",
            type: "GET", dataType: "json", data: { mAttributeSetInstanceId: asi, mProductId: pid },
            success: function (res) {
                cfg.BUSY(false);
                var r = (res && typeof res.result !== "undefined") ? res.result : res;
                if (r != null && String(r).length) $("#vasCilAttrSerNo").val(r);
                else cfg.TOAST(cfg.L("VAS_074_SerNoFailed", "Could not generate serial number"));
            },
            error: function (e) { console.log(e); cfg.BUSY(false); cfg.TOAST(cfg.L("VAS_074_SerNoFailed", "Could not generate serial number")); }
        });
    }

    function renderAttr() {
        var L = cfg.L;
        var d = $("#vasCilAttr");
        var isCreate = st.mode === "create";
        d.find("#vasCilAttrListCtrls").toggleClass("vas-cil-is-hidden", isCreate);
        d.find("#vasCilAttrTitle").toggleClass("vas-cil-is-hidden", isCreate);
        d.find("[data-act=attr-back]").toggleClass("vas-cil-is-hidden", !isCreate);
        d.find("[data-act=attr-create]").toggleClass("vas-cil-is-hidden", isCreate);
        d.find("#vasCilAttrList").toggleClass("vas-cil-is-hidden", isCreate);
        d.find("#vasCilAttrCreate").toggleClass("vas-cil-is-hidden", !isCreate);
        d.find("#vasCilAttrListFoot").toggleClass("vas-cil-is-hidden", isCreate);
        d.find("#vasCilAttrCreateFoot").toggleClass("vas-cil-is-hidden", !isCreate);
        // Refresh the footer error for BOTH modes so a stale message is cleared when e.g.
        // Back returns to the list (the handler sets st.error="" before renderAttr()).
        updateAttrError();
        if (isCreate) {
            d.find("[data-act=attr-submit]").text(st.editAsi ? L("VAS_074_UpdateAttribute", "Update attribute") : L("VAS_074_AddAttribute", "Add attribute"));
            updateAttrSubmit();
            setTimeout(function () { d.find("#vasCilAttrCreate").find("input, select").first().focus(); }, 0);
        }
        else renderAttrRows();
    }

    function renderAttrRows() {
        var L = cfg.L, E = cfg.E, IC = cfg.IC;
        var body = $("#vasCilAttrRows"); body.empty();
        var canEdit = !!(st.info && st.info.IsCanEdit);
        var q = (st.search || "").trim().toLowerCase();
        var matched = st.options.filter(function (o) {
            if (!q) return true;
            return (o.code + " " + o.label + " " + o.spec + " " + (o.locator || "")).toLowerCase().indexOf(q) !== -1;
        });
        var pageCount = Math.max(1, Math.ceil(matched.length / ATTR_PAGE_SIZE));
        if (st.page > pageCount - 1) st.page = pageCount - 1;
        if (st.page < 0) st.page = 0;
        var start = st.page * ATTR_PAGE_SIZE;
        var visible = matched.slice(start, start + ATTR_PAGE_SIZE);
        renderAttrPager(matched.length, pageCount, start, visible.length);
        if (!matched.length) { body.append('<p class="vas-cil-empty-message">' + E(L("VAS_074_NoMatches", "No matches")) + "</p>"); return; }
        visible.forEach(function (o) {
            var $row = $('<div class="vas-cil-attr-grid__row" role="button" tabindex="0"></div>');
            var key = optionKey(o);
            if (key === st.selected) $row.addClass("is-selected");
            var editCell = (canEdit && o.M_AttributeSetInstance_ID > 0) ?
                '<span class="vas-cil-attr-edit"><button type="button" class="vas-cil-attr-editbtn" data-act="attr-edit" data-key="' + E(key) +
                '" title="' + E(L("VAS_074_Edit", "Edit")) + '">' + IC("pencil", "✎") + "</button></span>" : (canEdit ? "<span></span>" : "");
            $row.html('<span class="vas-cil-attr-radio">' + (key === st.selected ? '<span class="vas-cil-attr-radio__dot"></span>' : "") + "</span>" +
                '<span class="vas-cil-attr-code">' + E(o.code) + '</span><span class="vas-cil-attr-label">' + E(o.label) + "</span>" +
                '<span class="vas-cil-attr-spec">' + E(o.spec) + '</span><span class="vas-cil-attr-delta">' + E(o.locator || "—") + "</span>" +
                '<span class="vas-cil-attr-avail">' + E(o.availability) + "</span>" + editCell);
            $row.on("click", function (e) { if ($(e.target).closest("[data-act=attr-edit]").length) return; st.selected = key; renderAttrRows(); });
            $row.on("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); st.selected = key; renderAttrRows(); } });
            body.append($row);
        });
    }

    function renderAttrPager(total, pageCount, start, shown) {
        var L = cfg.L;
        var info = $("#vasCilAttrPageInfo");
        if (!info.length) return;
        if (!total) { info.text(L("VAS_074_NoRecords", "No records")); }
        else { info.text((start + 1) + "-" + (start + shown) + " " + L("VAS_074_Of", "of") + " " + total); }
        $("#vasCilAttr [data-act=attr-prev]").prop("disabled", st.page <= 0);
        $("#vasCilAttr [data-act=attr-next]").prop("disabled", st.page >= pageCount - 1);
    }

    /* Switch the dialog into create/edit mode. editAsi != null -> UPDATE that instance. */
    function openCreateForm(editAsi) {
        st.editAsi = editAsi || null;
        st.mode = "create";
        st.error = "";
        $("#vasCilAttrCreate").html(attrCreateForm());
        renderAttr();
        if (editAsi) prefillCreateForm(editAsi);
    }
    function editExistingInstance(o) {
        if (!o || o.M_AttributeSetInstance_ID <= 0) return;
        openCreateForm(o);
    }
    /* Autofill the create form from an existing instance (set-level + per-attribute). */
    function prefillCreateForm(o) {
        if (o.lot) $("#vasCilAttrLot").val(o.lot);
        if (o.serno) $("#vasCilAttrSerNo").val(o.serno);
        if (o.guaranteeDate) $("#vasCilAttrGuarantee").val(o.guaranteeDate);
        var asi = parseInt(o.M_AttributeSetInstance_ID, 10) || 0;
        if (asi <= 0) return;
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + cfg.ep.getInstanceValues,
            type: "GET", dataType: "json", data: { M_AttributeSetInstance_ID: asi },
            success: function (raw) {
                cfg.BUSY(false);
                if (!st || st.mode !== "create" || !st.editAsi || st.editAsi.M_AttributeSetInstance_ID !== asi) return;
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
            error: function (e) { console.log(e); cfg.BUSY(false); }
        });
    }

    // Error now renders in the footer (left side, red) for BOTH modes - not a toast.
    // Both foots carry a .vas-cil-foot-error span; only the visible one is shown.
    function updateAttrError() {
        $("#vasCilAttr .vas-cil-foot-error").text(st.error || "");
    }
    function updateAttrSubmit() { $("#vasCilAttr [data-act=attr-submit]").prop("disabled", false); }
    function attrErr(name) { st.error = cfg.L("VAS_074_FieldRequired", "Required") + ": " + name; updateAttrError(); }

    /* Apply a result back to the host and close. */
    function apply(result) { var fn = cfg.onApply; closeDialog(); fn(result); }

    function commit() {
        var sel = st.options.filter(function (o) { return optionKey(o) === st.selected; })[0];
        if (!sel) { closeDialog(); return; }
        // Unchanged selection (pre-selected current instance OK'd as-is): no-op close, so the
        // host is not asked to re-apply / re-run a callout.
        var cur = st.M_AttributeSetInstance_ID;
        if (cur > 0 && (parseInt(sel.M_AttributeSetInstance_ID, 10) || 0) === cur) { closeDialog(); return; }
        // Existing instance -> bind it straight to the host (no save needed).
        if (sel.M_AttributeSetInstance_ID > 0) {
            apply({ M_AttributeSetInstance_ID: sel.M_AttributeSetInstance_ID, description: sel.label || sel.code });
            return;
        }
        // Client-created option with no real value id -> display-only.
        if (!sel.M_AttributeValue_ID) {
            cfg.TOAST(cfg.L("VAS_074_AttrNotPersisted", "New attribute is display-only until it exists in the product attribute set"));
            apply({ M_AttributeSetInstance_ID: 0, description: sel.code });
            return;
        }
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + cfg.ep.saveAttribute,
            type: "POST", dataType: "json",
            data: { payload: JSON.stringify({ M_Product_ID: st.M_Product_ID, Lot: "", SerNo: "", GuaranteeDate: "",
                Values: [{ M_Attribute_ID: sel.M_Attribute_ID, ValueType: "L", M_AttributeValue_ID: sel.M_AttributeValue_ID, DisplayValue: sel.label }] }) },
            success: function (raw) {
                cfg.BUSY(false);
                var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                if (res && res.M_AttributeSetInstance_ID > 0) {
                    apply({ M_AttributeSetInstance_ID: res.M_AttributeSetInstance_ID, description: sel.code });
                } else {
                    // Accurate server error (empty -> generic) in the footer, left/red - not a toast.
                    st.error = (res && res.Error) ? res.Error : cfg.L("VAS_074_AttrSaveFailed", "Could not save attribute");
                    updateAttrError();
                }
            },
            error: function (err) { console.log(err); cfg.BUSY(false); st.error = cfg.L("VAS_074_AttrSaveFailed", "Could not save attribute"); updateAttrError(); }
        });
    }

    /* Collect the create-form controls, build a real instance through SaveAttribute. */
    function submitNew() {
        var L = cfg.L;
        var info = st.info || { Attributes: [] };
        st.error = "";
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
                var num = cfg.PNUM(raw);
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
        if (lot) labelParts.push(L("VAS_074_Lot", "Lot") + ": " + lot);
        if (serno) labelParts.push(L("VAS_074_SerialNo", "Serial No") + ": " + serno);
        if (!values.length && !lot && !serno && !guarantee) {
            st.error = L("VAS_074_NothingEntered", "Enter at least one attribute value"); updateAttrError(); return;
        }
        cfg.BUSY(true);
        $.ajax({
            url: cfg.contextUrl + cfg.ep.saveAttribute,
            type: "POST", dataType: "json",
            data: { payload: JSON.stringify({ M_Product_ID: st.M_Product_ID,
                M_AttributeSetInstance_ID: (st.editAsi && st.editAsi.M_AttributeSetInstance_ID) || 0,
                Lot: lot, SerNo: serno, GuaranteeDate: guarantee, Values: values }) },
            success: function (raw) {
                cfg.BUSY(false);
                var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                if (res && res.M_AttributeSetInstance_ID > 0) {
                    apply({ M_AttributeSetInstance_ID: res.M_AttributeSetInstance_ID, description: res.Description || labelParts.join(", ") });
                } else {
                    // Surface the ACCURATE server message (framework mandatory / save error)
                    // inline in the create form (where the user is) and as a toast; fall back
                    // to the generic label only when the server sent no message.
                    st.error = (res && res.Error) ? res.Error : L("VAS_074_AttrSaveFailed", "Could not save attribute");
                    updateAttrError();
                }
            },
            error: function (err) { console.log(err); cfg.BUSY(false); st.error = L("VAS_074_AttrSaveFailed", "Could not save attribute"); updateAttrError(); }
        });
    }

})(VIS, jQuery);
