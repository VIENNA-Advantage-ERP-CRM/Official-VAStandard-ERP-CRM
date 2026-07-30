/************************************************************
 * Module Name    : VAS
 * Purpose        : Expected Landed Cost tab panel for the Purchase Order
 *                  window (C_Order, IsSOTrx = 'N'). A fixed header bar
 *                  (title, PO Number · Order Date · Document Type, state
 *                  badge) above an independently scrolling body holding the
 *                  cost element table, the generated distribution lines of
 *                  each entry and — while the order is drafted — the add /
 *                  edit form.
 *
 *                  Draft (DocStatus = 'DR') is editable; completed
 *                  (DocStatus = 'CO') is strictly read-only, with each
 *                  entry's generated lines expanded by default. The server
 *                  enforces that rule independently.
 *
 *                  Everything shown comes from
 *                  VAS_167_PurchaseOrderLandedCost/GetLandedCostPanel:
 *                  cost elements (M_CostElement), currencies (C_Currency),
 *                  currency rate types (C_ConversionType), the entries
 *                  (C_ExpectedCost) with their amount converted server-side
 *                  into the document currency, and the generated lines
 *                  (C_ExpectedCostDistribution). No exchange rate, cost
 *                  element list or allocation is computed in the browser,
 *                  and this panel carries no Complete Purchase Order action
 *                  (completion is owned by the window's own document action).
 * Chronological development:
 *   VAI163   2026-07-30  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_167_PurchaseOrderLandedCost = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $header;
        var $headSub;
        var $headBadge;
        var $body;
        var $emptyState;
        var data = null;
        // C_ExpectedCost_ID currently being edited; null while adding.
        var editId = null;
        // Generated-lines drawer state, keyed by C_ExpectedCost_ID. Absent means
        // "expanded" — the spec wants the allocation visible without hunting.
        var linesOpen = {};
        // True while a create / update / delete is in flight, so the form cannot
        // be submitted twice.
        var saving = false;

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        var MSG_DEFAULTS = {
            VAS_167_NoData: "No purchase order selected",
            VAS_167_Title: "Expected Landed Cost",
            VAS_167_BadgeExpected: "Expected",
            VAS_167_BadgeGenerated: "Lines Generated",
            VAS_167_DraftNotice: "Draft — expected landed cost is editable. Lines are generated against each distribution type when the PO is completed.",
            VAS_167_NoLinesNotice: "This purchase order has no product lines, so there is nothing to allocate expected landed cost against.",
            VAS_167_NoRateNotice: "At least one entry has no exchange rate for its currency rate type, so it is left out of the converted total.",
            VAS_167_CostElements: "Cost Elements",
            VAS_167_EditableInDraft: "Editable in draft",
            VAS_167_Locked: "Locked",
            VAS_167_ColElement: "Cost Element / Distribution",
            VAS_167_ColAmount: "Amount",
            VAS_167_Empty: "No expected landed cost defined yet — add the first cost element below.",
            VAS_167_EmptyLocked: "No expected landed cost was defined on this purchase order.",
            VAS_167_NoRate: "No exchange rate",
            VAS_167_POTotal: "PO Total",
            VAS_167_ExpectedTotal: "Expected Landed Cost",
            // Row actions
            VAS_167_Edit: "Edit",
            VAS_167_Remove: "Remove",
            VAS_167_Lines: "Lines",
            VAS_167_ShowLines: "Show generated lines",
            VAS_167_HideLines: "Hide generated lines",
            // Generated lines
            VAS_167_GeneratedLines: "Generated Lines",
            VAS_167_ColOrderLine: "Order Line (Product)",
            VAS_167_ColBase: "Base",
            VAS_167_ColQty: "Qty",
            VAS_167_Code: "Code",
            VAS_167_BaseQty: "Qty",
            VAS_167_BaseValue: "Value",
            VAS_167_BaseEqual: "Equal",
            VAS_167_Of: "of",
            VAS_167_Distributed: "Distributed",
            VAS_167_NotReconciled: "Does not add up to the entry amount",
            VAS_167_NoGeneratedLines: "No generated lines for this cost element.",
            // Form
            VAS_167_AddCaption: "Add Expected Landed Cost",
            VAS_167_EditCaption: "Edit Expected Landed Cost",
            VAS_167_FldDistribution: "Cost Distribution",
            VAS_167_FldCostElement: "Cost Element",
            VAS_167_FldAmount: "Amount",
            VAS_167_FldCurrency: "Currency",
            VAS_167_FldRateType: "Currency Rate Type",
            VAS_167_SelectDistribution: "Select type",
            VAS_167_SelectCostElement: "Select element",
            VAS_167_SelectCurrency: "Select currency",
            VAS_167_SelectRateType: "Select rate type",
            VAS_167_BtnAdd: "Add Expected Cost",
            VAS_167_BtnUpdate: "Update",
            VAS_167_BtnCancel: "Cancel",
            VAS_167_NoteIncomplete: "Fill the required fields (*)",
            VAS_167_NoteReadyAdd: "Ready — click Add",
            VAS_167_NoteReadyUpdate: "Ready — click Update",
            VAS_167_Saving: "Saving …",
            VAS_167_HintDraft: "On PO completion, the system generates lines against each distribution type — Order Line (Product), Base, Quantity and Amount.",
            VAS_167_HintCompleted: "This PO is completed — expected landed cost entries cannot be edited.",
            VAS_167_ConfirmRemove: "Remove this expected landed cost entry?",
            VAS_167_SaveFailed: "The expected landed cost could not be saved.",
            VAS_167_DeleteFailed: "The expected landed cost could not be removed."
        };

        // Prefer the seeded AD_Message; else the English default; else the key.
        function getMsg(key) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return MSG_DEFAULTS.hasOwnProperty(key) ? MSG_DEFAULTS[key] : key;
        }

        this.init = function () {
            $root = $('<div class="MPC-vaselc-root"></div>');

            // ---- Fixed header bar (never scrolls) ----
            $header = $('<div class="MPC-vaselc-head"></div>');
            var $htext = $('<div class="MPC-vaselc-headText"></div>');
            $htext.append($('<div class="MPC-vaselc-headTitle"></div>').text(getMsg("VAS_167_Title")));
            $headSub = $('<div class="MPC-vaselc-headSub"></div>');
            $htext.append($headSub);
            $headBadge = $('<span class="MPC-vaselc-badge"></span>');
            $header.append($htext).append($headBadge);

            // ---- Scrolling body ----
            $body = $('<div class="MPC-vaselc-body"></div>');
            $emptyState = $('<div class="MPC-vaselc-noRecord"></div>').text(getMsg("VAS_167_NoData"));
            $emptyState.hide();

            $root.append($header).append($body).append($emptyState);
            createBusyIndicator();
            bindEvents();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                      '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                      '</div>');
            $busy.css({
                "position": "absolute", "width": "100%", "height": "100%",
                "text-align": "center", "z-index": "999"
            });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) return;
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        // ----------------------------------------------------------------- //
        //  Data                                                             //
        // ----------------------------------------------------------------- //

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/GetLandedCostPanel",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    // A newly selected order starts in add mode with every
                    // generated-lines drawer open.
                    editId = null;
                    linesOpen = {};
                    saving = false;
                    render();
                    // Selection always returns the reader to the top of the panel.
                    $body.scrollTop(0);
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
                    showBusy(false);
                }
            });
        };

        this.clear = function () {
            data = null;
            render();
        };

        // ----------------------------------------------------------------- //
        //  Render                                                           //
        // ----------------------------------------------------------------- //

        function render() {
            $body.empty();

            if (!data || !data.PurchaseOrderId) {
                $header.hide();
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $header.show();
            $body.show();

            renderHeaderMeta();

            var $wrap = $('<div class="MPC-vaselc-stack"></div>');

            // Draft-only notice strip. A completed order shows no equivalent
            // strip — the badge and the absent controls already say it is locked.
            if (data.IsDrafted) {
                $wrap.append(noticeStrip("pencil", "warn", getMsg("VAS_167_DraftNotice")));
                if (!data.EligibleLineCount) {
                    $wrap.append(noticeStrip("alert", "risk", getMsg("VAS_167_NoLinesNotice")));
                }
            }
            if (data.HasMissingConversion) {
                $wrap.append(noticeStrip("alert", "risk", getMsg("VAS_167_NoRateNotice")));
            }

            $wrap.append(renderSubCaption());
            $wrap.append(renderTable());

            if (data.IsDrafted) {
                $wrap.append(renderForm());
                $wrap.append(hintRow("info", getMsg("VAS_167_HintDraft")));
            } else {
                $wrap.append(hintRow("checkCircle", getMsg("VAS_167_HintCompleted")));
            }

            $body.append($wrap);
            validateForm();
        }

        // Header sub-line: PO Number · Order Date · Document Type. Re-rendered on
        // every selection so the reader always sees which record they are on.
        function renderHeaderMeta() {
            $headSub.empty();
            $headSub.append($('<b></b>').text(data.PurchaseOrderNumber || ""));
            var bits = [];
            if (data.OrderDate) bits.push(formatDate(data.OrderDate));
            if (data.DocumentTypeName) bits.push(data.DocumentTypeName);
            if (bits.length) $headSub.append(document.createTextNode(" · " + bits.join(" · ")));

            $headBadge.removeClass("is-generated");
            if (data.IsDrafted) {
                $headBadge.text(getMsg("VAS_167_BadgeExpected"));
            } else {
                $headBadge.text(getMsg("VAS_167_BadgeGenerated")).addClass("is-generated");
            }
        }

        function noticeStrip(icon, tone, text) {
            var $s = $('<div class="MPC-vaselc-strip"></div>').addClass("tone-" + tone);
            $s.append(svgIcon(icon));
            $s.append($('<span></span>').text(text));
            return $s;
        }

        function hintRow(icon, text) {
            var $h = $('<div class="MPC-vaselc-hint"></div>');
            $h.append(svgIcon(icon));
            $h.append($('<span></span>').text(text));
            return $h;
        }

        function renderSubCaption() {
            var $row = $('<div class="MPC-vaselc-subcap"></div>');
            var $cap = $('<span class="MPC-vaselc-cap"></span>');
            $cap.append(svgIcon("coins"));
            $cap.append($('<span></span>').text(
                getMsg("VAS_167_CostElements") + " (" + (data.ExpectedCostCount || 0) + ")"));
            $row.append($cap);
            $row.append($('<span class="MPC-vaselc-capHint"></span>').text(
                data.IsDrafted ? getMsg("VAS_167_EditableInDraft") : getMsg("VAS_167_Locked")));
            return $row;
        }

        // ---------- Cost elements table ---------- //

        function renderTable() {
            var $tbl = $('<div class="MPC-vaselc-table"></div>');

            var $head = $('<div class="MPC-vaselc-row MPC-vaselc-rowHead"></div>');
            $head.append($('<span></span>').text(getMsg("VAS_167_ColElement")));
            $head.append($('<span class="ta-r"></span>').text(getMsg("VAS_167_ColAmount")));
            $head.append($('<span></span>'));
            $tbl.append($head);

            var costs = data.ExpectedCosts || [];
            if (!costs.length) {
                $tbl.append($('<div class="MPC-vaselc-empty"></div>').text(
                    data.IsDrafted ? getMsg("VAS_167_Empty") : getMsg("VAS_167_EmptyLocked")));
                return $tbl;
            }

            for (var i = 0; i < costs.length; i++) {
                $tbl.append(buildCostRow(costs[i]));
                // Generated lines belong to a completed order only — a drafted one
                // has nothing generated yet.
                if (!data.IsDrafted) $tbl.append(buildLinesBlock(costs[i]));
            }

            $tbl.append(buildTableFooter());
            return $tbl;
        }

        function buildCostRow(c) {
            var $row = $('<div class="MPC-vaselc-row MPC-vaselc-rowBody"></div>');
            if (editId === c.ExpectedCostId) $row.addClass("is-editing");

            // ---- Identity: element name, distribution chip, meta sub-line ----
            var $id = $('<span></span>');
            $id.append($('<div class="MPC-vaselc-name"></div>')
                .attr("title", c.CostElementName || "")
                .text(c.CostElementName || ""));

            var $chipWrap = $('<div class="MPC-vaselc-chipWrap"></div>');
            var $chip = $('<span class="MPC-vaselc-distChip"></span>')
                .addClass("dist-" + distTone(c.DistributionCode));
            $chip.append($('<span class="MPC-vaselc-dot"></span>'));
            $chip.append($('<span></span>').text(c.DistributionLabel || c.DistributionCode || ""));
            $chipWrap.append($chip);
            $id.append($chipWrap);

            // Meta = the real C_ExpectedCost_ID and the currency rate type; no
            // position-derived identifier is invented.
            var meta = "#" + c.ExpectedCostId;
            if (c.ConversionTypeName) meta += " · " + c.ConversionTypeName;
            $id.append($('<div class="MPC-vaselc-sub"></div>').text(meta));
            if (c.Description) {
                $id.append($('<div class="MPC-vaselc-sub"></div>')
                    .attr("title", c.Description).text(c.Description));
            }
            $row.append($id);

            // ---- Amount: entered amount + converted document-currency line ----
            var $amt = $('<span class="MPC-vaselc-amt"></span>');
            var $v = $('<div class="MPC-vaselc-amtV"></div>');
            $v.append(document.createTextNode(formatNumber(c.EnteredAmount, enteredPrecision(c))));
            $v.append($('<span></span>').text(c.EnteredCurrencyCode || ""));
            $amt.append($v);
            if (!c.IsSameCurrency) {
                if (c.IsConversionAvailable) {
                    $amt.append($('<div class="MPC-vaselc-sub"></div>').text(
                        "≈ " + formatNumber(c.ConvertedAmount, docPrecision()) +
                        " " + (data.DocumentCurrencyCode || "")));
                } else {
                    $amt.append($('<div class="MPC-vaselc-sub is-warn"></div>')
                        .text(getMsg("VAS_167_NoRate")));
                }
            }
            $row.append($amt);

            // ---- Actions: edit / remove in draft, a Lines toggle when completed ----
            var $act = $('<span class="MPC-vaselc-acts"></span>');
            if (data.IsDrafted) {
                $act.append(iconButton("pencil", "edit", getMsg("VAS_167_Edit"))
                    .attr("data-elc-edit", c.ExpectedCostId));
                $act.append(iconButton("trash", "rm", getMsg("VAS_167_Remove"))
                    .attr("data-elc-remove", c.ExpectedCostId));
            } else {
                var open = isLinesOpen(c.ExpectedCostId);
                var $tg = $('<button type="button" class="MPC-vaselc-linesBtn"></button>')
                    .attr("data-elc-lines", c.ExpectedCostId)
                    .attr("aria-expanded", open ? "true" : "false")
                    .attr("aria-label", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"))
                    .attr("title", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"));
                $tg.append($('<span></span>').text(getMsg("VAS_167_Lines")));
                $tg.append(svgIcon("chevDown"));
                if (open) $tg.addClass("is-open");
                $act.append($tg);
            }
            $row.append($act);

            return $row;
        }

        // Generated (server-side) distribution lines for one entry. Read straight
        // from C_ExpectedCostDistribution — never recalculated here.
        function buildLinesBlock(c) {
            var $wrap = $('<div class="MPC-vaselc-linesWrap"></div>')
                .attr("data-elc-linesfor", c.ExpectedCostId);
            if (!isLinesOpen(c.ExpectedCostId)) $wrap.addClass("is-closed");

            var $cap = $('<div class="MPC-vaselc-linesCap"></div>');
            $cap.append(svgIcon("checkCircle"));
            $cap.append($('<span></span>').text(
                getMsg("VAS_167_GeneratedLines") + " · " +
                (c.DistributionLabel || c.DistributionCode || "")));
            $wrap.append($cap);

            var lines = c.GeneratedLines || [];
            if (!lines.length) {
                $wrap.append($('<div class="MPC-vaselc-linesEmpty"></div>')
                    .text(getMsg("VAS_167_NoGeneratedLines")));
                return $wrap;
            }

            var $tbl = $('<div class="MPC-vaselc-lTable"></div>');
            var $lh = $('<div class="MPC-vaselc-lRow MPC-vaselc-lHead"></div>');
            $lh.append($('<span></span>').text(getMsg("VAS_167_ColOrderLine")));
            $lh.append($('<span></span>').text(getMsg("VAS_167_ColBase")));
            $lh.append($('<span class="ta-c"></span>').text(getMsg("VAS_167_ColQty")));
            $lh.append($('<span class="ta-r"></span>').text(getMsg("VAS_167_ColAmount")));
            $tbl.append($lh);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(c, lines[i], lines.length));
            }

            // Footer reports the distributed total in the currency the lines are
            // actually stored in, and — when that differs from the document
            // currency — the document-currency equivalent of the parent amount
            // beside it. The two are never conflated.
            var $foot = $('<div class="MPC-vaselc-lFoot"></div>');
            var $tf = $('<span class="MPC-vaselc-tf"></span>');
            $tf.append(document.createTextNode(getMsg("VAS_167_Distributed")));
            $tf.append($('<b></b>').text(
                formatNumber(c.DistributedAmount, enteredPrecision(c)) +
                " " + (c.EnteredCurrencyCode || "")));
            $foot.append($tf);

            if (!c.IsSameCurrency && c.IsConversionAvailable) {
                var $conv = $('<span class="MPC-vaselc-tf"></span>');
                $conv.append(document.createTextNode("≈"));
                $conv.append($('<b></b>').text(
                    formatNumber(c.ConvertedAmount, docPrecision()) +
                    " " + (data.DocumentCurrencyCode || "")));
                $foot.append($conv);
            }
            if (c.IsReconciled === false) {
                $foot.append($('<span class="MPC-vaselc-tf is-warn"></span>')
                    .text(getMsg("VAS_167_NotReconciled")));
            }
            $tbl.append($foot);

            $wrap.append($tbl);
            return $wrap;
        }

        function buildLineRow(c, g, lineCount) {
            var $row = $('<div class="MPC-vaselc-lRow MPC-vaselc-lBody"></div>');

            var $item = $('<span></span>');
            $item.append($('<div class="MPC-vaselc-name"></div>')
                .attr("title", g.ProductName || "").text(g.ProductName || ""));
            $item.append($('<div class="MPC-vaselc-sub"></div>')
                .text(getMsg("VAS_167_Code") + " " + (g.ProductCode || "")));
            $row.append($item);

            $row.append($('<span class="MPC-vaselc-base"></span>').text(baseLabel(c, g, lineCount)));
            $row.append($('<span class="ta-c"></span>').text(formatNumber(g.LineQuantity, 0)));

            // Stored in the entry's entered currency, so shown in it — the platform
            // converts these amounts to the accounting / invoice currency later,
            // on the accounting date (see the model's currency convention note).
            var $amt = $('<span class="ta-r"></span>');
            $amt.append($('<b></b>').text(formatNumber(g.AllocatedAmount, enteredPrecision(c))));
            $row.append($amt);

            return $row;
        }

        // The audit basis behind each slice, built from the stored Base value and
        // the parent's distribution code.
        function baseLabel(c, g, lineCount) {
            var of = " " + getMsg("VAS_167_Of") + " ";
            switch (c.DistributionCode) {
                case "Q":
                    return getMsg("VAS_167_BaseQty") + " " + formatNumber(g.AllocationBase, 0) +
                           of + formatNumber(g.TotalAllocationBase, 0);
                case "I":
                    return getMsg("VAS_167_BaseValue") + " " + formatNumber(g.AllocationBase, 2) +
                           of + formatNumber(g.TotalAllocationBase, 2);
                case "L":
                    return getMsg("VAS_167_BaseEqual") + " 1" + of + lineCount;
                default:
                    return formatNumber(g.AllocationBase, 2);
            }
        }

        // PO total beside the expected landed cost total — both in the document
        // currency, computed server-side from converted amounts.
        function buildTableFooter() {
            var $foot = $('<div class="MPC-vaselc-foot"></div>');

            var $po = $('<span class="MPC-vaselc-tf"></span>');
            $po.append(document.createTextNode(getMsg("VAS_167_POTotal")));
            $po.append($('<b></b>').text(money(data.PurchaseOrderTotal)));
            $foot.append($po);

            var $grand = $('<span class="MPC-vaselc-grand"></span>');
            $grand.append(document.createTextNode(getMsg("VAS_167_ExpectedTotal")));
            $grand.append($('<b></b>').text(money(data.ExpectedCostTotalConverted)));
            $foot.append($grand);

            return $foot;
        }

        // ---------- Add / edit form (drafted orders only) ---------- //

        function renderForm() {
            var editing = editId !== null;
            var c = editing ? findCost(editId) : null;
            if (editing && !c) { editId = null; editing = false; }

            var $form = $('<div class="MPC-vaselc-form"></div>');

            var $cap = $('<div class="MPC-vaselc-formCap"></div>');
            $cap.append(svgIcon(editing ? "pencil" : "plus"));
            $cap.append($('<span></span>').text(editing
                ? getMsg("VAS_167_EditCaption") + " · #" + c.ExpectedCostId
                : getMsg("VAS_167_AddCaption")));
            $form.append($cap);

            var $grid = $('<div class="MPC-vaselc-formGrid"></div>');
            $grid.append(selectField("elcDist", getMsg("VAS_167_FldDistribution"),
                data.Distributions, "Code", c ? c.DistributionCode : "",
                getMsg("VAS_167_SelectDistribution")));
            $grid.append(selectField("elcElem", getMsg("VAS_167_FldCostElement"),
                data.CostElements, "Id", c ? c.CostElementId : "",
                getMsg("VAS_167_SelectCostElement")));
            $grid.append(amountField(c ? c.EnteredAmount : ""));
            $grid.append(selectField("elcCur", getMsg("VAS_167_FldCurrency"),
                data.Currencies, "Id", c ? c.EnteredCurrencyId : data.DocumentCurrencyId,
                getMsg("VAS_167_SelectCurrency")));
            $grid.append(selectField("elcRate", getMsg("VAS_167_FldRateType"),
                data.ConversionTypes, "Id", c ? c.ConversionTypeId : "",
                getMsg("VAS_167_SelectRateType")));
            $form.append($grid);

            var $foot = $('<div class="MPC-vaselc-formFoot"></div>');
            if (editing) {
                $foot.append($('<button type="button" class="MPC-vaselc-btn is-ghost"></button>')
                    .attr("data-elc-cancel", "1").text(getMsg("VAS_167_BtnCancel")));
            }
            $foot.append($('<span class="MPC-vaselc-formNote"></span>'));
            var $submit = $('<button type="button" class="MPC-vaselc-btn is-primary"></button>')
                .attr("data-elc-save", "1").prop("disabled", true);
            $submit.append(svgIcon(editing ? "check" : "plus"));
            $submit.append($('<span></span>').text(
                editing ? getMsg("VAS_167_BtnUpdate") : getMsg("VAS_167_BtnAdd")));
            $foot.append($submit);
            $form.append($foot);

            var $err = $('<div class="MPC-vaselc-formErr"></div>').hide();
            $form.append($err);

            return $form;
        }

        // Shared underline-only field: label above value, transparent background.
        function fieldShell(label) {
            var $ff = $('<div class="MPC-vaselc-ff"></div>');
            var $content = $('<div class="MPC-vaselc-ffBody"></div>');
            var $label = $('<label></label>');
            $label.append(document.createTextNode(label));
            $label.append($('<span class="MPC-vaselc-req"></span>').text("*"));
            $content.append($label);
            $ff.append($content);
            return { $ff: $ff, $content: $content };
        }

        function selectField(name, label, items, valueKey, selectedValue, placeholder) {
            var shell = fieldShell(label);
            shell.$content.addClass("is-select");

            var $sel = $('<select class="MPC-vaselc-input"></select>').attr("data-elc-field", name);
            $sel.append($('<option value=""></option>').text(placeholder));

            var list = items || [];
            var selected = (selectedValue === null || selectedValue === undefined)
                ? "" : String(selectedValue);
            for (var i = 0; i < list.length; i++) {
                var val = String(list[i][valueKey]);
                var $op = $('<option></option>').attr("value", val).text(list[i].Name || val);
                if (val === selected) $op.prop("selected", true);
                $sel.append($op);
            }
            shell.$content.append($sel);
            return shell.$ff;
        }

        function amountField(value) {
            var shell = fieldShell(getMsg("VAS_167_FldAmount"));
            var $inp = $('<input type="text" class="MPC-vaselc-input is-num" inputmode="decimal" />')
                .attr("data-elc-field", "elcAmt")
                .attr("placeholder", "0.00")
                .val(value === "" || value === null || value === undefined ? "" : String(value));
            shell.$content.append($inp);
            return shell.$ff;
        }

        // ----------------------------------------------------------------- //
        //  Form state / validation                                           //
        // ----------------------------------------------------------------- //

        function formField(name) {
            return $body.find('[data-elc-field="' + name + '"]');
        }

        function readForm() {
            return {
                distributionCode: $.trim(formField("elcDist").val() || ""),
                costElementId:    parseInt(formField("elcElem").val() || "0", 10) || 0,
                amountText:       $.trim(formField("elcAmt").val() || ""),
                currencyId:       parseInt(formField("elcCur").val() || "0", 10) || 0,
                conversionTypeId: parseInt(formField("elcRate").val() || "0", 10) || 0
            };
        }

        // Reads a typed amount without assuming a decimal separator: "1,234.50"
        // and "1.234,50" both mean the same number, so whichever separator comes
        // last is the decimal point and the other is grouping. The result is
        // always sent to the server as an invariant "1234.5" string, so no
        // server-side culture setting can reinterpret it.
        function parseAmountInput(text) {
            var s = String(text || "").replace(/\s/g, "");
            if (!s) return NaN;
            var lastDot = s.lastIndexOf("."), lastComma = s.lastIndexOf(",");
            if (lastDot >= 0 && lastComma >= 0) {
                var dec = lastDot > lastComma ? "." : ",";
                var grp = dec === "." ? /,/g : /\./g;
                s = s.replace(grp, "");
                if (dec === ",") s = s.replace(",", ".");
            } else if (lastComma >= 0) {
                // A single comma is the decimal separator here; a grouping-only
                // value ("1,234") is unusual in an amount field and reading it as
                // 1.234 would understate the cost, so it is rejected instead.
                s = /^-?\d{1,3}(,\d{3})+$/.test(s) ? "NaN" : s.replace(",", ".");
            }
            if (!/^-?\d*(\.\d*)?$/.test(s)) return NaN;
            return parseFloat(s);
        }

        // All five fields are required and the amount must parse to a number
        // greater than zero; the submit button stays inert until then.
        function validateForm() {
            var $btn = $body.find("[data-elc-save]");
            if (!$btn.length) return;

            var v = readForm();
            var amount = parseAmountInput(v.amountText);
            var ok = !!v.distributionCode && v.costElementId > 0 && v.currencyId > 0 &&
                     v.conversionTypeId > 0 && !isNaN(amount) && amount > 0;

            $btn.prop("disabled", !ok || saving);
            var $note = $body.find(".MPC-vaselc-formNote");
            if (saving) {
                $note.text(getMsg("VAS_167_Saving"));
            } else {
                $note.text(ok
                    ? (editId !== null ? getMsg("VAS_167_NoteReadyUpdate") : getMsg("VAS_167_NoteReadyAdd"))
                    : getMsg("VAS_167_NoteIncomplete"));
            }
            return ok;
        }

        function showFormError(message) {
            var $err = $body.find(".MPC-vaselc-formErr");
            if (!$err.length) { toast(message, true); return; }
            $err.text(message).show();
        }

        // ----------------------------------------------------------------- //
        //  Mutations (always server-side)                                    //
        // ----------------------------------------------------------------- //

        function saveCost() {
            if (saving || !validateForm()) return;
            var v = readForm();

            saving = true;
            validateForm();
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/SaveExpectedCost",
                type: "POST",
                dataType: "json",
                data: {
                    C_ExpectedCost_ID: editId === null ? 0 : editId,
                    C_Order_ID: data.PurchaseOrderId,
                    LandedCostDistribution: v.distributionCode,
                    M_CostElement_ID: v.costElementId,
                    Description: "",
                    Amt: String(parseAmountInput(v.amountText)),
                    C_Currency_ID: v.currencyId,
                    C_ConversionType_ID: v.conversionTypeId
                },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    saving = false;
                    if (resp && resp.Success) {
                        // Re-read so the count, footer and totals stay in step
                        // with what the server actually stored.
                        editId = null;
                        $self.fetchData($self.record_ID);
                        return;
                    }
                    showBusy(false);
                    validateForm();
                    showFormError((resp && resp.Message) ? resp.Message : getMsg("VAS_167_SaveFailed"));
                },
                error: function (err) {
                    console.log(err);
                    saving = false;
                    showBusy(false);
                    validateForm();
                    showFormError(getMsg("VAS_167_SaveFailed"));
                }
            });
        }

        function removeCost(id) {
            if (saving) return;
            if (!window.confirm(getMsg("VAS_167_ConfirmRemove"))) return;

            saving = true;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/DeleteExpectedCost",
                type: "POST",
                dataType: "json",
                data: { C_ExpectedCost_ID: id },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    saving = false;
                    if (resp && resp.Success) {
                        // The removed entry may have been the one under edit.
                        if (editId === id) editId = null;
                        $self.fetchData($self.record_ID);
                        return;
                    }
                    showBusy(false);
                    toast((resp && resp.Message) ? resp.Message : getMsg("VAS_167_DeleteFailed"), true);
                },
                error: function (err) {
                    console.log(err);
                    saving = false;
                    showBusy(false);
                    toast(getMsg("VAS_167_DeleteFailed"), true);
                }
            });
        }

        // ----------------------------------------------------------------- //
        //  Events                                                           //
        // ----------------------------------------------------------------- //

        function bindEvents() {
            // Live validation while the reader fills the form.
            $root.on("input", "[data-elc-field]", validateForm);
            $root.on("change", "[data-elc-field]", validateForm);

            $root.on("click", "[data-elc-save]", function () { saveCost(); });

            $root.on("click", "[data-elc-cancel]", function () {
                editId = null;
                render();
            });

            // Editing loads the entry into the same form — no separate dialog.
            $root.on("click", "[data-elc-edit]", function () {
                editId = parseInt($(this).attr("data-elc-edit"), 10);
                render();
                var $first = $body.find('[data-elc-field="elcDist"]');
                if ($first.length) $first.focus();
            });

            $root.on("click", "[data-elc-remove]", function () {
                removeCost(parseInt($(this).attr("data-elc-remove"), 10));
            });

            // Generated-lines drawer, one per entry.
            $root.on("click", "[data-elc-lines]", function () {
                var id = parseInt($(this).attr("data-elc-lines"), 10);
                var open = !isLinesOpen(id);
                linesOpen[id] = open;
                var $btn = $(this);
                $btn.toggleClass("is-open", open)
                    .attr("aria-expanded", open ? "true" : "false")
                    .attr("aria-label", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"))
                    .attr("title", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"));
                $body.find('[data-elc-linesfor="' + id + '"]').toggleClass("is-closed", !open);
            });
        }

        function toast(message, isError) {
            var $t = $('<div class="MPC-vaselc-toast"></div>')
                .addClass(isError ? "err" : "ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("show"); }, 10);
            setTimeout(function () {
                $t.removeClass("show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3600);
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                          //
        // ----------------------------------------------------------------- //

        function findCost(id) {
            var costs = data.ExpectedCosts || [];
            for (var i = 0; i < costs.length; i++) {
                if (costs[i].ExpectedCostId === id) return costs[i];
            }
            return null;
        }

        // Absent state means expanded — the allocation is visible by default.
        function isLinesOpen(id) {
            return linesOpen[id] !== false;
        }

        function distTone(code) {
            switch (code) {
                case "Q": return "qty";
                case "I": return "value";
                case "L": return "equal";
                default:  return "other";
            }
        }

        function docPrecision() {
            var p = data && data.DocumentCurrencyPrecision;
            return (p >= 0) ? p : 2;
        }

        // Precision of the currency the entry — and therefore its generated
        // lines — is denominated in.
        function enteredPrecision(c) {
            var p = c && c.EnteredCurrencyPrecision;
            return (p >= 0) ? p : 2;
        }

        function iconButton(icon, kind, label) {
            var $b = $('<button type="button" class="MPC-vaselc-icBtn"></button>')
                .addClass("is-" + kind)
                .attr("title", label)
                .attr("aria-label", label);
            $b.append(svgIcon(icon));
            return $b;
        }

        var SVG_ICONS = {
            pencil: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            trash: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/></svg>',
            plus: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>',
            check: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            checkCircle: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M9 12l2 2 4-4"/></svg>',
            chevDown: '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>',
            info: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>',
            alert: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4M12 17h.01"/></svg>',
            coins: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vaselc-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 2;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        // Document-currency amount: symbol when the currency has one, else the
        // ISO code, so nothing is ever labelled with a hard-coded currency.
        function money(value) {
            var cur = (data && (data.DocumentCurrencySymbol || data.DocumentCurrencyCode)) || "";
            var formatted = formatNumber(value, docPrecision());
            return cur ? cur + " " + formatted : formatted;
        }

        // Date-only value: keep the stored calendar day, never shift it by zone.
        function formatDate(value) {
            if (!value) return "";
            var s = String(value);
            if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s)) {
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_167_PurchaseOrderLandedCost.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
