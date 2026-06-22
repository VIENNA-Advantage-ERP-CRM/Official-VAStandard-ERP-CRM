/************************************************************
 * Module Name    : VAS
 * Purpose        : AP Invoice / AP Credit Note overview tab panel.
 *                  Renders the contextual purchase-invoice panel (header,
 *                  KPI cards, lifecycle, invoice details, landed-cost banner,
 *                  line items, totals, withholding, 3-way match insight,
 *                  landed-cost distribution, goods receipt, payment schedule,
 *                  approval, posted journal) and the dual-mode Record Payment /
 *                  Allocate Credit Note modal.
 * chronological  : Development
 *   VAI_145        Created  16 June 2026
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_065_APInvoicePanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;
        var meta = null;
        var $scrim = null;
        var LINES_PER_PAGE = 5;
        var linePage = 0;

        /* ---------- short helpers ---------- */
        // Returns the dictionary message for `key`. VIS.Msg.getMsg wraps unknown
        // keys in '[...]'; when that happens we use `fallback` if one was supplied,
        // otherwise the raw '[KEY]' is kept (so callers without a fallback are unchanged).
        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            if (t && t.charAt(0) !== '[') return t;
            return (fallback !== undefined) ? fallback : t;
        }

        function fmtAmount(value, precision) {
            var v = +value || 0;
            var sign = v < 0 ? "-" : "";
            var abs = Math.abs(v);
            var cur = (data && (data.CurSymbol || data.CurISO)) || "";
            var p = (precision >= 0) ? precision : ((data && data.StdPrecision >= 0) ? data.StdPrecision : 2);
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + (cur ? cur : "") + formatted;
        }

        function fmtAmountCur(value, cur, precision) {
            var v = +value || 0;
            var p = (precision >= 0) ? precision : 2;
            return (cur ? cur : "") + Math.abs(v).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function fmtNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function fmtPct(value) {
            return fmtNumber(value, 2) + "%";
        }

        function fmtDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) { return d.toDateString(); }
        }

        /* Whole-day difference between a date and today (positive = future, negative = past). */
        function dayDelta(value) {
            if (!value) return 0;
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return 0;
            var due = new Date(d.getFullYear(), d.getMonth(), d.getDate());
            var now = new Date();
            var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            return Math.round((due - today) / 86400000);
        }

        function docStatusLabel(code) {
            switch (code) {
                case "DR": return lbl("VAS_065_Drafted", "Drafted");
                case "IP": return lbl("VAS_065_InProgress", "In progress");
                case "CO": return lbl("VAS_065_Completed", "Completed");
                case "CL": return lbl("VAS_065_Closed", "Closed");
                case "VO": return lbl("VAS_065_Voided", "Voided");
                case "RE": return lbl("VAS_065_Reversed", "Reversed");
            }
            return code || "";
        }

        /* ---------- lifecycle ---------- */
        this.init = function () {
            $root = $('<div class="vas-apinv-root"></div>');
            $body = $('<div class="vas-apinv-body"></div>');
            $emptyState = $('<div class="vas-apinv-empty" style="display:none;"></div>');
            $emptyState.text(lbl("VAS_065_NoData", "No data to display"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>');
            $busy.css({ "position": "absolute", "width": "100%", "height": "100%", "text-align": "center", "z-index": "999" });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) return;
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_065_APInvoicePanel/GetPanelData",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: recordID },
                success: function (raw) {
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    linePage = 0;
                    render();
                    showBusy(false);
                },
                error: function (err) { console.log(err); showBusy(false); }
            });
        };

        this.clear = function () { data = null; render(); };

        /* ---------- render ---------- */
        function render() {
            $body.empty();
            if (!data || !data.C_Invoice_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }
            $emptyState.hide();
            $body.show();

            var $panel = $('<div class="vas-apinv-panel"></div>');
            $panel.append(buildHeader());
            $panel.append(buildActions());
            $panel.append(buildKpis());

            var $life = buildLifecycle();
            if ($life) $panel.append($life);

            var $details = buildInvoiceDetails();
            if ($details) $panel.append($details);

            var $lcBanner = buildLandedCostBanner();
            if ($lcBanner) $panel.append($lcBanner);

            var $lines = buildLineItems();
            if ($lines) $panel.append($lines);

            $panel.append(buildTotals());

            var $wh = buildWithholding();
            if ($wh) $panel.append($wh);

            var $insight = buildMatchInsight();
            if ($insight) $panel.append($insight);

            var $lcDist = buildLandedCostDistribution();
            if ($lcDist) $panel.append($lcDist);

            var $twoCol = buildGoodsReceiptAndSchedule();
            if ($twoCol) $panel.append($twoCol);

            var $approval = buildApproval();
            if ($approval) $panel.append($approval);

            var $journal = buildPostedJournal();
            if ($journal) $panel.append($journal);

            $body.append($panel);
        }

        /* Header (vendor name + context line) */
        function buildHeader() {
            var $h = $(
                '<header class="vas-apinv-head">' +
                '<div class="vas-apinv-headLeft">' +
                '<div class="vas-apinv-h1 js-bp-name"></div>' +
                '<div class="vas-apinv-sub js-bp-sub"></div>' +
                '</div>' +
                '<div class="vas-apinv-ctx">' +
                '<b class="js-doc-no"></b>' +
                '<span class="js-doc-type"></span>' +
                '<span class="js-doc-date"></span>' +
                '</div>' +
                '</header>'
            );
            $h.find(".js-bp-name").text(data.BPName || "");
            // Sub-line: vendor location (city, country) followed by BP code.
            var locBits = [];
            if (data.BPCity) locBits.push(data.BPCity);
            if (data.BPCountry) locBits.push(data.BPCountry);
            if (data.BPPostal) locBits.push(data.BPPostal);
            var sub = [];
            if (locBits.length) sub.push(locBits.join(", "));
            if (data.BPValue) sub.push(data.BPValue);
            $h.find(".js-bp-sub").text(sub.join(" · "));
            $h.find(".js-doc-no").text(data.DocumentNo || "");
            // Context line shows the actual document type name (C_DocTypeTarget_ID).
            var typeText = data.DocTypeName
                || (data.IsAPCreditNote ? lbl("VAS_065_PurchaseCreditNote", "Purchase credit note") : lbl("VAS_065_PurchaseInvoice", "Purchase invoice"));
            $h.find(".js-doc-type").text(" · " + typeText);
            if (data.DateInvoiced) {
                $h.find(".js-doc-date").text(" · " + lbl("VAS_065_Invoiced", "Invoiced") + " " + fmtDate(data.DateInvoiced));
            }
            return $h;
        }

        /* Action bar */
        function buildActions() {
            var $a = $('<div class="vas-apinv-actions"></div>');

            var isCN = data.IsAPCreditNote;
            var openAmt = +data.OpenAmount || 0;
            var isCompleted = (data.DocStatus === "CO" || data.DocStatus === "CL");
            // Enabled whenever there is an open balance on a completed/closed document.
            var canPay = openAmt > 0 && isCompleted;

            // Show exactly one primary action based on document type: credit notes get
            // "Allocate credit note", everything else gets "Record payment". The button
            // is read-only (disabled) unless there is an open balance and the doc is CO/CL.
            $a.append($('<button type="button" class="vas-apinv-btn is-primary"></button>')
                .text(isCN ? lbl("VAS_065_AllocateCreditNote", "Allocate credit note")
                           : lbl("VAS_065_RecordPayment", "Record payment"))
                .prop("disabled", !canPay)
                .on("click", function () { if (canPay) openPaymentModal(); }));

            // Download PDF runs the tab's print process; it is read-only (disabled)
            // when the current tab has no AD_Process_ID linked (nothing to print).
            var hasPrintProcess = $self.curTab
                && typeof $self.curTab.getAD_Process_ID === "function"
                && +$self.curTab.getAD_Process_ID() > 0;
            $a.append($('<button type="button" class="vas-apinv-btn is-ghost"></button>')
                .text(lbl("VAS_065_DownloadPDF", "Download PDF"))
                .prop("disabled", !hasPrintProcess)
                .on("click", function () { if (hasPrintProcess) downloadInvoicePDF(); }));

            // View ledger entry only when posted facts exist.
            if (data.PostedJournal && data.PostedJournal.Rows && data.PostedJournal.Rows.length) {
                $a.append($('<button type="button" class="vas-apinv-btn is-text"></button>')
                    .text(lbl("VAS_065_ViewLedgerEntry", "View ledger entry"))
                    .on("click", function () { scrollToSection("vas-apinv-journal"); }));
            }
            return $a;
        }

        /* KPI cards: Vendor, Due date, Net payable / Paid */
        function buildKpis() {
            var $info = $('<section class="vas-apinv-info"></section>');

            // Vendor card (always).
            var $vendor = $(
                '<div class="vas-apinv-icard">' +
                '<div class="vas-apinv-eyebrow"></div>' +
                '<div class="vas-apinv-big link js-v"></div>' +
                '<div class="vas-apinv-meta js-m"></div>' +
                '</div>'
            );
            $vendor.find(".vas-apinv-eyebrow").text(lbl("Vendor"));
            $vendor.find(".js-v").text(data.BPName || "");
            $vendor.find(".js-m").text(data.BPTaxID || "");
            $info.append($vendor);

            // Due date card (only if due date exists).
            if (data.DueDate) {
                var $due = $(
                    '<div class="vas-apinv-icard">' +
                    '<div class="vas-apinv-eyebrow"></div>' +
                    '<div class="vas-apinv-big js-v"></div>' +
                    '<div class="vas-apinv-meta warn js-m"></div>' +
                    '</div>'
                );
                $due.find(".vas-apinv-eyebrow").text(lbl("DueDate"));
                $due.find(".js-v").text(fmtDate(data.DueDate));
                // Day delta = Due Date - Current Date (computed client-side).
                var dayDiff = dayDelta(data.DueDate);
                var overdue = dayDiff < 0;
                $due.find(".js-m").toggleClass("warn", overdue).text(overdue
                    ? lbl("VAS_065_DaysOverdue", "{0} days overdue").replace("{0}", Math.abs(dayDiff))
                    : lbl("VAS_065_DaysRemaining", "{0} days remaining").replace("{0}", Math.abs(dayDiff)));
                $info.append($due);
            }

            // Net payable / Paid card.
            var openAmt = +data.OpenAmount || 0;
            if (data.IsPaid || openAmt <= 0) {
                var $paid = $(
                    '<div class="vas-apinv-icard">' +
                    '<div class="vas-apinv-eyebrow"></div>' +
                    '<div class="vas-apinv-big js-v"></div>' +
                    '<div class="vas-apinv-meta js-m"></div>' +
                    '</div>'
                );
                $paid.find(".vas-apinv-eyebrow").text(lbl("VAS_065_Status", "Status"));
                $paid.find(".js-v").text(lbl("VAS_065_Paid", "Paid"));
                $paid.find(".js-m").text(fmtAmount(data.NetPayable));
                $info.append($paid);
            } else {
                var $np = $(
                    '<div class="vas-apinv-icard amber">' +
                    '<div class="vas-apinv-eyebrow"></div>' +
                    '<div class="vas-apinv-big js-v"></div>' +
                    '<div class="vas-apinv-meta js-m"></div>' +
                    '</div>'
                );
                $np.find(".vas-apinv-eyebrow").text(lbl("VAS_065_NetPayable", "Net payable"));
                $np.find(".js-v").text(fmtAmount(openAmt));
                if (+data.WithholdingAmount > 0) $np.find(".js-m").text(lbl("VAS_065_AfterWithholding", "After withholding"));
                else $np.find(".js-m").hide();
                $info.append($np);
            }
            return $info;
        }

        /* Lifecycle stepper — at least two states required */
        function buildLifecycle() {
            var steps = [];
            steps.push({ t: lbl("VAS_065_Drafted", "Drafted"), sub: subLine(data.CreatedByName, data.Created), done: true });

            var hasReceipt = data.GoodsReceipt && data.GoodsReceipt.Rows && data.GoodsReceipt.Rows.length;
            if (hasReceipt) {
                steps.push({ t: lbl("Matched"), sub: subLine(data.GoodsReceipt.ReceiptDocumentNo, data.GoodsReceipt.ReceivedDate), done: true });
            }
            if (data.IsApproved) {
                steps.push({ t: lbl("VAS_065_Approved", "Approved"), sub: subLine(data.ApprovedByName, data.Updated), done: true });
            }
            var posted = data.Posted === "Y";
            if (posted) {
                steps.push({ t: lbl("VAS_065_Posted", "Posted"), sub: subLine("", data.DateAcct), done: true });
            }
            // Payment due / Paid
            if (data.IsPaid) {
                steps.push({ t: lbl("VAS_065_Paid", "Paid"), sub: "", done: true });
            } else if (+data.OpenAmount > 0 && (data.DocStatus === "CO" || data.DocStatus === "CL")) {
                steps.push({ t: lbl("VAS_065_PaymentDue", "Payment due"), sub: data.DueDate ? lbl("DueDate") + " " + fmtDate(data.DueDate) : "", done: false, active: true });
            }

            if (steps.length < 2) return null;

            var $s = $('<section class="vas-apinv-stepper"></section>');
            var lastDone = -1;
            for (var k = 0; k < steps.length; k++) if (steps[k].done) lastDone = k;
            for (var i = 0; i < steps.length; i++) {
                var st = steps[i];
                var cls = st.done ? (i === lastDone && !hasActiveAfter(steps, i) ? "step done" : "step done") : "step future";
                if (st.active) cls = "step active";
                var $step = $('<div class="vas-apinv-step ' + (st.active ? "active" : (st.done ? "done" : "future")) + '"></div>');
                $step.append($('<div class="vas-apinv-step-t"></div>').text(st.t));
                $step.append($('<div class="vas-apinv-step-s"></div>').text(st.sub || " "));
                $s.append($step);
            }
            return $s;
        }
        function hasActiveAfter(steps, idx) { for (var i = idx + 1; i < steps.length; i++) if (steps[i].active) return true; return false; }
        function subLine(name, date) {
            var bits = [];
            if (name) bits.push(name);
            if (date) bits.push(fmtDate(date));
            return bits.join(" · ");
        }

        /* Invoice details (key/value pairs — only non-empty rows) */
        function buildInvoiceDetails() {
            var rows = [
                { k: lbl("VAS_065_VendorReferenceNo", "Vendor Reference No."), v: data.InvoiceReference },
                { k: lbl("InvoiceNo"), v: data.DocumentNo },
                { k: lbl("VAS_065_PurchaseOrder", "Purchase Order"), v: data.OrderDocumentNo },
                { k: lbl("VAS_065_DocumentStatus", "Document Status"), v: data.DocStatusName || docStatusLabel(data.DocStatus) },
                { k: lbl("VAS_065_PostedStatus", "Posted Status"), v: data.PostedName || (data.Posted ? data.Posted : "") },
                { k: lbl("VAS_065_PaymentTerms", "Payment Terms"), v: data.PaymentTermName },
                { k: lbl("VAS_065_PaymentMethod", "Payment Method"), v: data.PaymentMethodName },
                { k: lbl("VAS_065_InvoiceCurrency", "Invoice currency"), v: data.CurISO },
                { k: lbl("VAS_065_AccountingCurrency", "Accounting currency"), v: data.AcctCurISO },
                { k: lbl("VAS_065_Representative", "Representative"), v: data.RepresentativeName }
            ];
            var visible = rows.filter(function (r) { return r.v != null && String(r.v).length > 0; });
            if (!visible.length) return null;

            var $sec = $(
                '<section class="vas-apinv-block">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '</div>' +
                '<div class="vas-apinv-kv cols2 js-kv"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_InvoiceDetails", "Invoice details"));

            var $kv = $sec.find(".js-kv");
            visible.forEach(function (r) {
                $kv.append($('<div class="vas-apinv-kv-r"></div>')
                    .append($('<span class="vas-apinv-kv-k"></span>').text(r.k))
                    .append($('<span class="vas-apinv-kv-v"></span>').text(r.v)));
            });
            return $sec;
        }

        /* Landed cost banner — only when allocation exists */
        function buildLandedCostBanner() {
            var lc = data.LandedCost;
            if (!lc || !lc.Rows || !lc.Rows.length || +lc.TotalLandedCost <= 0) return null;

            var $b = $(
                '<section class="vas-apinv-banner">' +
                '<div class="vas-apinv-banner-l">' +
                '<span class="vas-apinv-well"><i class="fa fa-truck" aria-hidden="true"></i></span>' +
                '<div class="vas-apinv-banner-tx">' +
                '<div class="vas-apinv-banner-a"></div>' +
                '<div class="vas-apinv-banner-b"></div>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="vas-apinv-btn is-primary js-review"></button>' +
                '</section>'
            );
            $b.find(".vas-apinv-banner-a").text(
                lbl("VAS_065_LandedCostBannerTitle", "{0} in landed costs distributed across {1} items")
                    .replace("{0}", fmtAmount(lc.TotalLandedCost))
                    .replace("{1}", lc.ItemCount));
            if (lc.SourceDocumentNo) {
                $b.find(".vas-apinv-banner-b").text(
                    lbl("VAS_065_LandedCostBannerSub", "From {0} - capitalised into inventory cost").replace("{0}", lc.SourceDocumentNo));
            } else { $b.find(".vas-apinv-banner-b").hide(); }
            $b.find(".js-review").text(lbl("VAS_065_ReviewDistribution", "Review distribution"))
                .on("click", function () { scrollToSection("vas-apinv-landed"); });
            return $b;
        }

        /* Line items (paged when > 5) */
        function buildLineItems() {
            var lines = (data.Lines || []);
            if (!lines.length) return null;

            var anyTax = lines.some(function (l) { return +l.TaxRate > 0; });
            var goodsCount = lines.filter(function (l) { return l.IsProductLine; }).length;

            var $sec = $(
                '<section class="vas-apinv-block">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-block-r js-count"></span>' +
                '</div>' +
                '<div class="vas-apinv-li-head">' +
                '<span class="js-c-desc"></span>' +
                '<span class="right js-c-qty"></span>' +
                '<span class="js-c-uom"></span>' +
                '<span class="right js-c-rate"></span>' +
                '<span class="right js-c-tax"></span>' +
                '<span class="right js-c-amt"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="vas-apinv-block-foot js-foot" style="display:none;"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_LineItems", "Line items"));
            $sec.find(".js-count").text(lbl("VAS_065_GoodsLinesCount", "{0} goods lines").replace("{0}", goodsCount));
            $sec.find(".js-c-desc").text(lbl("Description"));
            $sec.find(".js-c-qty").text(lbl("Quantity"));
            $sec.find(".js-c-uom").text(lbl("VAS_065_UOM", "UOM"));
            $sec.find(".js-c-rate").text(lbl("VAS_065_Rate", "Rate"));
            $sec.find(".js-c-tax").text(lbl("VAS_065_Tax", "Tax"));
            $sec.find(".js-c-amt").text(lbl("Amount"));
            if (!anyTax) $sec.addClass("no-tax");

            renderLineRows($sec, lines);
            return $sec;
        }

        function renderLineRows($sec, lines) {
            var $rows = $sec.find(".js-rows").empty();
            var total = lines.length;
            var paged = total > LINES_PER_PAGE;
            var start = paged ? linePage * LINES_PER_PAGE : 0;
            var end = paged ? Math.min(start + LINES_PER_PAGE, total) : total;

            for (var i = start; i < end; i++) {
                var ln = lines[i];
                var name = ln.ProductName || ln.ChargeName || ln.Description || "";
                var $r = $('<div class="vas-apinv-lirow"></div>');
                var $desc = $('<div class="vas-apinv-desc"></div>');
                $desc.append($('<div class="vas-apinv-d"></div>').text(name));
                // Attribute-set-instance detail (e.g. lot/serial/attributes) below the item name.
                // Skip when absent or only a placeholder ("--", separators, whitespace).
                var asiText = (ln.ASIDescription || "").trim();
                if (asiText && asiText.replace(/[-\s—–_]/g, "") !== "") {
                    $desc.append($('<div class="vas-apinv-ds asi"></div>').text(asiText));
                }
                if (ln.Description && (ln.ProductName || ln.ChargeName)) {
                    $desc.append($('<div class="vas-apinv-ds"></div>').text(ln.Description));
                }
                $r.append($desc);
                $r.append($('<div class="vas-apinv-qty"></div>').text(fmtNumber(ln.QtyEntered, 0)));
                $r.append($('<div class="vas-apinv-uom"></div>').text(ln.UOMSymbol || ""));
                $r.append($('<div class="vas-apinv-rate"></div>').text(fmtAmount(ln.PriceEntered)));
                $r.append($('<div class="vas-apinv-tax"></div>').text(+ln.TaxRate > 0 ? fmtPct(ln.TaxRate) : ""));
                $r.append($('<div class="vas-apinv-amt"></div>').text(fmtAmount(ln.LineNetAmt)));
                $rows.append($r);
            }

            var $foot = $sec.find(".js-foot");
            if (paged) {
                $foot.empty().show();
                var pageCount = Math.ceil(total / LINES_PER_PAGE);
                $foot.append($('<span></span>').text(
                    lbl("VAS_065_Showing", "Showing {0} of {1}")
                        .replace("{0}", (start + 1) + "–" + end)
                        .replace("{1}", total)));
                var $nav = $('<span class="vas-apinv-pager"></span>');
                var $prev = $('<button type="button" class="vas-apinv-pagebtn"></button>').text(lbl("VAS_065_Previous", "Previous"));
                var $next = $('<button type="button" class="vas-apinv-pagebtn"></button>').text(lbl("VAS_065_Next", "Next"));
                $prev.prop("disabled", linePage <= 0).on("click", function () { linePage--; renderLineRows($sec, lines); });
                $next.prop("disabled", linePage >= pageCount - 1).on("click", function () { linePage++; renderLineRows($sec, lines); });
                $nav.append($prev).append($next);
                $foot.append($nav);
            } else {
                $foot.hide();
            }
        }

        /* Totals */
        function buildTotals() {
            var hasTax = +data.TaxAmt > 0;
            var hasWh = +data.WithholdingAmount > 0;
            var $wrap = $('<div class="vas-apinv-totals-wrap"><div class="vas-apinv-totals"></div></div>');
            var $t = $wrap.find(".vas-apinv-totals");

            $t.append(totalRow(lbl("VAS_065_Subtotal", "Subtotal"), fmtAmount(data.TotalLines), ""));
            if (hasTax) {
                $t.append(totalRow(lbl("VAS_065_Tax", "Tax"), fmtAmount(data.TaxAmt), ""));
            }
            $t.append(totalRow(lbl("VAS_065_GrossInvoiceTotal", "Gross invoice total"), fmtAmount(data.GrandTotal), ""));
            if (hasWh) {
                $t.append(totalRow(lbl("VAS_065_LessWithholding", "Less withholding"), "(" + fmtAmount(data.WithholdingAmount) + ")", ""));
            }
            $t.append(totalRow(lbl("VAS_065_NetPayable", "Net payable"), fmtAmount(data.NetPayable), "grand"));
            return $wrap;
        }
        function totalRow(k, v, cls) {
            return $('<div class="vas-apinv-trow ' + (cls || "") + '"></div>')
                .append($('<span class="vas-apinv-trow-k"></span>').text(k))
                .append($('<span class="vas-apinv-trow-v"></span>').text(v));
        }

        /* Withholding — only when withholding applies */
        function buildWithholding() {
            if (!(+data.WithholdingAmount > 0) || !data.Withholding) return null;
            var w = data.Withholding;
            var $sec = $(
                '<section class="vas-apinv-withholding">' +
                '<div class="vas-apinv-wh-head">' +
                '<span class="vas-apinv-wh-h"></span>' +
                '<span class="vas-apinv-wh-r"></span>' +
                '</div>' +
                '<div class="vas-apinv-wh-grid">' +
                '<div class="cell"><div class="k js-k1"></div><div class="v js-v1"></div></div>' +
                '<div class="cell"><div class="k js-k2"></div><div class="v js-v2"></div></div>' +
                '<div class="cell"><div class="k js-k3"></div><div class="v js-v3"></div></div>' +
                '<div class="cell"><div class="k js-k4"></div><div class="v warn js-v4"></div></div>' +
                '</div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-wh-h").text(lbl("VAS_065_WithholdingDetails", "Withholding details"));
            $sec.find(".vas-apinv-wh-r").text(lbl("VAS_065_AppliedBeforeVendorPayment", "Applied before vendor payment"));
            $sec.find(".js-k1").text(lbl("VAS_065_WithholdingType", "Withholding type"));
            $sec.find(".js-v1").text(w.TypeName || lbl("VAS_065_Withholding", "Withholding"));
            $sec.find(".js-k2").text(lbl("VAS_065_BaseAmount", "Base amount"));
            $sec.find(".js-v2").text(fmtAmount(w.Base));
            $sec.find(".js-k3").text(lbl("VAS_065_Rate", "Rate"));
            $sec.find(".js-v3").text(fmtPct(w.Rate));
            $sec.find(".js-k4").text(lbl("VAS_065_WithheldAmount", "Withheld amount"));
            $sec.find(".js-v4").text(fmtAmount(w.Amount));
            return $sec;
        }

        /* Three-way match insight — only for product invoices with receipt linkage */
        function buildMatchInsight() {
            var gr = data.GoodsReceipt;
            if (!gr || !gr.Rows || !gr.Rows.length) return null;

            var clean = gr.IsCleanMatch;
            var $sec = $(
                '<section class="vas-apinv-insight ' + (clean ? "" : "warn") + '">' +
                '<div class="vas-apinv-insight-l">' +
                '<span class="vas-apinv-well"><i class="fa ' + (clean ? "fa-check-circle" : "fa-exclamation-circle") + '" aria-hidden="true"></i></span>' +
                '<div class="vas-apinv-insight-tx">' +
                '<div class="vas-apinv-insight-a"></div>' +
                '<div class="vas-apinv-insight-b"></div>' +
                '</div>' +
                '</div>' +
                '<span class="vas-apinv-risk js-risk"></span>' +
                '</section>'
            );
            $sec.find(".vas-apinv-insight-a").text(clean
                ? lbl("VAS_065_ThreeWayMatchPassed", "3-way match passed - no price or quantity variance")
                : lbl("VAS_065_VarianceFound", "Variance found"));
            $sec.find(".vas-apinv-insight-b").text(
                lbl("VAS_065_MatchedToReceiptOrder", "Matched to receipt {0} and order {1}")
                    .replace("{0}", gr.ReceiptDocumentNo || "")
                    .replace("{1}", gr.OrderDocumentNo || ""));
            $sec.find(".js-risk").text(clean ? lbl("VAS_065_CleanMatch", "Clean match") : lbl("VAS_065_VarianceFound", "Variance found"));
            return $sec;
        }

        /* Landed cost distribution */
        function buildLandedCostDistribution() {
            var lc = data.LandedCost;
            if (!lc || !lc.Rows || !lc.Rows.length) return null;

            var $sec = $(
                '<section class="vas-apinv-block vas-apinv-landed">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-block-r js-basis"></span>' +
                '</div>' +
                '<div class="vas-apinv-lc-head">' +
                '<span class="js-c1"></span><span class="js-c2"></span>' +
                '<span class="right js-c3"></span><span class="right js-c4"></span><span class="right js-c5"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="vas-apinv-lc-total"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_LandedCostDistribution", "Landed cost distribution"));
            // Landed-cost amounts are converted to the base (accounting) currency;
            // its ISO code replaces the static "Basis" label so the figures are unambiguous.
            $sec.find(".js-basis").text(lbl("VAS_065_LandedCostBasis", "{2}: line value - {0} across {1} items")
                .replace("{0}", fmtAmount(lc.TotalLandedCost))
                .replace("{1}", lc.ItemCount)
                .replace("{2}", data.AcctCurISO || ""));
            $sec.find(".js-c1").text(lbl("VAS_065_Item", "Item"));
            $sec.find(".js-c2").text(lbl("VAS_065_CostInvoice", "Cost invoice"));
            $sec.find(".js-c3").text(lbl("VAS_065_PurchaseValue", "Purchase value"));
            $sec.find(".js-c4").text(lbl("VAS_065_LandedCost", "Landed cost"));
            $sec.find(".js-c5").text(lbl("VAS_065_InventoryCost", "Inventory cost"));

            var $rows = $sec.find(".js-rows");
            lc.Rows.forEach(function (row) {
                var $r = $('<div class="vas-apinv-lc-row"></div>');
                var $it = $('<div class="it"></div>');
                $it.append($('<div class="nm"></div>').text(row.ItemName || ""));
                $it.append($('<div class="su"></div>').text(fmtNumber(row.QtyEntered, 0) + (row.UOMSymbol ? " " + row.UOMSymbol : "")));
                $r.append($it);
                $r.append($('<div class="ref"></div>').text(row.SourceDocumentNo || ""));
                $r.append($('<div class="num pv"></div>').text(fmtAmount(row.PurchaseValue)));
                $r.append($('<div class="num lc"></div>').text("+ " + fmtAmount(row.AllocatedLandedCost)));
                var $iv = $('<div class="num iv"></div>').text(fmtAmount(row.InventoryCost));
                if (row.UnitInventoryCost) {
                    $iv.append($('<span class="u"></span>').text(
                        fmtAmount(row.UnitInventoryCost) + " " + lbl("VAS_065_PerUnit", "/") + " " + row.UOMSymbol));
                }
                $r.append($iv);
                $rows.append($r);
            });

            var $tot = $sec.find(".vas-apinv-lc-total");
            $tot.append($('<div class="tl"></div>').text(lbl("VAS_065_Total", "Total")));
            $tot.append($('<div class="num pv"></div>').text(fmtAmount(lc.TotalPurchaseValue)));
            $tot.append($('<div class="num lc"></div>').text("+ " + fmtAmount(lc.TotalLandedCost)));
            $tot.append($('<div class="num iv"></div>').text(fmtAmount(lc.TotalInventoryCost)));
            return $sec;
        }

        /* Goods receipt + payment schedule (two columns) */
        function buildGoodsReceiptAndSchedule() {
            var $gr = buildGoodsReceipt();
            var $ps = buildPaymentSchedule();
            if (!$gr && !$ps) return null;

            var $two = $('<div class="vas-apinv-two-col"></div>');
            if ($gr) $two.append($gr);
            if ($ps) $two.append($ps);
            // Only one block present (e.g. no goods-receipt detail) -> span full width.
            if (!($gr && $ps)) $two.addClass("single");
            return $two;
        }

        function buildGoodsReceipt() {
            var gr = data.GoodsReceipt;
            if (!gr || !gr.Rows || !gr.Rows.length) return null;

            var $sec = $(
                '<section class="vas-apinv-block">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-spill ok"><span class="vas-apinv-dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="vas-apinv-kv js-kv"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_GoodsReceiptMatching", "Goods receipt & matching"));
            $sec.find(".js-st").text(lbl("Matched"));

            var $kv = $sec.find(".js-kv");
            kvRow($kv, lbl("VAS_065_ReceiptGRN", "Receipt (GRN)"), gr.ReceiptDocumentNo);
            kvRow($kv, lbl("VAS_065_PurchaseOrder", "Purchase order"), gr.OrderDocumentNo);
            kvRow($kv, lbl("VAS_065_ReceivedOn", "Received on"), fmtDate(gr.ReceivedDate));
            kvRow($kv, lbl("QtyReceived"), fmtNumber(gr.TotalReceived, 0) + " " +
                lbl("VAS_065_UnitsCount", "units").replace("{0}", "").trim());
            kvRow($kv, lbl("VAS_065_Warehouse", "Warehouse"), gr.WarehouseName);
            kvRow($kv, lbl("VAS_065_PriceVariance", "Price variance"), fmtAmount(gr.PriceVariance));
            return $sec;
        }
        function kvRow($kv, k, v) {
            if (v == null || String(v).length === 0) return;
            $kv.append($('<div class="vas-apinv-kv-r"></div>')
                .append($('<span class="vas-apinv-kv-k"></span>').text(k))
                .append($('<span class="vas-apinv-kv-v"></span>').text(v)));
        }

        function buildPaymentSchedule() {
            var sched = data.PaymentSchedule || [];
            if (!sched.length) return null;

            var $sec = $(
                '<section class="vas-apinv-block">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-block-r js-sum"></span>' +
                '</div>' +
                '<div class="vas-apinv-t4-head">' +
                '<span class="js-h1"></span><span class="js-h2"></span><span class="js-h3"></span><span class="right js-h4"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="vas-apinv-block-foot js-foot"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_PaymentSchedule", "Payment schedule"));
            $sec.find(".js-sum").text((data.PaymentTermName ? data.PaymentTermName + " · " : "") +
                lbl("VAS_065_InstalmentCount", "{0} instalment(s)").replace("{0}", sched.length));
            $sec.find(".js-h1").text(lbl("VAS_065_Schedule", "Schedule"));
            $sec.find(".js-h2").text(lbl("DueDate"));
            $sec.find(".js-h3").text(lbl("VAS_065_Status", "Status"));
            $sec.find(".js-h4").text(lbl("Amount"));

            var $rows = $sec.find(".js-rows");
            var totalOpen = 0, anyHold = false;
            sched.forEach(function (s, idx) {
                if (!s.IsPaid) totalOpen += +s.DueAmt || 0;
                if (s.IsHold) anyHold = true;
                var pct = Math.round(100 / sched.length);
                var $r = $('<div class="vas-apinv-t4-row"></div>');
                $r.append($('<div class="col-a p"></div>').text(
                    lbl("VAS_065_ScheduleLine", "Schedule {0} - {1}%").replace("{0}", (idx + 1)).replace("{1}", pct)));
                $r.append($('<div class="col-b c"></div>').text(fmtDate(s.DueDate)));
                var statusKey = s.Status === "Paid" ? "VAS_065_Paid" : (s.Status === "OnHold" ? "VAS_065_OnHold" : "Open");
                var statusCls = s.Status === "Paid" ? "ok" : (s.Status === "OnHold" ? "warn" : "warn");
                $r.append($('<div class="col-c"></div>').append(
                    $('<span class="vas-apinv-spill ' + statusCls + '"></span>').text(lbl(statusKey))));
                $r.append($('<div class="col-d amt"></div>').text(fmtAmount(s.DueAmt)));
                $rows.append($r);
            });

            var $foot = $sec.find(".js-foot");
            $foot.append($('<span></span>').text(
                lbl("VAS_065_PaymentHold", "Payment hold: {0}").replace("{0}", anyHold ? lbl("Y") : lbl("N")) +
                " · " + lbl("VAS_065_AwaitingPayment", "awaiting payment")));
            $foot.append($('<span></span>').html(
                lbl("Open") + " <b>" + escapeHtml(fmtAmount(totalOpen)) + "</b>"));
            return $sec;
        }

        /* Approval — only if approval/workflow info available */
        function buildApproval() {
            // Build from available status milestones; require at least one meaningful step.
            var steps = [];
            steps.push({ st: lbl("VAS_065_Drafted", "Drafted"), mt: subLine(data.CreatedByName, data.Created), done: true });
            if (data.GoodsReceipt && data.GoodsReceipt.Rows && data.GoodsReceipt.Rows.length) {
                steps.push({
                    st: lbl("VAS_065_MatchedToReceipt", "Matched to receipt"), mt: fmtDate(data.GoodsReceipt.ReceivedDate),
                    nt: lbl("VAS_065_MatchedToReceiptOrder", "Matched to receipt {0} and order {1}")
                        .replace("{0}", data.GoodsReceipt.ReceiptDocumentNo || "")
                        .replace("{1}", data.GoodsReceipt.OrderDocumentNo || ""), done: true
                });
            }
            if (data.IsApproved) {
                steps.push({ st: lbl("VAS_065_Approved", "Approved"), mt: subLine(data.ApprovedByName, data.Updated), done: true });
            }
            if (data.Posted === "Y") {
                steps.push({ st: lbl("VAS_065_PostedToLedger", "Posted to ledger"), mt: fmtDate(data.DateAcct), done: true });
            }
            if (steps.length < 2) return null;

            var $sec = $(
                '<section class="vas-apinv-block">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-spill ok"><span class="vas-apinv-dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="vas-apinv-vsteps js-steps"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_Approval", "Approval"));
            $sec.find(".js-st").text(data.IsApproved ? lbl("VAS_065_Approved", "Approved") : (data.DocStatusName || docStatusLabel(data.DocStatus)));

            var $steps = $sec.find(".js-steps");
            steps.forEach(function (s) {
                var $v = $('<div class="vas-apinv-vstep done"></div>');
                $v.append('<div class="rail"><div class="node"><i class="fa fa-check" aria-hidden="true"></i></div><div class="line"></div></div>');
                var $c = $('<div class="c"></div>');
                $c.append($('<div class="st"></div>').text(s.st));
                if (s.mt) $c.append($('<div class="mt"></div>').text(s.mt));
                if (s.nt) $c.append($('<div class="nt"></div>').text(s.nt));
                $v.append($c);
                $steps.append($v);
            });
            return $sec;
        }

        /* Posted journal entry — only when posted facts exist */
        function buildPostedJournal() {
            var pj = data.PostedJournal;
            if (!pj || !pj.Rows || !pj.Rows.length) return null;

            // Posted amounts are in the base/accounting currency (primary acct schema),
            // so show that currency's symbol — not the invoice transaction currency.
            var acctCur = pj.CurSymbol || "";
            var acctPrec = (pj.StdPrecision >= 0) ? pj.StdPrecision : 2;

            // Columns in the requested order. Account/Debit/Credit always show;
            // each dimension column is dropped when NO row carries a value for it.
            var cols = [
                {
                    key: "OrgName", label: lbl("AD_Org_ID", "Organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgName || ""; }
                },
                {
                    key: "Account", label: lbl("VAS_065_Account", "Account"), w: "2fr", num: false, always: true,
                    get: function (jr) { return (jr.AccountValue ? jr.AccountValue + " · " : "") + (jr.AccountName || ""); }
                },
                {
                    key: "Dr", label: lbl("VAS_065_Debit", "Debit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctDr; }
                },
                {
                    key: "Cr", label: lbl("VAS_065_Credit", "Credit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctCr; }
                },
                {
                    key: "BPName", label: lbl("C_BPartner_ID", "Business Partner"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.BPName || ""; }
                },
                {
                    key: "ProductName", label: lbl("M_Product_ID", "Product"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.ProductName || ""; }
                },
                {
                    key: "OrgTrxName", label: lbl("AD_OrgTrx_ID", "Trx Organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgTrxName || ""; }
                }
            ].filter(function (c) {
                if (c.always) return true;
                return pj.Rows.some(function (jr) { var v = jr[c.key]; return v != null && String(v).length > 0; });
            });
            var tmpl = cols.map(function (c) { return c.w; }).join(" ");

            var $sec = $(
                '<section class="vas-apinv-block vas-apinv-journal">' +
                '<div class="vas-apinv-block-head">' +
                '<span class="vas-apinv-block-h"></span>' +
                '<span class="vas-apinv-spill ok"><span class="vas-apinv-dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="vas-apinv-je-head"></div>' +
                '<div class="js-rows"></div>' +
                '<div class="vas-apinv-je-total"></div>' +
                '<div class="vas-apinv-block-foot js-foot"></div>' +
                '</section>'
            );
            $sec.find(".vas-apinv-block-h").text(lbl("VAS_065_PostedJournalEntry", "Posted journal entry"));
            $sec.find(".js-st").text(lbl("VAS_065_Posted", "Posted"));

            var $head = $sec.find(".vas-apinv-je-head").css("grid-template-columns", tmpl);
            cols.forEach(function (c) {
                $head.append($('<span></span>').addClass(c.num ? "right" : "").text(c.label));
            });

            var $rows = $sec.find(".js-rows");
            pj.Rows.forEach(function (jr) {
                var $r = $('<div class="vas-apinv-je-row"></div>').css("grid-template-columns", tmpl);
                cols.forEach(function (c) {
                    if (c.num) {
                        $r.append(jeAmt(c.get(jr), c.key === "Dr" ? "dr" : "cr", acctCur, acctPrec));
                    } else if (c.key === "Account") {
                        $r.append($('<div class="acct"></div>').append($('<div class="code"></div>').text(c.get(jr))));
                    } else {
                        $r.append($('<div class="dimc"></div>').text(c.get(jr)));
                    }
                });
                $rows.append($r);
            });

            var $tot = $sec.find(".vas-apinv-je-total").css("grid-template-columns", tmpl);
            cols.forEach(function (c, idx) {
                if (c.key === "Dr") $tot.append($('<div class="num dr"></div>').text(fmtAmountCur(pj.TotalDr, acctCur, acctPrec)));
                else if (c.key === "Cr") $tot.append($('<div class="num cr"></div>').text(fmtAmountCur(pj.TotalCr, acctCur, acctPrec)));
                else if (idx === 0) $tot.append($('<div class="tlab"></div>').text(lbl("VAS_065_Total", "Total")));
                else $tot.append($('<div></div>'));
            });

            var footBits = [];
            if (pj.PeriodName) footBits.push(lbl("VAS_065_Period", "Period {0}").replace("{0}", pj.PeriodName));
            if (pj.PostingDate) footBits.push(lbl("VAS_065_PostingDate", "Posting date {0}").replace("{0}", fmtDate(pj.PostingDate)));
            var balanced = Math.abs((+pj.TotalDr || 0) - (+pj.TotalCr || 0)) < 0.005;
            $sec.find(".js-foot")
                .append($('<span></span>').text(footBits.join(" · ")))
                .append($('<span></span>').text(balanced ? lbl("VAS_065_Balanced", "Balanced - Dr = Cr") : ""));
            return $sec;
        }
        function jeAmt(value, cls, cur, prec) {
            var v = +value || 0;
            var $d = $('<div class="num ' + cls + '"></div>');
            if (v === 0) { $d.addClass("zero").text("—"); }
            else { $d.text((v < 0 ? "-" : "") + fmtAmountCur(v, cur, prec)); }
            return $d;
        }

        /* ===================== Payment / allocation modal ===================== */

        function openPaymentModal() {
            if (!data || !data.C_Invoice_ID) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_065_APInvoicePanel/GetPaymentModalMeta",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID },
                success: function (raw) {
                    showBusy(false);
                    meta = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!meta || !meta.C_BPartner_ID) { info(lbl("VAS_065_LoadFailed", "Could not load the requested information.")); return; }
                    renderModal();
                },
                error: function (err) { showBusy(false); console.log(err); info(lbl("VAS_065_LoadFailed", "Could not load the requested information.")); }
            });
        }

        function renderModal() {
            removeModal();
            var isCN = meta.RecordMode === "AP_CREDIT_NOTE_ALLOCATION";
            var p = meta.StdPrecision >= 0 ? meta.StdPrecision : 2;
            var cur = meta.CurSymbol || meta.ISO_Code || "";

            $scrim = $('<div class="vas-apinv-scrim ' + (isCN ? "cn-mode" : "invoice-mode") +
                '" role="dialog" aria-modal="true"></div>');
            var $modal = $('<div class="vas-apinv-modal"></div>');
            $scrim.append($modal);

            // Head
            var $head = $(
                '<div class="vas-apinv-m-head">' +
                '<div><h2 class="js-title"></h2><div class="ms js-sub"></div></div>' +
                '<button type="button" class="vas-apinv-m-x" aria-label="Close"><i class="fa fa-times"></i></button>' +
                '</div>'
            );
            $head.find(".js-title").text(isCN ? lbl("VAS_065_AllocateCreditNote", "Allocate credit note") : lbl("VAS_065_RecordPayment", "Record payment"));
            $head.find(".js-sub").text((data.BPName || "") + " · " + (data.DocumentNo || ""));
            $head.find(".vas-apinv-m-x").on("click", closeModal);
            $modal.append($head);

            var $form = $('<div class="js-form"></div>');
            var $bodyM = $('<div class="vas-apinv-m-body"></div>');

            // Stats
            var $stats = $('<div class="vas-apinv-m-stats"></div>');
            statCard($stats, lbl("VAS_065_VendorOutstanding", "Vendor outstanding"), fmtAmountCur(meta.VendorOutstanding, cur, p), "");
            statCard($stats, lbl("VAS_065_GrossInvoice", "Gross invoice"), fmtAmountCur(meta.GrossInvoice, cur, p), "amber");
            if (+meta.Withholding > 0) statCard($stats, lbl("VAS_065_Withholding", "Withholding"), fmtAmountCur(meta.Withholding, cur, p), "amber");
            statCard($stats, lbl("VAS_065_NetPayable", "Net payable"), fmtAmountCur(meta.NetPayable, cur, p), "green");
            if (isCN) {
                statCard($stats, lbl("VAS_065_CreditNoteAmount", "Credit note amount"), fmtAmountCur(meta.CreditNoteAmount, cur, p), "amber");
                statCard($stats, lbl("VAS_065_OpenInvoicesAvailable", "Open invoices available"), fmtAmountCur(meta.OpenInvoicesAvailable, cur, p), "green");
            } else {
                statCard($stats, lbl("VAS_065_AvailableToApply", "Available to apply"), fmtAmountCur(meta.AvailableToApply, cur, p), "green");
            }
            $bodyM.append($stats);

            if (isCN) buildCreditNoteSection($bodyM, p, cur);
            else buildInvoicePaymentSections($bodyM, p, cur);

            $form.append($bodyM);
            $form.append(buildModalFooter(isCN));
            $modal.append($form);

            // Success view (hidden initially)
            $modal.append(buildSuccessView());

            $("body").append($scrim);
            // Force reflow then open for the transition.
            void $scrim[0].offsetWidth;
            $scrim.addClass("open");

            $scrim.on("click", function (e) { if (e.target === $scrim[0]) closeModal(); });
            $(document).on("keydown.vasApinvModal", function (e) {
                if (e.key === "Escape") closeModal();
            });
        }

        function statCard($wrap, label, value, tone) {
            $wrap.append($('<div class="vas-apinv-m-stat ' + (tone || "") + '"></div>')
                .append($('<div class="l"></div>').text(label))
                .append($('<div class="v"></div>').text(value)));
        }

        /* AP invoice mode: apply credits + new payment + settlement */
        function buildInvoicePaymentSections($bodyM, p, cur) {
            var onAccount = meta.OnAccountPayments || [];
            var credits = meta.CreditNotes || [];
            var allRows = [];
            onAccount.forEach(function (r) { allRows.push(r); });
            credits.forEach(function (r) { allRows.push(r); });
            var invOpen = +meta.NetOpenAmount || +meta.NetPayable || 0;

            var state = { applied: 0, allocationCreated: false, selected: {} };

            // Apply on-account & credits
            if (allRows.length) {
                var $sec = $('<div class="vas-apinv-m-sec"></div>');
                $sec.append($('<div class="vas-apinv-m-sec-h"></div>')
                    .append($('<span class="t"></span>').text(lbl("VAS_065_ApplyOnAccountCredits", "Apply on-account & credits")))
                    .append($('<span class="hint"></span>').text(lbl("VAS_065_UseTheseFirst", "Use these first - no new cash"))));

                allRows.forEach(function (r, idx) {
                    var $row = $('<div class="vas-apinv-credit-row"></div>');
                    $row.append('<span class="chk"><i class="fa fa-check"></i></span>');
                    var label = (r.SourceType === "PAYMENT"
                        ? lbl("VAS_065_OnAccountPayment", "On-account payment - {0}")
                        : lbl("VAS_065_VendorCreditNote", "Vendor credit note - {0}")).replace("{0}", r.DocumentNo || "");
                    var $cl = $('<div class="cl"></div>');
                    $cl.append($('<div class="a"></div>').text(label));
                    $cl.append($('<div class="b"></div>').text(fmtDate(r.Date)));
                    $row.append($cl);
                    var $amt = $('<div class="amt"></div>');
                    $amt.append($('<div class="av"></div>').text(fmtAmountCur(r.AvailableAmount, cur, p)));
                    $amt.append($('<div class="ap"></div>').text(lbl("Selected")));
                    $row.append($amt);
                    $row.data("src", r);
                    $row.on("click", function () {
                        if (state.allocationCreated || $row.hasClass("locked")) return;
                        $row.toggleClass("on");
                        updateCreditSummary();
                        recompute();
                    });
                    $sec.append($row);
                });

                var $actions = $('<div class="vas-apinv-credit-actions"></div>');
                var $summary = $('<span class="summary js-credit-summary"></span>').text(lbl("VAS_065_SelectCreditsHint", "Select available payments or credits to allocate before recording a new payment."));
                var $applyBtn = $('<button type="button" class="vas-apinv-btn is-outline js-apply-credits"></button>')
                    .text(lbl("VAS_065_ApplySelectedCredits", "Apply selected credits")).prop("disabled", true);
                $actions.append($summary).append($applyBtn);
                $sec.append($actions);
                $sec.append($('<div class="vas-apinv-alloc-status js-alloc-status"></div>').text(lbl("VAS_065_AllocationStatusMsg", "Allocation transaction created. Remaining balance is now available for new payment.")));

                $applyBtn.on("click", function () { applySelectedCredits(); });
                $bodyM.append($sec);
            }

            // New payment form
            var $pay = $('<div class="vas-apinv-m-sec js-newpay"></div>');
            $pay.append($('<div class="vas-apinv-m-sec-h"></div>')
                .append($('<span class="t"></span>').text(lbl("VAS_065_NewPayment", "New payment")))
                .append($('<span class="hint"></span>').text(lbl("VAS_065_ForBalanceAfterCredits", "For the balance after credits"))));

            var $grid = $('<div class="vas-apinv-fgrid"></div>');
            var $amtField = field(lbl("VAS_065_PaymentAmount", "Payment amount"), '<div class="control"><span class="pfx">' + escapeHtml(cur) +
                '</span><input class="js-pay-amt" type="text" inputmode="decimal" /></div>');
            var $dateField = field(lbl("VAS_065_PaymentDate", "Payment date"), '<div class="control"><input class="js-pay-date" type="date" /></div>');
            var $methodSel = $('<select class="js-pay-method"></select>');
            (meta.PaymentMethods || []).forEach(function (m) {
                $methodSel.append($('<option></option>').val(m.VA009_PaymentMethod_ID).text(m.Name));
            });
            var $methodField = field(lbl("PaymentMethod"), $('<div class="control sel"></div>').append($methodSel));
            var $bankSel = $('<select class="js-pay-bank"></select>');
            (meta.BankAccounts || []).forEach(function (b) {
                var t = (b.BankName || "") + (b.AccountNo ? " · ****" + b.AccountNo.slice(-4) : "") + (b.CurrencyISO ? " . " + b.CurrencyISO : "");
                $bankSel.append($('<option></option>').val(b.C_BankAccount_ID).text(t));
            });
            var $bankField = field(lbl("VAS_065_BankAccount", "Bank account"), $('<div class="control sel"></div>').append($bankSel));
            var $discField = field(lbl("VAS_065_CashDiscount", "Cash discount"), '<div class="control"><span class="pfx">' + escapeHtml(cur) +
                '</span><input class="js-pay-disc" type="text" inputmode="decimal" value="0.00" /></div>');
            var $refField = field(lbl("VAS_Reference"), '<div class="control"><input class="js-pay-ref" type="text" /></div>');

            $grid.append($amtField).append($dateField).append($methodField).append($bankField).append($discField).append($refField);
            $pay.append($grid);
            $bodyM.append($pay);

            // Settlement summary
            var $settle = $(
                '<div class="vas-apinv-settle">' +
                '<div class="srow"><span class="k js-sk1"></span><span class="v js-sv1"></span></div>' +
                '<div class="srow js-wh-row"><span class="k js-sk2"></span><span class="v js-sv2"></span></div>' +
                '<div class="srow"><span class="k js-sk3"></span><span class="v green js-credit"></span></div>' +
                '<div class="srow"><span class="k js-sk4"></span><span class="v js-pay"></span></div>' +
                '<div class="srow"><span class="k js-sk5"></span><span class="v js-disc"></span></div>' +
                '<div class="srow tot"><span class="k js-sk6"></span><span class="v js-settle"></span></div>' +
                '<div class="bar"><span class="js-bar"></span></div>' +
                '<div class="srow"><span class="k js-sk7"></span><span class="v js-remain"></span></div>' +
                '<div class="note js-over"></div>' +
                '</div>'
            );
            $settle.find(".js-sk1").text(lbl("VAS_065_GrossInvoiceTotal", "Gross invoice total"));
            $settle.find(".js-sv1").text(fmtAmountCur(meta.GrossInvoice, cur, p));
            $settle.find(".js-sk2").text(lbl("VAS_065_LessWithholding", "Less withholding"));
            $settle.find(".js-sv2").text(fmtAmountCur(meta.Withholding, cur, p));
            if (!(+meta.Withholding > 0)) $settle.find(".js-wh-row").hide();
            $settle.find(".js-sk3").text(lbl("VAS_065_CreditOnAccountApplied", "Credit / on-account applied"));
            $settle.find(".js-sk4").text(lbl("VAS_065_NewPayment", "New payment"));
            $settle.find(".js-sk5").text(lbl("VAS_065_CashDiscount", "Cash discount"));
            $settle.find(".js-sk6").text(lbl("VAS_065_SettlingThisInvoice", "Settling this invoice"));
            $settle.find(".js-sk7").text(lbl("VAS_065_RemainingBalance", "Remaining balance"));
            $settle.find(".js-over").text(lbl("VAS_065_OverpaymentNote", "Overpayment will be held as a vendor advance (on account)."));
            $bodyM.append($settle);

            // wire compute
            var $payAmt = $bodyM.find(".js-pay-amt");
            var $payDisc = $bodyM.find(".js-pay-disc");
            $bodyM.find(".js-pay-date").val(new Date().toISOString().slice(0, 10));
            $payAmt.val((invOpen).toFixed(p));

            function parseNum(s) { var n = parseFloat(String(s).replace(/[^0-9.\-]/g, "")); return isNaN(n) ? 0 : n; }

            function selectedCreditAmount() {
                var remaining = invOpen, total = 0;
                $bodyM.find(".vas-apinv-credit-row.on").each(function () {
                    var r = $(this).data("src");
                    var use = Math.min(+r.AvailableAmount, remaining);
                    if (use > 0) { total += use; remaining -= use; }
                });
                return total;
            }

            function updateCreditSummary() {
                var sel = selectedCreditAmount();
                var $btn = $bodyM.find(".js-apply-credits");
                var $sum = $bodyM.find(".js-credit-summary");
                if (state.allocationCreated) {
                    $btn.text(lbl("VAS_065_CreditsApplied", "Credits applied")).prop("disabled", true);
                    $sum.text(lbl("VAS_065_CreditsAppliedSummary", "{0} allocated. New payment amount has been recalculated.").replace("{0}", fmtAmountCur(state.applied, cur, p)));
                    return;
                }
                $btn.prop("disabled", sel <= 0);
                $sum.text(sel > 0
                    ? lbl("VAS_065_SelectedForAllocation", "{0} selected for allocation.").replace("{0}", fmtAmountCur(sel, cur, p))
                    : lbl("VAS_065_SelectCreditsHint", "Select available payments or credits to allocate before recording a new payment."));
            }

            function recompute() {
                var credit = state.applied;
                var disc = parseNum($payDisc.val());
                var pay = parseNum($payAmt.val());
                var settle = Math.min(credit + pay + disc, invOpen);
                var over = Math.max(0, (credit + pay + disc) - invOpen);
                var remain = Math.max(0, invOpen - credit - pay - disc);
                // Applied amount (credits + new payment + discount) may not exceed the
                // open invoice due amount; overpayment is blocked rather than advanced.
                var exceedsOpen = over > 1e-6;

                $settle.find(".js-credit").text(fmtAmountCur(credit, cur, p));
                $settle.find(".js-pay").text(fmtAmountCur(pay, cur, p));
                $settle.find(".js-disc").text(fmtAmountCur(disc, cur, p));
                $settle.find(".js-settle").text(fmtAmountCur(settle, cur, p));
                $settle.find(".js-remain").text(fmtAmountCur(remain, cur, p));
                $settle.find(".js-bar").css("width", Math.min(100, (settle / (invOpen || 1)) * 100) + "%");

                $payAmt.closest(".control").toggleClass("invalid", exceedsOpen);
                $settle.find(".js-over")
                    .text(lbl("VAS_065_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount"))
                    .toggleClass("err show", exceedsOpen);

                var $submit = $scrim.find(".js-submit");
                $submit.prop("disabled", exceedsOpen)
                    .text(pay <= 0 && credit > 0 ? lbl("VAS_065_CompleteSettlement", "Complete settlement") : lbl("VAS_065_RecordPayment", "Record payment"));

                var $foot = $scrim.find(".js-foot-msg");
                if (exceedsOpen) { $foot.text(lbl("VAS_065_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount")); }
                else if (remain <= 1e-6) { $foot.text(lbl("VAS_065_InvoiceFullySettled", "Invoice fully settled")); }
                else { $foot.text(lbl("VAS_065_WillRemainOpen", "{0} will remain open").replace("{0}", fmtAmountCur(remain, cur, p))); }
            }

            function applySelectedCredits() {
                var sel = selectedCreditAmount();
                if (sel <= 0 || state.allocationCreated) return;

                var sources = [];
                var remaining = invOpen;
                $bodyM.find(".vas-apinv-credit-row.on").each(function () {
                    var r = $(this).data("src");
                    var use = Math.min(+r.AvailableAmount, remaining);
                    if (use > 0) { sources.push({ SourceType: r.SourceType, Id: r.Id, Amount: use }); remaining -= use; }
                });

                var $btn = $bodyM.find(".js-apply-credits").prop("disabled", true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_065_APInvoicePanel/ApplyCredits",
                    type: "POST",
                    dataType: "json",
                    data: { payload: JSON.stringify({ C_Invoice_ID: $self.record_ID, Sources: sources }) },
                    success: function (raw) {
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (resp && resp.Success) {
                            state.applied = +resp.AppliedAmount || sel;
                            state.allocationCreated = true;
                            $bodyM.find(".vas-apinv-credit-row.on").addClass("locked")
                                .find(".ap").text(lbl("VAS_065_Allocated", "Allocated"));
                            $bodyM.find(".js-alloc-status").addClass("show");
                            $payAmt.val((Math.max(0, invOpen - state.applied)).toFixed(p));
                            updateCreditSummary();
                            recompute();
                        } else {
                            $btn.prop("disabled", false);
                            error((resp && resp.Message) || lbl("VAS_065_ActionFailed", "The action could not be completed."));
                        }
                    },
                    error: function (xhr) { $btn.prop("disabled", false); console.log(xhr); error(lbl("VAS_065_ActionFailed", "The action could not be completed.")); }
                });
            }

            // expose closures used by buttons
            $bodyM.data("updateCreditSummary", updateCreditSummary);
            $bodyM.data("recompute", recompute);
            $bodyM.data("applySelectedCredits", applySelectedCredits);
            $bodyM.data("getPayState", function () { return { state: state, payAmt: $payAmt, payDisc: $payDisc, invOpen: invOpen, parseNum: parseNum }; });

            $payAmt.on("input", recompute);
            $payDisc.on("input", recompute);
            updateCreditSummary();
            recompute();

            // bridge for credit-row click handlers declared earlier
            updateCreditSummaryRef = updateCreditSummary;
            recomputeRef = recompute;
        }

        var updateCreditSummaryRef = function () { };
        var recomputeRef = function () { };
        function updateCreditSummary() { updateCreditSummaryRef(); }
        function recompute() { recomputeRef(); }

        /* AP credit-note mode: allocate to open invoices */
        function buildCreditNoteSection($bodyM, p, cur) {
            var openInvoices = meta.OpenInvoices || [];
            var cnTotal = +meta.CreditNoteAmount || 0;

            var $sec = $('<div class="vas-apinv-m-sec"></div>');
            $sec.append($('<div class="vas-apinv-m-sec-h"></div>')
                .append($('<span class="t"></span>').text(lbl("VAS_065_AllocateToOpenInvoices", "Allocate to open invoices")))
                .append($('<span class="hint"></span>').text(lbl("VAS_065_SelectInvoicesToSettle", "Select invoices to settle with this credit note"))));

            if (!openInvoices.length) {
                $sec.append($('<div class="vas-apinv-alloc-empty"></div>').text(lbl("VAS_065_NoOpenInvoices", "No open invoices found for this vendor.")));
            }

            openInvoices.forEach(function (oi) {
                var $row = $('<div class="vas-apinv-open-invoice-row"></div>');
                $row.append('<span class="chk"><i class="fa fa-check"></i></span>');
                var $cl = $('<div class="cl"></div>');
                $cl.append($('<div class="a"></div>').text(lbl("VAS_065_PurchaseInvoiceDoc", "Purchase invoice - {0}").replace("{0}", oi.DocumentNo || "")));
                $cl.append($('<div class="b"></div>').text(
                    (oi.DueDate ? lbl("DueDate") + " " + fmtDate(oi.DueDate) + " · " : "") + lbl("Open")));
                $row.append($cl);
                var $amt = $('<div class="amt"></div>');
                $amt.append($('<div class="av"></div>').text(fmtAmountCur(oi.OpenAmount, cur, p)));
                $amt.append($('<div class="ap"></div>').text(lbl("Selected")));
                $row.append($amt);
                $row.data("inv", oi);
                $row.on("click", function () {
                    if ($row.hasClass("locked")) return;
                    $row.toggleClass("on");
                    recomputeCN();
                });
                $sec.append($row);
            });

            var $actions = $('<div class="vas-apinv-allocation-actions"></div>');
            var $summary = $('<span class="summary js-cn-summary"></span>').text(lbl("VAS_065_SelectInvoicesHint", "Select open invoices to allocate this credit note."));
            var $allocBtn = $('<button type="button" class="vas-apinv-btn is-outline js-alloc-cn"></button>')
                .text(lbl("VAS_065_AllocateSelectedInvoices", "Allocate selected invoices")).prop("disabled", true);
            $actions.append($summary).append($allocBtn);
            $sec.append($actions);
            $sec.append($('<div class="vas-apinv-alloc-status js-alloc-status"></div>').text(lbl("VAS_065_AllocationStatusMsg", "Allocation transaction created. Remaining balance is now available for new payment.")));
            $bodyM.append($sec);

            // Settlement summary (credit note)
            var $settle = $(
                '<div class="vas-apinv-settle">' +
                '<div class="srow"><span class="k js-k1"></span><span class="v js-v1"></span></div>' +
                '<div class="srow"><span class="k js-k2"></span><span class="v green js-applied"></span></div>' +
                '<div class="srow tot"><span class="k js-k3"></span><span class="v js-remain"></span></div>' +
                '<div class="bar"><span class="js-bar"></span></div>' +
                '</div>'
            );
            $settle.find(".js-k1").text(lbl("VAS_065_CreditNoteAmount", "Credit note amount"));
            $settle.find(".js-v1").text(fmtAmountCur(cnTotal, cur, p));
            $settle.find(".js-k2").text(lbl("VAS_065_SelectedInvoices", "Selected invoices"));
            $settle.find(".js-k3").text(lbl("VAS_065_CreditNoteRemaining", "Credit note remaining"));
            $bodyM.append($settle);

            function selectedAmount() {
                var remaining = cnTotal, total = 0;
                $bodyM.find(".vas-apinv-open-invoice-row.on").each(function () {
                    var oi = $(this).data("inv");
                    var use = Math.min(+oi.OpenAmount, remaining);
                    if (use > 0) { total += use; remaining -= use; }
                });
                return total;
            }

            function recomputeCN() {
                var apply = selectedAmount();
                var remain = Math.max(0, cnTotal - apply);
                $settle.find(".js-applied").text(fmtAmountCur(apply, cur, p));
                $settle.find(".js-remain").text(fmtAmountCur(remain, cur, p));
                $settle.find(".js-bar").css("width", Math.min(100, (apply / (cnTotal || 1)) * 100) + "%");
                $bodyM.find(".js-alloc-cn").prop("disabled", apply <= 0);
                $bodyM.find(".js-cn-summary").text(apply > 0
                    ? lbl("VAS_065_SelectedForAllocation", "{0} selected for allocation.").replace("{0}", fmtAmountCur(apply, cur, p))
                    : lbl("VAS_065_SelectInvoicesHint", "Select open invoices to allocate this credit note."));
            }

            $allocBtn.on("click", function () {
                var ids = [];
                $bodyM.find(".vas-apinv-open-invoice-row.on").each(function () {
                    ids.push($(this).data("inv").C_Invoice_ID);
                });
                if (!ids.length) return;
                $allocBtn.prop("disabled", true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_065_APInvoicePanel/AllocateCreditNote",
                    type: "POST",
                    dataType: "json",
                    data: { payload: JSON.stringify({ C_Invoice_ID: $self.record_ID, C_Invoice_IDs: ids }) },
                    success: function (raw) {
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (resp && resp.Success) {
                            $bodyM.find(".vas-apinv-open-invoice-row.on").addClass("locked").find(".ap").text(lbl("VAS_065_Allocated", "Allocated"));
                            $bodyM.find(".js-alloc-status").addClass("show");
                            recomputeCN();
                            showSuccess(lbl("VAS_065_CreditNoteAllocated", "Credit note allocated"), lbl("VAS_065_CreditNoteAllocatedMsg", "The selected invoices have been allocated against the AP credit note."), [
                                { k: lbl("VAS_065_AllocatedToInvoices", "Allocated to invoices"), v: fmtAmountCur(resp.AppliedAmount, cur, p) },
                                { k: lbl("VAS_065_CreditNoteRemaining", "Credit note remaining"), v: fmtAmountCur(resp.RemainingAmount, cur, p) }
                            ]);
                        } else {
                            $allocBtn.prop("disabled", false);
                            error((resp && resp.Message) || lbl("VAS_065_ActionFailed", "The action could not be completed."));
                        }
                    },
                    error: function (xhr) { $allocBtn.prop("disabled", false); console.log(xhr); error(lbl("VAS_065_ActionFailed", "The action could not be completed.")); }
                });
            });

            $bodyM.data("recomputeCN", recomputeCN);
            recomputeCN();
        }

        function buildModalFooter(isCN) {
            var $foot = $('<div class="vas-apinv-m-foot"></div>');
            $foot.append($('<span class="spacer js-foot-msg"></span>').text(isCN ? "" : lbl("VAS_065_InvoiceFullySettled", "Invoice fully settled")));
            $foot.append($('<button type="button" class="vas-apinv-btn is-ghost js-cancel"></button>')
                .text(lbl("VAS_065_Cancel", "Cancel")).on("click", closeModal));
            if (isCN) {
                $foot.append($('<button type="button" class="vas-apinv-btn is-primary js-submit-cn"></button>')
                    .text(lbl("VAS_065_AllocateCreditNote", "Allocate credit note")).on("click", submitCreditNote));
            } else {
                $foot.append($('<button type="button" class="vas-apinv-btn is-primary js-submit"></button>')
                    .text(lbl("VAS_065_RecordPayment", "Record payment")).on("click", submitPayment));
            }
            return $foot;
        }

        function submitPayment() {
            var $bodyM = $scrim.find(".vas-apinv-m-body");
            var getState = $bodyM.data("getPayState");
            if (!getState) return;
            var st = getState();
            var pay = st.parseNum(st.payAmt.val());
            var disc = st.parseNum(st.payDisc.val());

            if (pay <= 0 && st.state.applied > 0) {
                // Settlement is fully covered by the already-created allocation.
                var p = meta.StdPrecision >= 0 ? meta.StdPrecision : 2;
                var cur = meta.CurSymbol || meta.ISO_Code || "";
                showSuccess(lbl("VAS_065_CreditApplied", "Credit applied"), lbl("VAS_065_CreditAppliedMsg", "The selected on-account payment and credit have been allocated to this invoice."), [
                    { k: lbl("VAS_065_AllocationCreated", "Allocation created"), v: fmtAmountCur(st.state.applied, cur, meta.StdPrecision) }
                ]);
                return;
            }

            // Guard: applied credits + new payment + discount cannot exceed the open due.
            var applied = +st.state.applied || 0;
            if ((applied + pay + disc) - st.invOpen > 1e-6) {
                error(lbl("VAS_065_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount"));
                return;
            }

            var payload = {
                C_Invoice_ID: $self.record_ID,
                PayAmt: pay,
                C_Currency_ID: meta.C_Currency_ID,
                C_BankAccount_ID: parseInt($bodyM.find(".js-pay-bank").val(), 10) || 0,
                VA009_PaymentMethod_ID: parseInt($bodyM.find(".js-pay-method").val(), 10) || 0,
                DateTrx: $bodyM.find(".js-pay-date").val(),
                DiscountAmt: disc,
                ReferenceNo: $bodyM.find(".js-pay-ref").val()
            };
            var $submit = $scrim.find(".js-submit").prop("disabled", true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_065_APInvoicePanel/RecordPayment",
                type: "POST",
                dataType: "json",
                data: { payload: JSON.stringify(payload) },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (resp && resp.Success) {
                        var cur = meta.CurSymbol || meta.ISO_Code || "";
                        showSuccess(lbl("VAS_065_PaymentRecorded", "Payment recorded"),
                            lbl("VAS_065_PaymentRecordedMsg", "The payment {0} and allocation have been created against the net payable amount.").replace("{0}", resp.DocumentNo || ""),
                            [{ k: lbl("VAS_065_NewPayment", "New payment"), v: fmtAmountCur(resp.PayAmt, cur, meta.StdPrecision) }]);
                    } else {
                        $submit.prop("disabled", false);
                        error((resp && resp.Message) || lbl("VAS_065_ActionFailed", "The action could not be completed."));
                    }
                },
                error: function (xhr) { $submit.prop("disabled", false); console.log(xhr); error(lbl("VAS_065_ActionFailed", "The action could not be completed.")); }
            });
        }

        function submitCreditNote() {
            // Delegate to the allocate button which performs the same backend call.
            var $btn = $scrim.find(".js-alloc-cn");
            if ($btn.length && !$btn.prop("disabled")) { $btn.trigger("click"); }
            else { info(lbl("VAS_065_SelectInvoicesHint", "Select open invoices to allocate this credit note.")); }
        }

        function buildSuccessView() {
            return $(
                '<div class="vas-apinv-success js-success" style="display:none;">' +
                '<div class="vas-apinv-m-success">' +
                '<div class="circle"><i class="fa fa-check"></i></div>' +
                '<h3 class="js-ok-title"></h3>' +
                '<p class="js-ok-msg"></p>' +
                '<div class="recap js-ok-recap"></div>' +
                '</div>' +
                '<div class="vas-apinv-m-foot">' +
                '<button type="button" class="vas-apinv-btn is-primary js-done"></button>' +
                '</div>' +
                '</div>'
            );
        }

        function showSuccess(title, message, recapRows) {
            if (!$scrim) return;
            $scrim.find(".js-form").hide();
            var $s = $scrim.find(".js-success").show();
            $s.find(".js-ok-title").text(title);
            $s.find(".js-ok-msg").text(message);
            var $recap = $s.find(".js-ok-recap").empty();
            (recapRows || []).forEach(function (r) {
                $recap.append($('<div class="rr"></div>')
                    .append($('<span class="k"></span>').text(r.k))
                    .append($('<span class="v"></span>').text(r.v)));
            });
            $s.find(".js-done").text(lbl("VAS_065_Done", "Done")).off("click").on("click", function () {
                closeModal();
                $self.fetchData($self.record_ID);
            });
        }

        function field(labelText, controlHtml) {
            var $f = $('<div class="vas-apinv-field"></div>');
            $f.append($('<label></label>').text(labelText));
            $f.append(controlHtml);
            return $f;
        }

        function closeModal() {
            if (!$scrim) return;
            $scrim.removeClass("open");
            $(document).off("keydown.vasApinvModal");
            setTimeout(removeModal, 220);
        }
        function removeModal() {
            if ($scrim) { $scrim.remove(); $scrim = null; }
        }

        /* ---------- misc ---------- */
        function scrollToSection(cssClass) {
            var el = $body.find("." + cssClass)[0];
            if (!el) return;
            el.scrollIntoView({ behavior: "smooth", block: "start" });
            var $el = $(el);
            $el.removeClass("flash"); void el.offsetWidth; $el.addClass("flash");
        }
        function escapeHtml(s) {
            return String(s == null ? "" : s)
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
        }
        function info(msg) {
            if (VIS && VIS.ADialog && VIS.ADialog.info) VIS.ADialog.info(msg); else console.log(msg);
        }
        function error(msg) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) VIS.ADialog.error(msg); else console.log(msg);
        }

        // Generate the invoice PDF via the framework print process and download it.
        // AD_Process_ID / AD_Table_ID come straight off the current grid tab; the
        // PDF is written to TempDownload and opened from there.
        function downloadInvoicePDF() {
            if (!$self.record_ID || !$self.curTab) return;

            var tableId = (typeof $self.curTab.getAD_Table_ID === "function") ? $self.curTab.getAD_Table_ID() : 0;
            var processId = (typeof $self.curTab.getAD_Process_ID === "function") ? $self.curTab.getAD_Process_ID() : 0;
            if (!processId || !tableId) {
                error(lbl("VAS_065_DownloadPDF", "Download PDF"));
                return;
            }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "JsonData/GeneratePrint/",
                dataType: "json",
                data: {
                    AD_Process_ID: processId,           // from curTab
                    Name: "Print",
                    AD_Table_ID: tableId,             // from curTab
                    Record_ID: $self.record_ID,     // the invoice
                    WindowNo: $self.windowNo,
                    filetype: "P",                 // P = PDF
                    actionOrigin: "W",
                    originName: "AP Invoice"
                },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!res) {
                        showBusy(false);
                        return;
                    }

                    // GeneratePrint writes to TempDownload and returns the file name/path.
                    var file = res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path;
                    if (!file) {
                        var error = (rep && rep.ErrorText) || (res && res.ReportProcessInfo && res && res.ReportProcessInfo.Summary)
                        VIS.ADialog.error("", "", error);
                        console.log("GeneratePrint response:", res);
                        showBusy(false);
                        return;
                    }

                    showBusy(false);
                    window.open(VIS.Application.contextUrl + file, "_blank");
                },
                error: function (err) {
                    console.log(err);
                    showBusy(false);
                }
            });
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_065_APInvoicePanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_065_APInvoicePanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_065_APInvoicePanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_065_APInvoicePanel.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);

