/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Requisition overview tab panel. Renders a
 *                  review-oriented overview of the selected requisition
 *                  (M_Requisition): identity, requester / preparer, origin +
 *                  warehouse route, convert actions, a budget-aware stat strip,
 *                  a 6-stage progress stepper, and a tabbed lower region
 *                  (Items / Activity / Notes). Data is fetched from
 *                  VAS_098_PurchaseRequisition/GetRequisitionOverview.
 *                  Design follows the attached requisition-window reference.
 * Chronological development:
 *   VAI163   2026-07-01  Created (modelled on VAS_092_OverviewPurchaseOrder).
 *   VAI163   2026-07-02  Reworked the header to the VAS_092 pattern: a soft-
 *                        gradient title strip (title + subtitle + priority /
 *                        type / status pills) whose tint follows requisition
 *                        progress, above a white two-column details card with
 *                        the Source Warehouse leading the left column. Replaces
 *                        the former identity card + separate route strip.
 *   VAI163   2026-07-02  Added a "Create RFQ" document-action button beside the
 *                        two conversion actions, and moved every on-screen label
 *                        behind VIS.Msg.getMsg("VAS_098_*") (seed in AD_Message).
 *   VAI163   2026-07-28  - Dropped the requester-warehouse route line from the
 *                          details card's left column; it already reads as a
 *                          labelled field in the right column.
 *                        - People now carry a "People" heading and a visible role
 *                          (Requester / Preparer) beside each name.
 *                        - Progress "Drafted" stage captions with the record's
 *                          creation date instead of the document date.
 *                        - Line items show the Attribute Set Instance sub-line and
 *                          drop the "SKU" prefix before the product search key;
 *                          the full product name is a hover tooltip.
 *                        - Removed the Contingency total from the items footer.
 *                        - msg() takes an optional English fallback so an unseeded
 *                          key never renders as raw "VAS_098_*" text.
 *   VAI163   2026-07-28  - Source availability (card + line column) now reports
 *                          real on-hand stock at the source warehouse; 0 is a
 *                          value, and only a requisition without a source
 *                          warehouse reads N/A.
 *                        - The three convert buttons are functional: each
 *                          confirms, POSTs to the controller, shows the process
 *                          result in a toast and refreshes the panel. Create RFQ
 *                          no longer greys out just because the requisition has
 *                          already been converted to a purchase order.
 *                        - Progress stages are captioned with real dates:
 *                          completion under Submitted / Completed, PO creation
 *                          under Converted, PO completion under In Fulfilment and
 *                          the close under Closed.
 *   VAI163   2026-07-28  - The status pill and the progress header now show the
 *                          requisition's own DocStatus; "Converted" is a progress
 *                          state and no longer masks it.
 *                        - Added a posting-status badge (M_Requisition.Posted).
 *                        - Added a Reference field carrying the purchase order
 *                          raised from this requisition.
 *                        - Timestamps are parsed as wall-clock time so the
 *                          creation time matches the requisition window instead of
 *                          being shifted into the browser's timezone.
 *   VAI163   2026-07-28  - Line items show the UOM in place of the product
 *                          category.
 *                        - Items / Activity / Notes are stacked sections down the
 *                          panel instead of tabs, so Activity and Notes sit at the
 *                          bottom and need no click.
 *                        - Action buttons renamed to the document they produce:
 *                          Material Transfer / RFQ / Purchase Order.
 *                        - The converted note reads "Converted".
 *   VAI163   2026-07-28  - Activity carries the full lifecycle: PO Created, GRN
 *                          Created and GRN Completed, each with document no and
 *                          timestamp.
 *                        - Convert strip wraps inside the panel instead of
 *                          overflowing it; line items page at 10 rows.
 *                        - Priority badge colours match the requisition window.
 *                        - The items footer reports the Budget set for the
 *                          requisition in place of the estimated subtotal.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_098_PurchaseRequisition = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;

        // Line items page client-side (the whole set arrives in one payload); the
        // page resets whenever a different record is loaded.
        var LINES_PER_PAGE = 10;
        var linesPage = 0;

        // ---- Code maps: status/priority codes -> message key + tone. Labels
        //      are looked up through VIS.Msg (AD_Message VAS_098_*) at render. ---- //
        var STATUS_MAP = {
            "DR": { key: "Draft",       tone: "draft"     },
            "IP": { key: "InProgress",  tone: "partial"   },
            "AP": { key: "Approved",    tone: "approved"  },
            "CO": { key: "Completed",   tone: "approved"  },
            "CL": { key: "Closed",      tone: "sent"      },
            "VO": { key: "Voided",      tone: "cancelled" },
            "RE": { key: "Reversed",    tone: "cancelled" },
            "WC": { key: "WaitingConfirmation", tone: "partial" },
            "WP": { key: "WaitingPayment",      tone: "partial" },
            "IN": { key: "Invalid",     tone: "cancelled" },
            "NA": { key: "NotApproved", tone: "cancelled" }
        };
        // Tones match the colours the requisition window itself uses for the
        // PriorityRule field: urgent red, high orange, medium blue, low green,
        // minor grey. Urgent and High used to share one tone, and Low shared grey
        // with Minor, so the badge disagreed with the screen.
        var PRIORITY_MAP = {
            "1": { key: "UrgentPriority", tone: "urgent" },
            "3": { key: "HighPriority",   tone: "high"   },
            "5": { key: "MediumPriority", tone: "med"    },
            "7": { key: "LowPriority",    tone: "low"    },
            "9": { key: "MinorPriority",  tone: "minor"  }
        };

        this.init = function () {
            $root = $('<div class="MPC-vasrq-root"></div>');
            $body = $('<div class="MPC-vasrq-body"></div>');
            $emptyState = $('<div class="MPC-vasrq-empty" style="display:none;"></div>');
            $emptyState.text(msg("NoData"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
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

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_098_PurchaseRequisition/GetRequisitionOverview",
                type: "GET",
                dataType: "json",
                data: { M_Requisition_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    linesPage = 0;
                    render();
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
            linesPage = 0;
            render();
        };

        function render() {
            // Nothing to draw into until startPanel() -> init() has built the DOM.
            if (!$body) return;

            $body.empty();

            if (!data || !data.M_Requisition_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHead();
            renderDetails();
            renderConvert();
            renderStats();
            renderProgress();
            renderLower();
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                           //
        // ----------------------------------------------------------------- //

        // Localised label lookup. All on-screen text is seeded in AD_Message as
        // VAS_098_<key>. VIS.Msg returns the key itself when it is not seeded, so
        // an optional English fallback keeps a raw "VAS_098_Foo" off the screen
        // until the message is added to the dictionary.
        function msg(key, fallback) {
            var full = "VAS_098_" + key;
            try {
                var m = VIS.Msg.getMsg(full);
                if (m && m !== full) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : full;
        }

        // The requisition's own document status. "Converted" is a progress state,
        // not a DocStatus, and it used to be substituted here — which meant a
        // completed requisition stopped reporting that it was Completed once a PO
        // had been raised. Conversion is still visible in its own right: the
        // convert strip states it and the progress stepper has a Converted stage.
        function statusMeta() {
            var m = STATUS_MAP[data.StatusCode];
            if (m) return { label: msg(m.key), tone: m.tone };
            return { label: data.StatusCode || msg("NA"), tone: "draft" };
        }

        // Posting status of the record (M_Requisition.Posted). Anything other than
        // 'Y' is "not posted"; 'E' is a posting error and is called out as such.
        function postedMeta() {
            if (data.Posted) return { label: msg("Posted", "Posted"), tone: "approved" };
            if (data.PostedCode === "E")
                return { label: msg("PostingError", "Posting Error"), tone: "cancelled" };
            return { label: msg("NotPosted", "Not Posted"), tone: "draft" };
        }

        function priorityMeta() {
            var m = PRIORITY_MAP[data.PriorityCode];
            if (m) return { label: msg(m.key), tone: m.tone };
            return { label: msg("NormalPriority"), tone: "med" };
        }

        function procurementType() {
            return data.SourceWarehouseName ? msg("InternalFulfillment") : msg("PurchaseRequisition");
        }

        function tag(label, tone) {
            var $t = $('<span class="MPC-vasrq-tag"></span>').addClass(tone || "draft");
            $t.append($('<span class="MPC-vasrq-dot"></span>'));
            $t.append(document.createTextNode(label));
            return $t;
        }

        // ---------------------------- Head ------------------------------- //

        // VAI163 2026-07-02  Reworked to the VAS_092 header pattern: a title strip
        // (title + subtitle, with the priority / type / status pills on the
        // right).
        // VAI163 2026-07-17  Title strip is now untinted, matching VAS_106; the
        // status is still carried by the pills on the right.
        function renderHead() {
            var st = statusMeta();
            var pm = priorityMeta();

            var $head = $('<div class="MPC-vasrq-hdr"></div>');
            var $top = $('<div class="MPC-vasrq-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasrq-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasrq-hdrTitle"></div>').text(
                msg("Requisition") + " — " + (data.DocumentNo || msg("NA"))));
            var subBits = [procurementType()];
            var raised = formatDate(data.DateDoc || data.Created);
            if (raised) subBits.push(msg("Raised") + " " + raised);
            $tl.append($('<div class="MPC-vasrq-hdrSub"></div>').text(subBits.join(" · ")));
            $top.append($tl);

            var $pills = $('<div class="MPC-vasrq-hdrPills"></div>');
            $pills.append(priorityPill(pm));
            if (data.SourceWarehouseName) {
                // origin / procurement-type chip (design shows a chip here)
                $pills.append(tag(procurementType(), "mwo"));
            }
            $pills.append(tag(st.label, st.tone));
            // Posting status of the record, beside the document status.
            var pst = postedMeta();
            $pills.append(tag(pst.label, pst.tone));
            $top.append($pills);

            $head.append($top);
            $body.append($head);
        }

        function priorityPill(pm) {
            var $p = $('<span class="MPC-vasrq-prio"></span>').addClass(pm.tone);
            $p.append(svgIcon("chevrons"));
            $p.append(document.createTextNode(pm.label));
            return $p;
        }

        // --------------------- Header details card ----------------------- //

        // VAI163 2026-07-02  Two-column details card (VAS_092 pattern): the goods
        // source — the Source Warehouse — leads the LEFT column (with the transfer
        // route and the requester / preparer), and labelled meta fields fill the
        // right. Replaces the former identity card + separate route strip. Purpose
        // is no longer surfaced here — it already lives in the Notes tab.
        // VAI163 2026-07-28  Dropped the requester-warehouse route line from the
        // left column: the same value is a labelled field in the right column, so
        // it was showing twice in the card.
        function renderDetails() {
            var $card = $('<div class="MPC-vasrq-hdrCard"></div>');

            // Left: Source Warehouse (the source of goods) + route + people.
            var $l = $('<div class="MPC-vasrq-hdrColL"></div>');
            $l.append($('<div class="MPC-vasrq-fLabel"></div>').text(msg("SourceWarehouse")));
            $l.append($('<div class="MPC-vasrq-srcName"></div>').text(
                data.SourceWarehouseName || msg("ExternalProcurement")));
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.replace(/\s/g, "")) $l.append(headerField(msg("Currency"), cur));
            // The requester warehouse used to repeat here as a route line; it is
            // already a labelled field in the right column, so this column now
            // carries only the source warehouse, currency and the people.

            // People — a heading plus a visible role on each name, so it is clear
            // who the names belong to (the role used to be a hover title only).
            var $people = $('<div class="MPC-vasrq-srcPeople"></div>');
            appendPersonBit($people, data.RequesterName, msg("Requester"));
            appendPersonBit($people, data.PreparerName, msg("Preparer"));
            if ($people.children().length) {
                $l.append($('<div class="MPC-vasrq-fLabel MPC-vasrq-peopleLbl"></div>')
                    .text(msg("People", "People")));
                $l.append($people);
            }
            $card.append($l);

            // Right: labelled meta fields.
            var $r = $('<div class="MPC-vasrq-hdrColR"></div>');
            $r.append(headerField(msg("ProcurementType"), procurementType()));
            $r.append(headerField(msg("PriceList"), data.PriceListName || msg("NA")));
            $r.append(headerField(msg("RequestWarehouse"), data.RequestWarehouseName || msg("NA")));
            // Reference — the purchase order raised from this requisition. N/A
            // until it has been converted.
            $r.append(headerField(msg("Reference", "Reference"), referenceText()));
           
           
            $card.append($r);

            $body.append($card);
        }

        // Person chip: user icon, the role (Requester / Preparer) and the name.
        // The role is rendered, not just a tooltip, so a single name is never
        // ambiguous about which of the two it is.
        function appendPersonBit($container, value, role) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasrq-personBit"></span>').attr("title", role + ": " + value);
            $bit.append(svgIcon("user"));
            $bit.append($('<span class="MPC-vasrq-personRole"></span>').text(role));
            $bit.append($('<span class="MPC-vasrq-personName"></span>').text(value));
            $container.append($bit);
        }

        // The purchase order this requisition produced. When it produced more than
        // one, the first is named and the rest counted.
        function referenceText() {
            if (!data.OrderDocumentNo) return msg("NA");
            var count = +data.OrderCount || 0;
            return count > 1
                ? data.OrderDocumentNo + " +" + (count - 1) + " " + msg("More", "more")
                : data.OrderDocumentNo;
        }

        // Labelled field block (uppercase caption + value) for the right column.
        function headerField(label, value) {
            var $f = $('<div class="MPC-vasrq-hdrField"></div>');
            $f.append($('<div class="MPC-vasrq-fLabel"></div>').text(label));
            $f.append($('<div class="MPC-vasrq-fVal"></div>').text(value));
            return $f;
        }

        // ------------------------ Convert strip -------------------------- //

        function renderConvert() {
            var canConvert = data.StatusCode === "CO" && !data.IsConverted;

            var $strip = $('<div class="MPC-vasrq-convert"></div>');

            var noteText, noteOk = false;
            if (data.IsConverted) {
                // "Converted" — the note used to read "Already converted".
                noteText = msg("Converted");
                noteOk = true;
            } else if (canConvert) {
                noteText = msg("ReadyToConvertNote");
                noteOk = true;
            } else {
                noteText = msg("ConversionAvailable");
            }

            var $note = $('<span class="MPC-vasrq-cvnote"></span>');
            if (noteOk) $note.append($('<span class="MPC-vasrq-ok"></span>').append(svgIcon("check")));
            $note.append(document.createTextNode(noteText));
            $strip.append($note);

            // Document actions, each run through the platform's process engine via
            // the controller. Preconditions are per-action rather than one shared
            // flag: raising an RFQ is still legitimate after the requisition has
            // been converted to a PO, so it no longer greys out with the others.
            var isCompleted = data.StatusCode === "CO" || data.StatusCode === "CL";
            var isClosedOff = data.StatusCode === "VO" || data.StatusCode === "RE";
            var canAct = isCompleted && !isClosedOff;

            // Button labels name the target document only — the "Convert to" /
            // "Create" verb has been dropped from each. New message keys are used
            // so the old seeded ConvertTo* / CreateRFQ text does not resurface.
            var $actions = $('<div class="MPC-vasrq-cvactions"></div>');
            $actions.append(convertBtn(msg("MaterialTransfer", "Material Transfer"), "transfer", "primary",
                canConvert, "ConvertToMaterialTransfer",
                msg("ConfirmMaterialTransfer",
                    "Create a material transfer from this requisition?")));
            $actions.append(convertBtn(msg("RFQ", "RFQ"), "rfq", "secondary",
                canAct, "CreateRFQ",
                msg("ConfirmCreateRFQ",
                    "Create an RFQ from this requisition?")));
            $actions.append(convertBtn(msg("PurchaseOrder", "Purchase Order"), "external", "secondary",
                canConvert, "ConvertToPurchaseOrder",
                msg("ConfirmConvertToPO",
                    "Create the purchase order(s) for this requisition?")));
            $strip.append($actions);

            $body.append($strip);
        }

        // Action button: confirms, POSTs to the controller, then refreshes the
        // panel so the status, progress stepper and button states reflect the
        // result. Disabled buttons carry a reason as a tooltip.
        function convertBtn(label, icon, variant, enabled, endpoint, confirmText) {
            var $b = $('<button type="button" class="MPC-vasrq-btn"></button>').addClass(variant);
            $b.append(svgIcon(icon));
            $b.append(document.createTextNode(label));

            if (!enabled) {
                $b.prop("disabled", true);
                $b.attr("title", data.IsConverted
                    ? msg("AlreadyConverted")
                    : msg("ConversionAvailable"));
                return $b;
            }

            $b.on("click", function () {
                if (!confirm(confirmText)) return;
                runAction($b, endpoint, label);
            });
            return $b;
        }

        // POSTs a convert action and refreshes on success. Guards against
        // double-clicks while the request is in flight.
        function runAction($btn, endpoint, label) {
            if ($btn.prop("disabled")) return;
            $btn.prop("disabled", true);
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_098_PurchaseRequisition/" + endpoint,
                type: "POST",
                dataType: "json",
                data: { M_Requisition_ID: $self.record_ID },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.success) {
                        toast(res.message || label, false);
                        // Re-fetch: conversion changes the progress stepper, the
                        // convert note and which actions still apply.
                        $self.fetchData($self.record_ID);
                    } else {
                        $btn.prop("disabled", false);
                        showBusy(false);
                        toast((res && (res.error || res.message)) ||
                              msg("ActionFailed", "The action could not be completed."), true);
                    }
                },
                error: function () {
                    $btn.prop("disabled", false);
                    showBusy(false);
                    toast(msg("ActionFailed", "The action could not be completed."), true);
                }
            });
        }

        // Lightweight self-contained toast.
        function toast(message, isError) {
            var $t = $('<div class="MPC-vasrq-toast"></div>')
                .addClass(isError ? "err" : "ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("show"); }, 10);
            setTimeout(function () {
                $t.removeClass("show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 4200);
        }

        // -------------------------- Stat strip --------------------------- //

        function renderStats() {
            var $strip = $('<div class="MPC-vasrq-stats"></div>');

            // Estimated value (breach-aware)
            var $s1 = statCard(data.IsBudgetBreach ? "breach" : "a-blue", msg("EstimatedValue"));
            var $v1 = $('<div class="MPC-vasrq-sval"></div>').text(money(data.EstimatedValue));
            if (data.IsBudgetBreach) {
                $v1.append($('<span class="MPC-vasrq-breachic"></span>')
                    .attr("title", msg("BudgetBreached")).append(svgIcon("warn")));
            }
            $s1.append($v1);
            $s1.append(statSub(budgetSubText(), data.IsBudgetBreach ? "breach" : ""));
            $strip.append($s1);

            // Required by
            var $s2 = statCard("a-violet", msg("RequiredBy"));
            $s2.append($('<div class="MPC-vasrq-sval"></div>').text(formatDate(data.DateRequired) || msg("NA")));
            $s2.append(statSub(requiredSubText(), ""));
            $strip.append($s2);

            // Line items
            var $s3 = statCard("a-green", msg("LineItems"));
            $s3.append($('<div class="MPC-vasrq-sval"></div>').text((data.LineCount || 0) + " " + msg("Lines")));
            $s3.append(statSub(formatNumber(data.RequestedUnits) + " " + msg("UnitsRequested"), ""));
            $strip.append($s3);

            // Source availability — on-hand stock at the source warehouse. Shows a
            // real quantity (0 included) whenever a source warehouse is configured;
            // only external procurement, which has no source warehouse, reads N/A.
            var $s4 = statCard("a-amber", msg("SourceAvailability"));
            if (data.HasSourceData) {
                $s4.append($('<div class="MPC-vasrq-sval"></div>')
                    .text(formatNumber(data.SourceStockOnHand || 0)));
                $s4.append(statSub(
                    msg("OnHandAtSource", "on hand") +
                    (data.SourceWarehouseName ? " · " + data.SourceWarehouseName : "") +
                    " · " + (data.FullyInStockLines || 0) + " / " + (data.LineCount || 0) + " " +
                    msg("LinesFullyInStock"), ""));
            } else {
                $s4.append($('<div class="MPC-vasrq-sval"></div>').text(msg("NA")));
                $s4.append(statSub(msg("SourceStockNA"), ""));
            }
            $strip.append($s4);

            $body.append($strip);
        }

        function statCard(accent, cap) {
            var $s = $('<div class="MPC-vasrq-stat"></div>').addClass(accent);
            $s.append($('<div class="MPC-vasrq-scap"></div>').text(cap));
            return $s;
        }

        function statSub(text, cls) {
            return $('<div class="MPC-vasrq-ssub"></div>').addClass(cls || "").text(text);
        }

        function budgetSubText() {
            if (data.IsBudgetBreach) {
                if (data.BudgetBreachNote) return data.BudgetBreachNote;
                if (data.BudgetOverage > 0) return msg("OverBudgetBy") + " " + money(data.BudgetOverage);
                return msg("BudgetBreached");
            }
            return msg("WithinBudget");
        }

        function requiredSubText() {
            if (!data.DateRequired) return msg("NoDateSet");
            // Wall-clock parsing here too: a timezone shift on either date can move
            // the day-count across a midnight and report the wrong days remaining.
            var req = parseServerDate(data.DateRequired);
            var sys = parseServerDate(data.SystemDate) || new Date();
            if (!req) return "";
            var days = Math.round((stripTime(req) - stripTime(sys)) / 86400000);
            if (days > 0) return days + " " + msg("DaysRemaining");
            if (days < 0) return Math.abs(days) + " " + msg("DaysOverdue");
            return msg("DueToday");
        }

        // -------------------------- Progress ----------------------------- //

        // Every stage is captioned with the moment it actually happened, all of
        // which the model derives from the record's workflow and from the purchase
        // orders raised against it (never from the document date, which can be
        // back- or forward-dated).
        function progressStages() {
            var s = data.StatusCode;
            var submitted  = data.Processed || s === "IP" || s === "AP" || s === "CO" || s === "CL";
            var completed  = s === "CO" || s === "CL" || data.IsConverted;
            var converted  = data.IsConverted;
            // In fulfilment once a purchase order raised from this requisition has
            // been completed — not from a per-line ordered quantity.
            var fulfilment = data.HasOrdered;
            // Closed when the requisition itself is closed, or when every purchase
            // order raised from it has closed.
            var closed     = data.IsClosed;

            return [
                { key: "c1", label: msg("Drafted"),      done: true,       sub: formatDateShort(data.Created) },
                { key: "c2", label: msg("Submitted"),    done: submitted,  sub: formatDateShort(data.CompletedDate) },
                { key: "c3", label: msg("Completed"),    done: completed,  sub: formatDateShort(data.CompletedDate) },
                { key: "c4", label: msg("Converted"),    done: converted,  sub: formatDateShort(data.ConvertedDate) },
                { key: "c5", label: msg("InFulfilment"), done: fulfilment, sub: formatDateShort(data.FulfilmentDate) },
                { key: "c6", label: msg("Closed"),       done: closed,     sub: formatDateShort(data.ClosedDate) }
            ];
        }

        function renderProgress() {
            var stages = progressStages();

            // Monotonic reach: a stage is "reached" if it or any later stage is done.
            var reached = [];
            for (var i = 0; i < stages.length; i++) {
                var any = false;
                for (var j = i; j < stages.length; j++) { if (stages[j].done) { any = true; break; } }
                reached.push(any);
            }
            var current = 1;
            for (var k = 0; k < reached.length; k++) { if (reached[k]) current = k + 1; }

            var st = statusMeta();
            var $sh = $('<div class="MPC-vasrq-sechead"></div>');
            $sh.append($('<h2></h2>').text(msg("RequisitionProgress")));
            $sh.append($('<span class="MPC-vasrq-secright"></span>').text(
                msg("Stage") + " " + current + " " + msg("Of") + " " + stages.length + " · " + st.label));
            $body.append($sh);

            var $stepper = $('<div class="MPC-vasrq-stepper"></div>');
            for (var s = 0; s < stages.length; s++) {
                var stg = stages[s];
                var stateCls, sub, showCheck;
                if (s + 1 < current) { stateCls = "done";    showCheck = true;  sub = stg.sub || ""; }
                else if (s + 1 === current) { stateCls = "active"; showCheck = false; sub = activeSub(stg, current); }
                else { stateCls = "pending"; showCheck = false; sub = msg("Pending"); }
                $stepper.append(stepEntry(s + 1, stg, stateCls, showCheck, sub));
            }
            $body.append($stepper);
        }

        function activeSub(stg, current) {
            if (stg.key === "c3") return msg("ReadyToConvert");
            if (stg.sub) return stg.sub;
            return msg("InProgressSub");
        }

        function stepEntry(num, stg, stateCls, showCheck, sub) {
            var $step = $('<div class="MPC-vasrq-step"></div>').addClass(stateCls).addClass(stg.key);
            var $node = $('<div class="MPC-vasrq-node"></div>');
            if (showCheck) $node.append(svgIcon("check")); else $node.text(num);
            $step.append($node);
            $step.append($('<div class="MPC-vasrq-slabel"></div>').text(stg.label));
            if (sub) $step.append($('<div class="MPC-vasrq-ssub2"></div>').text(sub));
            return $step;
        }

        // -------------------- Lower region (stacked) --------------------- //

        // Items, then Activity, then Notes — each a headed section stacked down the
        // panel. These used to be three tabs; Activity and Notes now sit at the
        // bottom where they are visible without a click.
        function renderLower() {
            var lines = (data.Lines) || [];
            var activity = (data.Activity) || [];

            sectionHead(msg("Items"), lines.length + " " + msg("Lines"));
            $body.append(renderItemsPanel(lines));

            sectionHead(msg("Activity"), activity.length + " " + msg("Updates", "updates"));
            $body.append(renderActivityPanel(activity));

            sectionHead(msg("Notes"), "");
            $body.append(renderNotesPanel());
        }

        // Section header: title on the left, optional summary on the right.
        function sectionHead(title, summary) {
            var $sh = $('<div class="MPC-vasrq-sechead MPC-vasrq-lowerhead"></div>');
            $sh.append($('<h2></h2>').text(title));
            if (summary) $sh.append($('<span class="MPC-vasrq-secright"></span>').text(summary));
            $body.append($sh);
            return $sh;
        }

        // ---- Items ---- //

        function renderItemsPanel(lines) {
            var $panel = $('<div class="MPC-vasrq-lowersec"></div>');
            var $items = $('<div class="MPC-vasrq-items"></div>');

            var $head = $('<div class="MPC-vasrq-itrow MPC-vasrq-ithead"></div>');
            $head.append($('<span></span>').text(msg("Item")));
            $head.append($('<span></span>').text(msg("UOM", "UOM")));
            $head.append($('<span class="ta-c"></span>').text(msg("Qty")));
            $head.append($('<span class="ta-r"></span>').text(msg("SourceStock")));
            $head.append($('<span class="ta-r"></span>').text(msg("UnitCost")));
            $head.append($('<span class="ta-r"></span>').text(msg("EstTotal")));
            $items.append($head);

            if (!lines.length) {
                $items.append($('<div class="MPC-vasrq-itempty"></div>').text(msg("NoLineItems")));
                $panel.append($items);
                return $panel;
            }

            // Totals footer always covers the whole requisition, never the page.
            var $foot = itemsFooter();
            $items.append($foot);
            $panel.append($items);

            // The pager sits outside the items box: that box takes its own
            // horizontal scroll on narrow panels and the controls must not scroll
            // away with the columns.
            var $pager = $('<div class="MPC-vasrq-pager"></div>');
            if (lines.length > LINES_PER_PAGE) $panel.append($pager);

            // Rows are replaced in place, ahead of the footer, so the table's
            // structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * LINES_PER_PAGE;
                var end = Math.min(lines.length, start + LINES_PER_PAGE);

                $items.find(".MPC-vasrq-itbody").remove();
                for (var i = start; i < end; i++) $foot.before(itemRow(lines[i]));

                buildPager($pager, lines.length, pageCount, start, end, paintPage);
            }

            paintPage();
            return $panel;
        }

        // Range caption on the left, Previous / page-of / Next on the right.
        // Rebuilt on every page change so the disabled states stay accurate.
        function buildPager($pager, total, pageCount, start, end, onChange) {
            $pager.empty();
            if (total <= LINES_PER_PAGE) return;

            $pager.append($('<span class="MPC-vasrq-pgRange"></span>').text(
                msg("Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("Of") + " " + total));

            var $ctrls = $('<span class="MPC-vasrq-pgCtrls"></span>');
            $ctrls.append(pagerButton(msg("Previous", "Previous"), "chevLeft",
                linesPage <= 0, function () { linesPage--; onChange(); }));
            $ctrls.append($('<span class="MPC-vasrq-pgPos"></span>').text(
                msg("Page", "Page") + " " + (linesPage + 1) + " " +
                msg("Of") + " " + pageCount));
            $ctrls.append(pagerButton(msg("Next", "Next"), "chevRight",
                linesPage >= pageCount - 1, function () { linesPage++; onChange(); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="MPC-vasrq-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        function itemRow(ln) {
            var $r = $('<div class="MPC-vasrq-itrow MPC-vasrq-itbody"></div>');

            var $item = $('<span></span>');
            var pname = ln.ProductName || msg("NA");
            $item.append($('<div class="MPC-vasrq-itname"></div>').text(pname).attr("title", pname));
            // Product search key, shown without the former "SKU" prefix.
            if (ln.ProductValue) {
                $item.append($('<div class="MPC-vasrq-itsku"></div>').text(ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasrq-itsku"></div>').text(ln.Description));
            }
            // Attribute Set Instance (lot / serial / size ...), only when the line
            // carries a real instance — a blank or "--" placeholder is not shown.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="MPC-vasrq-itattr"></div>').text(asi).attr("title", asi));
            }
            $r.append($item);

            // Unit of measure (replaced the product category column).
            var $uom = $('<span></span>');
            if (ln.UOMName) $uom.append($('<span class="MPC-vasrq-uom"></span>').text(ln.UOMName));
            else $uom.append($('<span class="MPC-vasrq-na"></span>').text(msg("NA")));
            $r.append($uom);

            $r.append($('<span class="ta-c"></span>').text(formatNumber(ln.RequestedQty, ln.UOMPrecision)));

            $r.append(sourceCell(ln));

            $r.append($('<span class="ta-r"></span>').text(money(ln.UnitPrice)));
            $r.append($('<span class="ta-r"></span>').text(money(ln.LineAmount)));
            return $r;
        }

        // On-hand stock for this line's product at the source warehouse, against
        // the requested quantity. N/A only when the requisition has no source
        // warehouse at all — with one configured, no stock reads as 0, not N/A.
        function sourceCell(ln) {
            var $c = $('<span class="ta-r"></span>');
            if (!ln.HasSourceData) {
                $c.append($('<span class="MPC-vasrq-na"></span>').text(msg("NA")));
                return $c;
            }
            var req = +ln.RequestedQty || 0;
            var onHand = +ln.SourceQtyOnHand || 0;
            var pct = req > 0 ? Math.round((onHand / req) * 100) : (onHand > 0 ? 100 : 0);
            var cls = (req > 0 && onHand >= req) ? "full" : "short";
            var $src = $('<span class="MPC-vasrq-src"></span>').addClass(cls);
            $src.attr("title", msg("OnHandAtSource", "on hand") +
                (data.SourceWarehouseName ? " · " + data.SourceWarehouseName : ""));
            var $bar = $('<span class="MPC-vasrq-bar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $src.append($bar);
            $src.append(document.createTextNode(
                formatNumber(onHand, ln.UOMPrecision) + "/" + formatNumber(req, ln.UOMPrecision)));
            $c.append($src);
            return $c;
        }

        function itemsFooter() {
            var $f = $('<div class="MPC-vasrq-itfoot"></div>');
            // Budget set for the requisition (VAS_AvailableBudget, written by the
            // "Calculate Budget" process) sits where the subtotal used to, so the
            // estimate can be read straight against it. N/A when no budget has been
            // calculated for this requisition.
            $f.append(footBit(msg("Budget", "Budget"),
                (+data.AvailableBudget || 0) > 0 ? money(data.AvailableBudget) : msg("NA"), false));
            $f.append(footBit(msg("EstimatedTotal"), money(data.EstimatedValue), true));
            return $f;
        }

        function footBit(label, value, grand) {
            var $b = $('<span></span>').addClass(grand ? "MPC-vasrq-grand" : "MPC-vasrq-tf");
            $b.append(document.createTextNode(label));
            $b.append($('<b></b>').text(value));
            return $b;
        }

        // ---- Activity ---- //

        var ACT_BADGE = {
            create:  { cls: "create",  key: "ActCreated" },
            status:  { cls: "status",  key: "ActStatus"  },
            submit:  { cls: "submit",  key: "ActSubmit"  },
            link:    { cls: "link",    key: "ActLinked"  },
            comment: { cls: "comment", key: "ActComment" },
            // Downstream lifecycle documents.
            po:          { cls: "po",  key: "ActPO",          fallback: "PO"  },
            grn:         { cls: "grn", key: "ActGRN",         fallback: "GRN" },
            grncomplete: { cls: "grn", key: "ActGRNComplete", fallback: "GRN" }
        };

        function renderActivityPanel(activity) {
            var $panel = $('<div class="MPC-vasrq-lowersec"></div>');
            var $card = $('<div class="MPC-vasrq-panelcard"></div>');

            if (!activity.length) {
                $card.append($('<div class="MPC-vasrq-itempty"></div>').text(msg("NoActivity")));
            } else {
                for (var i = 0; i < activity.length; i++) $card.append(activityRow(activity[i]));
            }
            $panel.append($card);
            return $panel;
        }

        function activityRow(a) {
            var meta = ACT_BADGE[a.Type] || ACT_BADGE.comment;
            var $row = $('<div class="MPC-vasrq-actrow"></div>');

            $row.append($('<span class="MPC-vasrq-actbadge"></span>')
                .addClass(meta.cls).text(msg(meta.key, meta.fallback)));

            var $main = $('<div class="MPC-vasrq-actmain"></div>');
            var $wrap = $('<div class="MPC-vasrq-atwrap"></div>');
            $wrap.append($('<span class="MPC-vasrq-at"></span>').text(activityText(a)));
            $wrap.append($('<span class="MPC-vasrq-attime"></span>').text(formatDateTime(a.Created)));
            $main.append($wrap);
            $row.append($main);
            return $row;
        }

        function activityText(a) {
            if (a.Type === "create")
                return msg("RequisitionCreated") + (a.Text ? " " + msg("By") + " " + a.Text : "");
            if (a.Type === "status")
                return msg("RequisitionMarked") + " " + statusMeta().label + (a.Text ? " " + msg("By") + " " + a.Text : "");

            // Downstream documents name themselves, so the row reads
            // "PO Created — PO-000123 by <user>".
            if (a.Type === "po" || a.Type === "grn" || a.Type === "grncomplete") {
                var label;
                if (a.Type === "po") label = msg("POCreated", "PO Created");
                else if (a.Type === "grn") label = msg("GRNCreated", "GRN Created");
                else label = msg("GRNCompleted", "GRN Completed");

                if (a.DocumentNo) label += " — " + a.DocumentNo;
                if (a.Text) label += " " + msg("By") + " " + a.Text;
                return label;
            }

            return a.Text || msg("ActComment");
        }

        // ---- Notes ---- //

        function renderNotesPanel() {
            var $panel = $('<div class="MPC-vasrq-lowersec"></div>');
            var $card = $('<div class="MPC-vasrq-panelcard MPC-vasrq-notescard"></div>');

            var $notes = $('<div class="MPC-vasrq-notesbody"></div>');
            var text = data.Description;
            if (text) {
                var paras = String(text).split(/\r?\n+/);
                for (var i = 0; i < paras.length; i++) {
                    var t = paras[i].trim();
                    if (t) $notes.append($('<p></p>').text(t));
                }
            }
            if (!$notes.children().length) $notes.append($('<p class="MPC-vasrq-na"></p>').text(msg("NoNotes")));
            $card.append($notes);

            $panel.append($card);
            return $panel;
        }

        // ----------------------------------------------------------------- //
        //  Icons                                                             //
        // ----------------------------------------------------------------- //
        var SVG_ICONS = {
            user:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            arrow:     '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>',
            check:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            warn:      '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
            chevLeft:  '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>',
            chevrons:  '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 11 12 6 7 11"/><polyline points="17 18 12 13 7 18"/></svg>',
            transfer:  '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>',
            external:  '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 3h5v5"/><path d="M21 3 9 15"/><path d="M21 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h6"/></svg>',
            rfq:       '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>',
            list:      '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
            clock:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></svg>',
            note:      '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasrq-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting                                                        //
        // ----------------------------------------------------------------- //

        function currencySymbol() { return data && data.CurSymbol ? data.CurSymbol : "$"; }

        // Whole-currency display to match the reference design.
        function money(value) {
            var v = Math.round(+value || 0);
            return currencySymbol() + " " + v.toLocaleString(window.navigator.language);
        }

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function stripTime(d) {
            return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
        }

        // Parses a timestamp as it came off the server, as WALL-CLOCK time.
        //
        // Created / Updated are stored in server local time. Depending on how the
        // DateTime is tagged on the way out, the JSON can carry a "Z" or an
        // offset, and `new Date(...)` then converts it into the browser's timezone
        // — so the panel showed a creation time hours away from the one the
        // requisition window shows. Reading the date and time components straight
        // out of the string and building a local Date keeps the two in agreement
        // regardless of how the value was tagged.
        function parseServerDate(value) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;

            var m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/
                .exec(String(value));
            if (m) {
                return new Date(+m[1], +m[2] - 1, +m[3],
                                +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
            }

            // Anything else (e.g. an epoch value) falls back to native parsing.
            var d = new Date(value);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(value) {
            var d = parseServerDate(value);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language,
                    { year: "numeric", month: "short", day: "numeric" });
            } catch (e) { return d.toDateString(); }
        }

        function formatDateShort(value) {
            var d = parseServerDate(value);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
            } catch (e) { return ""; }
        }

        function formatDateTime(value) {
            var d = parseServerDate(value);
            if (!d) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_098_PurchaseRequisition.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_098_PurchaseRequisition.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_098_PurchaseRequisition.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_098_PurchaseRequisition.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
