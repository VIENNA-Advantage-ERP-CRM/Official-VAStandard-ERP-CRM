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
        var activeTab = "items";

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
        var PRIORITY_MAP = {
            "1": { key: "UrgentPriority", tone: "high" },
            "3": { key: "HighPriority",   tone: "high" },
            "5": { key: "MediumPriority", tone: "med"  },
            "7": { key: "LowPriority",    tone: "low"  },
            "9": { key: "MinorPriority",  tone: "low"  }
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
            render();
        };

        function render() {
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
            renderTabs();
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                           //
        // ----------------------------------------------------------------- //

        // Localised label lookup. All on-screen text is seeded in AD_Message as
        // VAS_098_<key>; a missing key falls back to the key text via VIS.Msg.
        function msg(key) { return VIS.Msg.getMsg("VAS_098_" + key); }

        function statusMeta() {
            // A converted requisition surfaces as "Converted" regardless of the
            // underlying DocStatus.
            if (data.IsConverted) return { label: msg("Converted"), tone: "sent" };
            var m = STATUS_MAP[data.StatusCode];
            if (m) return { label: msg(m.key), tone: m.tone };
            return { label: data.StatusCode || msg("NA"), tone: "draft" };
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

        // VAI163 2026-07-02  Reworked to the VAS_092 header pattern: a soft-
        // gradient title strip (title + subtitle, with the priority / type /
        // status pills on the right) whose tint is driven by requisition
        // progress (headerTone()).
        function renderHead() {
            var st = statusMeta();
            var pm = priorityMeta();

            var $head = $('<div class="MPC-vasrq-hdr"></div>').addClass("tone-" + headerTone());
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
            $top.append($pills);

            $head.append($top);
            $body.append($head);
        }

        // Header gradient tint by requisition progress; collapses the document
        // status into the five banner tints defined in the stylesheet.
        function headerTone() {
            if (data.IsConverted) return "sent";
            switch (data.StatusCode) {
                case "CO": case "AP":            return "done";
                case "CL":                       return "sent";
                case "IP": case "WC": case "WP": return "progress";
                case "VO": case "RE":
                case "IN": case "NA":            return "cancelled";
                default:                         return "draft";
            }
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
        function renderDetails() {
            var $card = $('<div class="MPC-vasrq-hdrCard"></div>');

            // Left: Source Warehouse (the source of goods) + route + people.
            var $l = $('<div class="MPC-vasrq-hdrColL"></div>');
            $l.append($('<div class="MPC-vasrq-fLabel"></div>').text(msg("SourceWarehouse")));
            $l.append($('<div class="MPC-vasrq-srcName"></div>').text(
                data.SourceWarehouseName || msg("ExternalProcurement")));
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.replace(/\s/g, "")) $l.append(headerField(msg("Currency"), cur));
            if (data.RequestWarehouseName) {
                var $route = $('<div class="MPC-vasrq-srcRoute"></div>');
                $route.append(svgIcon("arrow"));
                $route.append($('<span></span>').text(msg("RequestWHRoute") + ": " + data.RequestWarehouseName));
                $l.append($route);
            }

            var $people = $('<div class="MPC-vasrq-srcPeople"></div>');
            appendPersonBit($people, data.RequesterName, msg("Requester"));
            appendPersonBit($people, data.PreparerName, msg("Preparer"));
            if ($people.children().length) $l.append($people);
            $card.append($l);

            // Right: labelled meta fields.
            var $r = $('<div class="MPC-vasrq-hdrColR"></div>');
            $r.append(headerField(msg("ProcurementType"), procurementType()));
            $r.append(headerField(msg("PriceList"), data.PriceListName || msg("NA")));
            $r.append(headerField(msg("RequestWarehouse"), data.RequestWarehouseName || msg("NA")));
           
           
            $card.append($r);

            $body.append($card);
        }

        // Person chip (requester / preparer) with a leading user icon.
        function appendPersonBit($container, value, title) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasrq-personBit"></span>').attr("title", title);
            $bit.append(svgIcon("user"));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
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
                noteText = msg("AlreadyConverted");
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

            // Document actions run by the platform's process engine. "Create RFQ"
            // (VAI163 2026-07-02) sits alongside the two conversion actions.
            var $actions = $('<div class="MPC-vasrq-cvactions"></div>');
            $actions.append(convertBtn(msg("ConvertToMaterialTransfer"), "transfer", "primary", canConvert));
            $actions.append(convertBtn(msg("CreateRFQ"), "rfq", "secondary", canConvert));
            $actions.append(convertBtn(msg("ConvertToPurchaseOrder"), "external", "secondary", canConvert));
            $strip.append($actions);

            $body.append($strip);
        }

        function convertBtn(label, icon, variant, enabled) {
            var $b = $('<button type="button" class="MPC-vasrq-btn"></button>').addClass(variant);
            $b.append(svgIcon(icon));
            $b.append(document.createTextNode(label));
            if (!enabled) {
                $b.prop("disabled", true);
            } else {
                $b.on("click", function () {
                    // Conversion is a document action executed by the platform's
                    // process engine; surface a hint rather than firing here.
                    if (VIS && VIS.ADialog && VIS.ADialog.info) {
                        VIS.ADialog.info(0, null, msg("RunFromToolbar"));
                    } else {
                        alert(msg("RunFromToolbar"));
                    }
                });
            }
            return $b;
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

            // Source availability
            var $s4 = statCard("a-amber", msg("SourceAvailability"));
            if (data.HasSourceData) {
                $s4.append($('<div class="MPC-vasrq-sval"></div>').text(
                    (data.FullyInStockLines || 0) + " / " + (data.LineCount || 0)));
                $s4.append(statSub(msg("LinesFullyInStock"), ""));
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
            var req = new Date(data.DateRequired);
            var sys = data.SystemDate ? new Date(data.SystemDate) : new Date();
            if (isNaN(req.getTime())) return "";
            var days = Math.round((stripTime(req) - stripTime(sys)) / 86400000);
            if (days > 0) return days + " " + msg("DaysRemaining");
            if (days < 0) return Math.abs(days) + " " + msg("DaysOverdue");
            return msg("DueToday");
        }

        // -------------------------- Progress ----------------------------- //

        function progressStages() {
            var s = data.StatusCode;
            var submitted  = data.Processed || s === "IP" || s === "AP" || s === "CO" || s === "CL";
            var completed  = s === "CO" || s === "CL" || data.IsConverted;
            var converted  = data.IsConverted;
            var fulfilment = data.HasOrdered;
            var closed     = s === "CL";

            return [
                { key: "c1", label: msg("Drafted"),      done: true,       sub: formatDateShort(data.DateDoc || data.Created) },
                { key: "c2", label: msg("Submitted"),    done: submitted,  sub: "" },
                { key: "c3", label: msg("Completed"),    done: completed,  sub: "" },
                { key: "c4", label: msg("Converted"),    done: converted,  sub: "" },
                { key: "c5", label: msg("InFulfilment"), done: fulfilment, sub: "" },
                { key: "c6", label: msg("Closed"),       done: closed,     sub: formatDateShort(closed ? data.Updated : null) }
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

        // ---------------------------- Tabs ------------------------------- //

        function renderTabs() {
            var lines = (data.Lines) || [];
            var activity = (data.Activity) || [];
            var noteCount = data.Description ? 1 : 0;

            var $bar = $('<div class="MPC-vasrq-tabbar"></div>');
            $bar.append(tabButton("items", "list", msg("Items"), lines.length));
            $bar.append(tabButton("activity", "clock", msg("Activity"), activity.length));
            $bar.append(tabButton("notes", "note", msg("Notes"), noteCount));
            $body.append($bar);

            var $tb = $('<div class="MPC-vasrq-tabbody"></div>');
            $tb.append(renderItemsPanel(lines));
            $tb.append(renderActivityPanel(activity));
            $tb.append(renderNotesPanel());
            $body.append($tb);

            showTab(activeTab);
        }

        function tabButton(key, icon, label, count) {
            var $t = $('<button type="button" class="MPC-vasrq-tab"></button>').attr("data-tab", key);
            $t.append($('<span class="MPC-vasrq-tic"></span>').append(svgIcon(icon)));
            $t.append(document.createTextNode(label + " "));
            $t.append($('<span class="MPC-vasrq-badge"></span>').text(count));
            $t.on("click", function () { activeTab = key; showTab(key); });
            return $t;
        }

        function showTab(key) {
            $body.find(".MPC-vasrq-tab").each(function () {
                $(this).toggleClass("active", $(this).attr("data-tab") === key);
            });
            $body.find(".MPC-vasrq-tabpanel").each(function () {
                $(this).toggleClass("show", $(this).attr("data-tab") === key);
            });
        }

        // ---- Items ---- //

        function renderItemsPanel(lines) {
            var $panel = $('<div class="MPC-vasrq-tabpanel"></div>').attr("data-tab", "items");
            var $items = $('<div class="MPC-vasrq-items"></div>');

            var $head = $('<div class="MPC-vasrq-itrow MPC-vasrq-ithead"></div>');
            $head.append($('<span></span>').text(msg("Item")));
            $head.append($('<span></span>').text(msg("Category")));
            $head.append($('<span class="ta-c"></span>').text(msg("Qty")));
            $head.append($('<span class="ta-r"></span>').text(msg("SourceStock")));
            $head.append($('<span class="ta-r"></span>').text(msg("UnitCost")));
            $head.append($('<span class="ta-r"></span>').text(msg("EstTotal")));
            $items.append($head);

            if (!lines.length) {
                $items.append($('<div class="MPC-vasrq-itempty"></div>').text(msg("NoLineItems")));
            } else {
                for (var i = 0; i < lines.length; i++) $items.append(itemRow(lines[i]));
                $items.append(itemsFooter());
            }

            $panel.append($items);
            return $panel;
        }

        function itemRow(ln) {
            var $r = $('<div class="MPC-vasrq-itrow MPC-vasrq-itbody"></div>');

            var $item = $('<span></span>');
            $item.append($('<div class="MPC-vasrq-itname"></div>').text(ln.ProductName || msg("NA")));
            if (ln.ProductValue) {
                $item.append($('<div class="MPC-vasrq-itsku"></div>').text(msg("SKU") + " " + ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasrq-itsku"></div>').text(ln.Description));
            }
            $r.append($item);

            var $cat = $('<span></span>');
            if (ln.CategoryName) $cat.append($('<span class="MPC-vasrq-cat"></span>').text(ln.CategoryName));
            else $cat.append($('<span class="MPC-vasrq-na"></span>').text(msg("NA")));
            $r.append($cat);

            $r.append($('<span class="ta-c"></span>').text(formatNumber(ln.RequestedQty, ln.UOMPrecision)));

            $r.append(sourceCell(ln));

            $r.append($('<span class="ta-r"></span>').text(money(ln.UnitPrice)));
            $r.append($('<span class="ta-r"></span>').text(money(ln.LineAmount)));
            return $r;
        }

        function sourceCell(ln) {
            var $c = $('<span class="ta-r"></span>');
            if (!ln.HasSourceData) {
                $c.append($('<span class="MPC-vasrq-na"></span>').text(msg("NA")));
                return $c;
            }
            var req = +ln.RequestedQty || 0;
            var res = +ln.ReservedQty || 0;
            var pct = req > 0 ? Math.round((res / req) * 100) : 0;
            var cls = (req > 0 && res >= req) ? "full" : "short";
            var $src = $('<span class="MPC-vasrq-src"></span>').addClass(cls);
            var $bar = $('<span class="MPC-vasrq-bar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $src.append($bar);
            $src.append(document.createTextNode(
                formatNumber(res, ln.UOMPrecision) + "/" + formatNumber(req, ln.UOMPrecision)));
            $c.append($src);
            return $c;
        }

        function itemsFooter() {
            var $f = $('<div class="MPC-vasrq-itfoot"></div>');
            $f.append(footBit(msg("EstimatedSubtotal"), money(data.EstimatedSubtotal), false));
            $f.append(footBit(msg("Contingency"), money(data.Contingency), false));
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
            comment: { cls: "comment", key: "ActComment" }
        };

        function renderActivityPanel(activity) {
            var $panel = $('<div class="MPC-vasrq-tabpanel"></div>').attr("data-tab", "activity");
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

            $row.append($('<span class="MPC-vasrq-actbadge"></span>').addClass(meta.cls).text(msg(meta.key)));

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
            return a.Text || msg("ActComment");
        }

        // ---- Notes ---- //

        function renderNotesPanel() {
            var $panel = $('<div class="MPC-vasrq-tabpanel"></div>').attr("data-tab", "notes");
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

        function formatDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language,
                    { year: "numeric", month: "short", day: "numeric" });
            } catch (e) { return d.toDateString(); }
        }

        function formatDateShort(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
            } catch (e) { return ""; }
        }

        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
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
