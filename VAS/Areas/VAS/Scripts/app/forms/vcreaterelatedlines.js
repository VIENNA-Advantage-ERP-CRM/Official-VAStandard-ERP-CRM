/************************************************************
 * Module Name    : VAS
 * Purpose        : Add Related Products pop-up (AD_Form CreateRelatedLines) on Sales Order and Opportunity.
 *                  Pick a product, then add it together with its related products (mandatory, supplemental,
 *                  alternative, service, web promotion) as lines. Layout follows the Onfinity design used by
 *                  the VAMFG BOM Management dialog.
 * Message keys   : C_Order_ID, C_Project_ID, C_BPartner_ID, Close, M_Product_ID, Type, QtyEntered, C_UOM_ID, PriceActual, LineNetAmt, PriceList,
 *                  M_AttributeSetInstance_ID, All, Change, Details, Cancel, VIS_PlzSelLines,
 *                  VAS_RelProdPickTitle, VAS_RelProdPickHelp, VAS_RelProdNoRelated, VAS_RelProdPickStart,
 *                  VAS_RelProdLinesToAdd, VAS_RelProdAddContinue, VAS_RelProdAddLines,
 *                  VAS_RelProdMandatory, VAS_RelProdNotAdded
 * Chronological development
 * 27 Nov 2026    : Redesigned (single related list with type filters, row details, result inside the pop-up)
 ***********************************************************/
; VIS = window.VIS || {};
; (function (VIS, $) {
    VIS.CreateRelatedLines = function () {
        this.frame;
        this.windowNo;
        this.Record_ID;
        this.Table_ID;
        var $self = this;
        var $root = $('<div class="VAS-relprod-root">');
        var $bsyDiv;
        var $eyebrow, $productArea, $body, $chips, $rows, $selectAll, $result, $hint, $btnCancel, $btnContinue, $btnAdd;

        var ctx = VIS.Env.getCtx();
        var msgs = null;
        var format = VIS.DisplayType.GetNumberFormat(VIS.DisplayType.Amount);
        var dotFormatter = VIS.Env.isDecimalPoint();

        // Framework controls
        var productLookup = null, prdCtrl = null;
        var uomLookup = null, attrLookup = null;
        var detailCtrls = [];       // controls in the open row details
        var mainCtrls = [];         // controls in the main product strip

        // State
        var header = { DocumentNo: "", BPName: "", M_PriceList_ID: 0, ISO_Code: "", StdPrecision: 2 };
        var mainItem = null;        // the picked product line
        var related = [];           // related product lines
        var filter = "all";
        var openDetail = -1;        // index of the related row whose details are open
        var mainDetailOpen = false; // details of the main product open

        // Chip colours per related product type (M_RelatedProduct Type list values)
        var typeClass = { M: "danger", S: "info", A: "accent", E: "ok", P: "warn" };

        /* ---------- helpers ---------- */

        function msg(key) {
            return VIS.Msg.getMsg(key);
        };

        function esc(text) {
            return $('<div>').text(text == null ? "" : String(text)).html();
        };

        function num(value) {
            var n = parseFloat(value);
            return isNaN(n) ? 0 : n;
        };

        // Text for a number input in the user's decimal format
        function toInput(value) {
            var v = format.GetFormatAmount(num(value), "init", dotFormatter);
            return format.GetConvertedString(v, dotFormatter);
        };

        // Number from a number input typed in the user's decimal format
        function fromInput(text) {
            var v = format.GetConvertedNumber(String(text == null ? "" : text), dotFormatter);
            return isNaN(v) ? 0 : v;
        };

        // Amount in the record currency, laid out by the browser culture
        function money(value) {
            var precision = header.StdPrecision >= 0 ? header.StdPrecision : 2;
            if (header.ISO_Code && window.VAS && typeof VAS.formatAmountByBrowser === 'function') {
                return VAS.formatAmountByBrowser(num(value), header.ISO_Code, precision);
            }
            // Older shells without the shared helper: the browser culture still places separators and currency sign
            var opts = { minimumFractionDigits: precision, maximumFractionDigits: precision };
            if (header.ISO_Code) {
                opts.style = "currency";
                opts.currency = header.ISO_Code;
            }
            try {
                return num(value).toLocaleString(navigator.language, opts);
            }
            catch (e) {
                return num(value).toLocaleString(navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
            }
        };

        function setBusy(busy) {
            $bsyDiv[0].style.visibility = busy ? "visible" : "hidden";
        };

        /* ---------- layout ---------- */

        this.initalize = function () {
            msgs = VIS.Msg.translate(ctx, ["M_Product_ID", "QtyEntered", "C_UOM_ID", "PriceActual", "LineNetAmt",
                "PriceList", "M_AttributeSetInstance_ID", "C_Order_ID", "C_Project_ID", "C_BPartner_ID"], true);

            var w = $self.windowNo;
            $root.append(
                // Header: document (type and number) as eyebrow, customer as title
                '<div class="VAS-rp-header">'
                + '  <div class="VAS-rp-header-icon"><i class="fa fa-file-text-o" aria-hidden="true"></i></div>'
                + '  <div class="VAS-rp-title-wrap">'
                + '    <span class="VAS-rp-eyebrow" id="VAS_RPEyebrow_' + w + '"></span>'
                + '    <span class="VAS-rp-title" id="VAS_RPTitle_' + w + '"></span>'
                + '  </div>'
                + '  <button type="button" class="VAS-rp-close" id="VAS_RPClose_' + w + '" title="' + esc(msg("Close")) + '" aria-label="' + esc(msg("Close")) + '" data-msgkey="Close">'
                + '<i class="fa fa-times" aria-hidden="true"></i></button>'
                + '</div>'
                + '<div class="VAS-rp-product" id="VAS_RPProduct_' + w + '">'
                + '  <div class="VAS-rp-search">'
                + '    <label class="VAS-rp-label" data-msgkey="M_Product_ID">' + esc(msgs.M_Product_ID) + ' <span class="VAS-rp-req">*</span></label>'
                + '    <div class="input-group vis-input-wrap" id="VAS_RPProductCtrl_' + w + '"></div>'
                + '  </div>'
                + '  <div class="VAS-rp-picked" id="VAS_RPPicked_' + w + '"></div>'
                + '</div>'
                + '<div class="VAS-rp-body" id="VAS_RPBody_' + w + '">'
                + '  <div class="VAS-rp-empty" id="VAS_RPEmpty_' + w + '">'
                + '    <div class="VAS-rp-empty-icon"><i class="fa fa-link" aria-hidden="true"></i></div>'
                + '    <div class="VAS-rp-empty-title" data-msgkey="VAS_RelProdPickTitle">' + esc(msg("VAS_RelProdPickTitle")) + '</div>'
                + '    <div class="VAS-rp-empty-text" data-msgkey="VAS_RelProdPickHelp">' + esc(msg("VAS_RelProdPickHelp")) + '</div>'
                + '  </div>'
                + '  <div class="VAS-rp-list" id="VAS_RPList_' + w + '">'
                + '    <div class="VAS-rp-chips" id="VAS_RPChips_' + w + '"></div>'
                + '    <div class="VAS-rp-tablewrap">'
                + '      <table class="VAS-rp-table">'
                + '        <thead><tr>'
                + '          <th class="VAS-rp-col-check"><input type="checkbox" id="VAS_RPAll_' + w + '"></th>'
                + '          <th data-msgkey="M_Product_ID">' + esc(msgs.M_Product_ID) + '</th>'
                + '          <th class="VAS-rp-col-type" data-msgkey="Type">' + esc(msg("Type")) + '</th>'
                + '          <th class="VAS-rp-col-num" data-msgkey="QtyEntered">' + esc(msgs.QtyEntered) + '</th>'
                + '          <th class="VAS-rp-col-uom" data-msgkey="C_UOM_ID">' + esc(msgs.C_UOM_ID) + '</th>'
                + '          <th class="VAS-rp-col-num" data-msgkey="PriceActual">' + esc(msgs.PriceActual) + '</th>'
                + '          <th class="VAS-rp-col-amt" data-msgkey="LineNetAmt">' + esc(msgs.LineNetAmt) + '</th>'
                + '          <th class="VAS-rp-col-more"></th>'
                + '        </tr></thead>'
                + '        <tbody id="VAS_RPRows_' + w + '"></tbody>'
                + '      </table>'
                + '      <div class="VAS-rp-none" id="VAS_RPNone_' + w + '" data-msgkey="VAS_RelProdNoRelated">' + esc(msg("VAS_RelProdNoRelated")) + '</div>'
                + '    </div>'
                + '  </div>'
                + '  <div class="VAS-rp-result" id="VAS_RPResult_' + w + '"></div>'
                + '</div>'
                + '<div class="VAS-rp-footer">'
                + '  <span class="VAS-rp-hint" id="VAS_RPHint_' + w + '"></span>'
                + '  <div class="VAS-rp-actions">'
                + '    <button type="button" class="VAS-rp-btn VAS-rp-btn-secondary" id="VAS_RPCancel_' + w + '" data-msgkey="Cancel">' + esc(msg("Cancel")) + '</button>'
                + '    <button type="button" class="VAS-rp-btn VAS-rp-btn-secondary" id="VAS_RPContinue_' + w + '" data-msgkey="VAS_RelProdAddContinue">' + esc(msg("VAS_RelProdAddContinue")) + '</button>'
                + '    <button type="button" class="VAS-rp-btn VAS-rp-btn-primary" id="VAS_RPAdd_' + w + '" data-msgkey="VAS_RelProdAddLines"><i class="fa fa-check" aria-hidden="true"></i>' + esc(msg("VAS_RelProdAddLines")) + '</button>'
                + '  </div>'
                + '</div>');

            $bsyDiv = $("<div class='vis-apanel-busy VAS-rp-busy'>");
            $root.append($bsyDiv);
            setBusy(true);
        };

        this.intialLoad = function () {
            var w = $self.windowNo;
            $eyebrow = $root.find("#VAS_RPEyebrow_" + w);
            $productArea = $root.find("#VAS_RPProduct_" + w);
            $body = $root.find("#VAS_RPBody_" + w);
            $chips = $root.find("#VAS_RPChips_" + w);
            $rows = $root.find("#VAS_RPRows_" + w);
            $selectAll = $root.find("#VAS_RPAll_" + w);
            $result = $root.find("#VAS_RPResult_" + w);
            $hint = $root.find("#VAS_RPHint_" + w);
            $btnCancel = $root.find("#VAS_RPCancel_" + w);
            $btnContinue = $root.find("#VAS_RPContinue_" + w);
            $btnAdd = $root.find("#VAS_RPAdd_" + w);

            loadControls();
            bindEvents();
            showEmpty();
            loadHeader();
        };

        // Product search (framework control), attribute and UOM lookups for the row details
        function loadControls() {
            productLookup = VIS.MLookupFactory.getMLookUp(ctx, $self.windowNo, 2221, VIS.DisplayType.Search);
            prdCtrl = new VIS.Controls.VTextBoxButton("M_Product_ID", true, false, true, VIS.DisplayType.Search, productLookup);
            var $wrap = $('<div class="vis-control-wrap">');
            var $btns = $('<div class="input-group-append">');
            $wrap.append(prdCtrl.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            var $ctrl = $root.find("#VAS_RPProductCtrl_" + $self.windowNo);
            $ctrl.append($wrap).append($btns);
            for (var i = 0; i < prdCtrl.getBtnCount(); i++) {
                $btns.append(prdCtrl.getBtn(i));
            }
            ctx.setContext($self.windowNo, "M_Product_ID", null);

            attrLookup = new VIS.MPAttributeLookup(VIS.context, $self.windowNo);
            uomLookup = VIS.MLookupFactory.getMLookUp(ctx, $self.windowNo, 2222, VIS.DisplayType.TableDir);
        };

        // Document number, business partner, price list and currency of the order / opportunity
        function loadHeader() {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "RelatedProduct/GetHeaderInfo",
                { "Table_ID": $self.Table_ID, "Record_ID": $self.Record_ID }, function (data) {
                    if (data != null) {
                        header = data;
                        // Document type of the order (from the server, translated); otherwise the element name
                        var docKey = $self.Table_ID == 259 ? "C_Order_ID" : "C_Project_ID";
                        var docLabel = data.DocTypeName
                            ? '<span>' + esc(data.DocTypeName) + '</span>'
                            : '<span data-msgkey="' + docKey + '">' + esc(msgs[docKey]) + '</span>';
                        $eyebrow.html(docLabel + ' · <strong>' + esc(data.DocumentNo) + '</strong>');
                        $root.find("#VAS_RPTitle_" + $self.windowNo).text(data.BPName || "")
                            .attr("title", msgs.C_BPartner_ID + ": " + (data.BPName || "")).attr("data-msgkey", "C_BPartner_ID");
                    }
                    setBusy(false);
                });
        };

        /* ---------- data ---------- */

        function loadProduct(productId) {
            setBusy(true);
            clearResult();
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "RelatedProduct/GetProductData", {
                "M_Product_ID": productId, "M_AttributeSetInstance_ID": 0, "C_UOM_ID": 0,
                "M_PriceList_ID": header.M_PriceList_ID, "Table_ID": $self.Table_ID, "Record_ID": $self.Record_ID
            }, function (data) {
                if (data == null) {
                    setBusy(false);
                    return;
                }
                mainItem = toItem(data, true);
                VIS.dataContext.getJSONData(VIS.Application.contextUrl + "RelatedProduct/GetRelatedProduct", {
                    "M_Product_ID": productId, "M_AttributeSetInstance_ID": 0, "C_UOM_ID": 0,
                    "M_PriceList_ID": header.M_PriceList_ID, "RelatedProductType": "",
                    "Table_ID": $self.Table_ID, "Record_ID": $self.Record_ID
                }, function (list) {
                    related = [];
                    if (list != null) {
                        for (var i = 0; i < list.length; i++) {
                            related.push(toItem(list[i], false));
                        }
                    }
                    filter = "all";
                    openDetail = -1;
                    mainDetailOpen = false;
                    showPicked();
                    setBusy(false);
                });
            });
        };

        function toItem(d, isMain) {
            var mandatory = !isMain && d.RelatedProductType == "M";
            return {
                Product_ID: d.Product_ID, Product: d.Product, Value: d.Value, RelationName: d.RelationName || "",
                RelatedProductType: d.RelatedProductType, RelatedType: d.RelatedType,
                AttributeSetInstance_ID: d.AttributeSetInstance_ID || 0, UOM_ID: d.UOM_ID,
                QtyEntered: num(d.QtyEntered) || 1, PriceStd: num(d.PriceStd), PriceList: num(d.PriceList),
                selected: isMain || mandatory, mandatory: mandatory
            };
        };

        // New price after the unit or attribute of a line changed
        function reprice(item, onDone) {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "RelatedProduct/GetProductData", {
                "M_Product_ID": item.Product_ID, "M_AttributeSetInstance_ID": item.AttributeSetInstance_ID,
                "C_UOM_ID": item.UOM_ID, "M_PriceList_ID": header.M_PriceList_ID,
                "Table_ID": $self.Table_ID, "Record_ID": $self.Record_ID
            }, function (data) {
                if (data != null) {
                    item.PriceStd = num(data.PriceStd);
                    item.PriceList = num(data.PriceList);
                }
                onDone();
            });
        };

        /* ---------- rendering ---------- */

        function showEmpty() {
            mainItem = null;
            related = [];
            disposeDetailCtrls();
            $productArea.removeClass("VAS-rp-has-product");
            $body.removeClass("VAS-rp-has-product");
            prdCtrl.setValue(null, false, true);
            updateFooter();
        };

        function showPicked() {
            $productArea.addClass("VAS-rp-has-product");
            $body.addClass("VAS-rp-has-product");
            renderPicked();
            renderChips();
            renderRows();
            updateFooter();
        };

        function renderPicked() {
            var it = mainItem;
            var w = $self.windowNo;
            var html = '<div class="VAS-rp-picked-icon"><i class="fa fa-cube" aria-hidden="true"></i></div>'
                + '<div class="VAS-rp-picked-text">'
                + '  <span class="VAS-rp-code">' + esc(it.Value) + '</span>'
                + '  <span class="VAS-rp-name">' + esc(it.Product) + '</span>'
                + '</div>'
                + '<div class="VAS-rp-picked-fields">'
                + '  <label data-msgkey="QtyEntered">' + esc(msgs.QtyEntered)
                + '    <input type="text" inputmode="decimal" class="VAS-rp-input VAS-rp-qty" data-main="qty" value="' + esc(toInput(it.QtyEntered)) + '"></label>'
                + '  <span class="VAS-rp-uom-text">' + esc(uomDisplay(it.UOM_ID)) + '</span>'
                + '  <label data-msgkey="PriceActual">' + esc(msgs.PriceActual)
                + '    <input type="text" inputmode="decimal" class="VAS-rp-input VAS-rp-price" data-main="price" value="' + esc(toInput(it.PriceStd)) + '"></label>'
                + '</div>'
                + '<div class="VAS-rp-picked-actions">'
                + '<button type="button" class="VAS-rp-btn VAS-rp-btn-secondary VAS-rp-btn-small" data-main="details" data-msgkey="Details">'
                + '<i class="fa fa-sliders" aria-hidden="true"></i>' + esc(msg("Details")) + '</button>'
                + '<button type="button" class="VAS-rp-btn VAS-rp-btn-secondary VAS-rp-btn-small" id="VAS_RPChange_' + w + '" data-msgkey="Change">'
                + '<i class="fa fa-search" aria-hidden="true"></i>' + esc(msg("Change")) + '</button>'
                + '</div>'
                + '<div class="VAS-rp-picked-detail" id="VAS_RPMainDetail_' + w + '"></div>';
            disposeCtrls(mainCtrls);
            $root.find("#VAS_RPPicked_" + w).html(html);
            if (mainDetailOpen) {
                buildDetail(mainItem, $root.find("#VAS_RPMainDetail_" + w), mainCtrls, true);
            }
        };

        function uomDisplay(uomId) {
            var d = uomLookup ? uomLookup.getDisplay(uomId) : "";
            return d == null ? "" : d;
        };

        function renderChips() {
            var counts = {}, names = {}, order = [];
            for (var i = 0; i < related.length; i++) {
                var t = related[i].RelatedProductType;
                if (!counts[t]) {
                    counts[t] = 0;
                    names[t] = related[i].RelatedType || t;
                    order.push(t);
                }
                counts[t]++;
            }
            var html = '<button type="button" class="VAS-rp-chip VAS-rp-filter' + (filter == "all" ? ' VAS-rp-on' : '') + '" data-filter="all" data-msgkey="All">'
                + esc(msg("All")) + ' <span>' + related.length + '</span></button>';
            for (var j = 0; j < order.length; j++) {
                html += '<button type="button" class="VAS-rp-chip VAS-rp-filter' + (filter == order[j] ? ' VAS-rp-on' : '') + '" data-filter="' + esc(order[j]) + '">'
                    + esc(names[order[j]]) + ' <span>' + counts[order[j]] + '</span></button>';
            }
            $chips.html(html).toggle(related.length > 0);
        };

        function renderRows() {
            disposeCtrls(detailCtrls);
            var html = "";
            var visible = 0;
            for (var i = 0; i < related.length; i++) {
                var it = related[i];
                if (filter != "all" && it.RelatedProductType != filter) {
                    continue;
                }
                visible++;
                var chip = typeClass[it.RelatedProductType] || "mute";
                html += '<tr class="VAS-rp-row' + (it.selected ? ' VAS-rp-selected' : '') + '" data-index="' + i + '">'
                    + '<td class="VAS-rp-col-check"><input type="checkbox" data-index="' + i + '"' + (it.selected ? ' checked' : '')
                    + (it.mandatory ? ' disabled title="' + esc(msg("VAS_RelProdMandatory")) + '" data-msgkey="VAS_RelProdMandatory"' : '') + '></td>'
                    + '<td><span class="VAS-rp-name">' + esc(it.Product) + '</span><span class="VAS-rp-sub"><span class="VAS-rp-code">' + esc(it.Value) + '</span>'
                    + (it.RelationName && it.RelationName != it.Product ? '<span class="VAS-rp-relname">' + esc(it.RelationName) + '</span>' : '') + '</span></td>'
                    + '<td class="VAS-rp-col-type"><span class="VAS-rp-chip VAS-rp-' + chip + '">' + esc(it.RelatedType || it.RelatedProductType) + '</span></td>'
                    + '<td class="VAS-rp-col-num"><input type="text" inputmode="decimal" class="VAS-rp-input VAS-rp-qty" data-qty="' + i + '" value="' + esc(toInput(it.QtyEntered)) + '"></td>'
                    + '<td class="VAS-rp-col-uom">' + esc(uomDisplay(it.UOM_ID)) + '</td>'
                    + '<td class="VAS-rp-col-num"><input type="text" inputmode="decimal" class="VAS-rp-input VAS-rp-price" data-price="' + i + '" value="' + esc(toInput(it.PriceStd)) + '"></td>'
                    + '<td class="VAS-rp-col-amt" data-amt="' + i + '">' + esc(money(it.QtyEntered * it.PriceStd)) + '</td>'
                    + '<td class="VAS-rp-col-more"><button type="button" class="VAS-rp-iconbtn' + (openDetail == i ? ' VAS-rp-open' : '') + '" data-detail="' + i + '" title="' + esc(msg("Details")) + '" data-msgkey="Details">'
                    + '<i class="fa fa-chevron-down" aria-hidden="true"></i></button></td>'
                    + '</tr>';
                if (openDetail == i) {
                    html += '<tr class="VAS-rp-detailrow"><td></td><td colspan="7"><div class="VAS-rp-detail" data-detailfor="' + i + '"></div></td></tr>';
                }
            }
            $rows.html(html);
            $root.find("#VAS_RPNone_" + $self.windowNo).toggle(related.length == 0);
            $root.find(".VAS-rp-tablewrap table").toggle(related.length > 0);
            if (openDetail >= 0) {
                buildDetail(related[openDetail], $rows.find('[data-detailfor="' + openDetail + '"]'), detailCtrls, true);
            }
            updateSelectAll();
        };

        // Attribute set instance field of one line (framework control). The attribute dialog reads the product
        // from the context, so it is set again whenever the user works with this field.
        function buildAttr(item, $container, ctrls) {
            ctx.setContext($self.windowNo, "M_Product_ID", item.Product_ID);
            var attrCtrl = new VIS.Controls.VPAttribute("M_AttributeSetInstance_ID", false, false, true, VIS.DisplayType.PAttribute,
                attrLookup, $self.windowNo, false, false, false, true);
            ctrls.push(attrCtrl);
            var $wrap = $('<div class="VAS-rp-attr input-group"></div>');
            $wrap.append(attrCtrl.getControl());
            for (var b = 0; b < attrCtrl.getBtnCount(); b++) {
                $wrap.append(attrCtrl.getBtn(b));
            }
            $wrap.on("mousedown focusin", function () {
                ctx.setContext($self.windowNo, "M_Product_ID", item.Product_ID);
            });
            $container.empty().append('<label data-msgkey="M_AttributeSetInstance_ID">' + esc(msgs.M_AttributeSetInstance_ID) + '</label>').append($wrap);
            attrCtrl.setValue(item.AttributeSetInstance_ID || 0);
            attrCtrl.fireValueChanged = function () {
                item.AttributeSetInstance_ID = VIS.Utility.Util.getValueOfInt(attrCtrl.getValue());
                reprice(item, refreshAll);
            };
        };

        // Unit, attribute set instance and list price of a line (opened with Details)
        function buildDetail(item, $container, ctrls, withAttr) {
            ctx.setContext($self.windowNo, "M_Product_ID", item.Product_ID);
            // Not mandatory: a line always has a unit, and the mandatory style would show it red
            var uomCtrl = new VIS.Controls.VComboBox("C_UOM_ID", false, false, true, uomLookup, 50);
            ctrls.push(uomCtrl);

            var $uom = $('<div class="VAS-rp-field"><label data-msgkey="C_UOM_ID">' + esc(msgs.C_UOM_ID) + '</label></div>');
            $uom.append(uomCtrl.getControl());
            var $list = $('<div class="VAS-rp-field"><label data-msgkey="PriceList">' + esc(msgs.PriceList) + '</label>'
                + '<input type="text" inputmode="decimal" class="VAS-rp-input" value="' + esc(toInput(item.PriceList)) + '"></div>');
            $container.empty().append($uom);
            if (withAttr) {
                var $attr = $('<div class="VAS-rp-field"></div>');
                $container.append($attr);
                buildAttr(item, $attr, ctrls);
            }
            $container.append($list);

            uomCtrl.setValue(item.UOM_ID);
            uomCtrl.fireValueChanged = function () {
                var v = VIS.Utility.Util.getValueOfInt(uomCtrl.getValue());
                if (v > 0 && v != item.UOM_ID) {
                    item.UOM_ID = v;
                    reprice(item, refreshAll);
                }
            };
            $list.find("input").on("change", function () {
                item.PriceList = fromInput($(this).val());
                $(this).val(toInput(item.PriceList));
            });
        };

        function refreshAll() {
            renderPicked();
            renderRows();
            updateFooter();
        };

        // Disposes the framework controls in the list and empties it (the same array stays in use)
        function disposeCtrls(ctrls) {
            for (var i = 0; i < ctrls.length; i++) {
                if (ctrls[i] && ctrls[i].dispose) {
                    ctrls[i].dispose();
                }
            }
            ctrls.length = 0;
        };

        // Controls of the main product strip and of the open row details
        function disposeDetailCtrls() {
            disposeCtrls(mainCtrls);
            disposeCtrls(detailCtrls);
        };

        function updateSelectAll() {
            var all = true, any = false;
            for (var i = 0; i < related.length; i++) {
                if (related[i].mandatory || (filter != "all" && related[i].RelatedProductType != filter)) {
                    continue;
                }
                any = true;
                if (!related[i].selected) {
                    all = false;
                }
            }
            $selectAll.prop("checked", any && all).prop("disabled", !any);
        };

        function selectedLines() {
            var lines = [];
            if (mainItem == null) {
                return lines;
            }
            lines.push(mainItem);
            for (var i = 0; i < related.length; i++) {
                if (related[i].selected) {
                    lines.push(related[i]);
                }
            }
            return lines;
        };

        function updateFooter() {
            var lines = selectedLines();
            if (lines.length == 0) {
                $hint.text(msg("VAS_RelProdPickStart")).attr("data-msgkey", "VAS_RelProdPickStart");
            }
            else {
                var total = 0;
                for (var i = 0; i < lines.length; i++) {
                    total += lines[i].QtyEntered * lines[i].PriceStd;
                }
                $hint.html('<span data-msgkey="VAS_RelProdLinesToAdd">' + esc(msg("VAS_RelProdLinesToAdd")) + '</span> <strong>'
                    + lines.length + '</strong> · <strong>' + esc(money(total)) + '</strong>').removeAttr("data-msgkey");
            }
            $btnContinue.prop("disabled", lines.length == 0);
            $btnAdd.prop("disabled", lines.length == 0);
        };

        /* ---------- result ---------- */

        function clearResult() {
            $result.removeClass("VAS-rp-ok VAS-rp-warn").empty().hide();
        };

        function showResult(res) {
            var ok = res.Errors == null || res.Errors.length == 0;
            var html = '<i class="fa ' + (ok ? 'fa-check-circle' : 'fa-exclamation-triangle') + '" aria-hidden="true"></i><div>'
                + '<div>' + esc(res.Message) + '</div>';
            if (!ok) {
                html += '<div><span data-msgkey="VAS_RelProdNotAdded">' + esc(msg("VAS_RelProdNotAdded")) + '</span></div><ul>';
                for (var i = 0; i < res.Errors.length; i++) {
                    html += '<li><strong>' + esc(res.Errors[i].Product) + '</strong>'
                        + (res.Errors[i].Error ? ' — ' + esc(res.Errors[i].Error) : '') + '</li>';
                }
                html += '</ul>';
            }
            html += '</div>';
            $result.removeClass("VAS-rp-ok VAS-rp-warn").addClass(ok ? "VAS-rp-ok" : "VAS-rp-warn").html(html).show();
        };

        /* ---------- save ---------- */

        function save(closeAfter) {
            var lines = selectedLines();
            if (lines.length == 0) {
                VIS.ADialog.error("VIS_PlzSelLines");
                return;
            }
            var data = [];
            for (var i = 0; i < lines.length; i++) {
                data.push({
                    Product_ID: lines[i].Product_ID, Product: lines[i].Product, Value: lines[i].Value,
                    AttributeSetInstance_ID: lines[i].AttributeSetInstance_ID, UOM_ID: lines[i].UOM_ID,
                    RelatedProductType: lines[i].RelatedProductType, RelatedType: lines[i].RelatedType,
                    QtyEntered: lines[i].QtyEntered, PriceStd: lines[i].PriceStd, PriceList: lines[i].PriceList
                });
            }
            setBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "RelatedProduct/SaveRelatedLines",
                type: "POST",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: JSON.stringify({ Data: data, Table_ID: $self.Table_ID, Record_ID: $self.Record_ID }),
                success: function (result) {
                    setBusy(false);
                    var res = result ? JSON.parse(result) : null;
                    if (res == null) {
                        return;
                    }
                    // Show the saved lines in the window behind the pop-up
                    if (res.Saved > 0 && $self.frame && $self.frame.gridDataRefreshAll) {
                        $self.frame.gridDataRefreshAll();
                    }
                    var failed = res.Errors || [];
                    if (failed.length == 0) {
                        if (closeAfter) {
                            $self.frame.close();
                            return;
                        }
                        showEmpty();
                        showResult(res);
                        return;
                    }
                    keepFailedLines(lines, failed);
                    showResult(res);
                },
                error: function () {
                    setBusy(false);
                }
            });
        };

        // After a partial save only the failed lines stay in the pop-up, so a retry does not add the others twice
        function keepFailedLines(sent, failed) {
            var failedItems = [];
            for (var i = 0; i < failed.length; i++) {
                if (sent[failed[i].LineIndex]) {
                    failedItems.push(sent[failed[i].LineIndex]);
                }
            }
            if (failedItems.indexOf(mainItem) < 0) {
                // Main product was added: keep the remaining related lines as optional extra lines
                mainItem.selected = false;
            }
            var keep = [];
            for (var j = 0; j < related.length; j++) {
                if (failedItems.indexOf(related[j]) >= 0) {
                    keep.push(related[j]);
                }
            }
            related = keep;
            if (failedItems.indexOf(mainItem) < 0) {
                // The main product is on the order now; offer the failed related lines on their own
                if (related.length > 0) {
                    mainItem = related.shift();
                    mainItem.selected = true;
                    mainItem.mandatory = false;
                }
                else {
                    showEmpty();
                    return;
                }
            }
            openDetail = -1;
            filter = "all";
            showPicked();
        };

        /* ---------- events ---------- */

        function bindEvents() {
            prdCtrl.fireValueChanged = function () {
                if (prdCtrl.value != null) {
                    loadProduct(prdCtrl.value);
                }
            };

            $productArea.on("click", "#VAS_RPChange_" + $self.windowNo, function () {
                clearResult();
                showEmpty();
            });

            $productArea.on("click", '[data-main="details"]', function () {
                mainDetailOpen = !mainDetailOpen;
                renderPicked();
            });

            $productArea.on("change", '[data-main="qty"]', function () {
                mainItem.QtyEntered = fromInput($(this).val()) || 1;
                $(this).val(toInput(mainItem.QtyEntered));
                updateFooter();
            });

            $productArea.on("change", '[data-main="price"]', function () {
                mainItem.PriceStd = fromInput($(this).val());
                $(this).val(toInput(mainItem.PriceStd));
                updateFooter();
            });

            $chips.on("click", ".VAS-rp-filter", function () {
                filter = $(this).attr("data-filter");
                openDetail = -1;
                renderChips();
                renderRows();
            });

            $rows.on("change", 'input[type="checkbox"]', function () {
                var it = related[VIS.Utility.Util.getValueOfInt($(this).attr("data-index"))];
                if (it && !it.mandatory) {
                    it.selected = $(this).prop("checked");
                    $(this).closest("tr").toggleClass("VAS-rp-selected", it.selected);
                    updateSelectAll();
                    updateFooter();
                }
            });

            $rows.on("change", "[data-qty]", function () {
                var i = VIS.Utility.Util.getValueOfInt($(this).attr("data-qty"));
                related[i].QtyEntered = fromInput($(this).val()) || 1;
                $(this).val(toInput(related[i].QtyEntered));
                $rows.find('[data-amt="' + i + '"]').text(money(related[i].QtyEntered * related[i].PriceStd));
                updateFooter();
            });

            $rows.on("change", "[data-price]", function () {
                var i = VIS.Utility.Util.getValueOfInt($(this).attr("data-price"));
                related[i].PriceStd = fromInput($(this).val());
                $(this).val(toInput(related[i].PriceStd));
                $rows.find('[data-amt="' + i + '"]').text(money(related[i].QtyEntered * related[i].PriceStd));
                updateFooter();
            });

            $rows.on("click", "[data-detail]", function () {
                var i = VIS.Utility.Util.getValueOfInt($(this).attr("data-detail"));
                openDetail = openDetail == i ? -1 : i;
                renderRows();
            });

            $selectAll.on("change", function () {
                var on = $(this).prop("checked");
                for (var i = 0; i < related.length; i++) {
                    if (!related[i].mandatory && (filter == "all" || related[i].RelatedProductType == filter)) {
                        related[i].selected = on;
                    }
                }
                renderRows();
                updateFooter();
            });

            $btnCancel.on("click", function () {
                $self.frame.close();
            });

            // Cross in the header closes the form, same as Cancel
            $root.find("#VAS_RPClose_" + $self.windowNo).on("click", function () {
                $self.frame.close();
            });

            $btnContinue.on("click", function () {
                save(false);
            });

            $btnAdd.on("click", function () {
                save(true);
            });
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            disposeDetailCtrls();
            if ($root) {
                $root.off().find("*").off();
                $root.remove();
            }
            if (prdCtrl && prdCtrl.dispose) {
                prdCtrl.dispose();
            }
            prdCtrl = null;
            productLookup = null;
            uomLookup = null;
            attrLookup = null;
            related = null;
            mainItem = null;
            $root = null;
            $bsyDiv = null;
        };
    };

    VIS.CreateRelatedLines.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.Record_ID = this.frame.getRecord_ID();
        this.Table_ID = this.frame.getAD_Table_ID();
        this.initalize();
        this.intialLoad();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.CreateRelatedLines.prototype.setHeight = function () {
        return "540";
    };

    VIS.CreateRelatedLines.prototype.setWidth = function () {
        return "900";
    };

    VIS.CreateRelatedLines.prototype.dispose = function () {
        this.disposeComponent();
        this.frame = null;
        this.windowNo = null;
    };
})(VIS, jQuery);
