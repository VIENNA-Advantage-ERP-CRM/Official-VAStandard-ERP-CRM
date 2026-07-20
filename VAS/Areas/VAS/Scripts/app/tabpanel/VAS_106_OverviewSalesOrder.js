/************************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order Overview tab panel. Renders a review-and-act
 *                  overview of the selected sales order (C_Order, IsSOTrx = 'Y'):
 *                  header identity, customer + addresses, created-from origin
 *                  documents, workflow action bar, KPI strip, a 6-stage order
 *                  progress stepper and collapsible sections — Order Lines
 *                  (with per-line contract flow), Delivery Readiness, Deliveries,
 *                  Invoices, Activity and Notes. Data comes from
 *                  VAS_106_OverviewSalesOrder/GetSalesOrderOverview.
 *
 *                  Two write actions are wired to the server (never mutate
 *                  documents from the browser): Complete Sales Order
 *                  (CompleteSalesOrder) and Create Contract from a service /
 *                  charge line (CreateContract). Create Delivery / Create
 *                  Invoice are rendered and enabled from live data but their
 *                  server generation is wired in a follow-up.
 * Chronological development:
 *   VAI163   2026-07-08  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_106_OverviewSalesOrder = function () {
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

        this.init = function () {
            $root = $('<div class="MPC-vaso-root"></div>');
            $body = $('<div class="MPC-vaso-body"></div>');
            $emptyState = $('<div class="MPC-vaso-empty" style="display:none;"></div>');
            $emptyState.text(getMsg("VAS_106_NoData", "No sales order selected"));
            $root.append($body).append($emptyState);
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

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        function getMsg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return fallback != null ? fallback : key;
        }

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_106_OverviewSalesOrder/GetSalesOrderOverview",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
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
            if (!data || !data.C_Order_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }
            $emptyState.hide();
            $body.show();

            renderHeader();
            renderCustomerCard();
            renderCreatedFrom();
            renderActionBar();
            renderKpis();
            renderStepper();
            renderOrderLines();
            renderDeliveryReadiness();
            renderDeliveries();
            renderInvoices();
            renderActivity();
            renderNotes();
        }

        // ----------------------------------------------------------------- //
        //  Derived-value helpers (single source of truth for the UI)         //
        // ----------------------------------------------------------------- //

        function stockLines() {
            var out = [], lines = data.Lines || [];
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].LineType === "product") out.push(lines[i]);
            }
            return out;
        }

        // Fulfilment: fully-delivered stock lines / total stock lines.
        function fulfilment() {
            var st = stockLines(), full = 0;
            for (var i = 0; i < st.length; i++) {
                if (st[i].QtyOrdered > 0 && st[i].QtyDelivered >= st[i].QtyOrdered) full++;
            }
            return { full: full, total: st.length, pct: st.length ? Math.round(full / st.length * 100) : 0 };
        }

        // Invoiced: Σ invoice grand totals vs order grand total + payment state.
        function invoiced() {
            var inv = data.Invoices || [], amt = 0, allPaid = inv.length > 0;
            for (var i = 0; i < inv.length; i++) {
                amt += (+inv[i].GrandTotal || 0);
                if (!inv[i].IsPaid) allPaid = false;
            }
            var gt = +data.GrandTotal || 0;
            return {
                amount: amt,
                pct: gt > 0 ? Math.round(amt / gt * 100) : 0,
                state: inv.length === 0 ? "—" : (allPaid ? getMsg("VAS_106_Paid", "paid") : getMsg("VAS_106_Unpaid", "unpaid")),
                fully: gt > 0 && amt >= gt
            };
        }

        // Delivery readiness: available (min(onhand,pending)) / pending units.
        function readiness() {
            var rows = data.DeliveryReadiness || [], pending = 0, avail = 0, shortCount = 0;
            for (var i = 0; i < rows.length; i++) {
                var p = +rows[i].PendingQty || 0, oh = +rows[i].QtyOnHand || 0;
                if (p <= 0) continue;
                pending += p;
                avail += Math.min(oh, p);
                if (oh < p) shortCount++;
            }
            return {
                pending: pending, available: avail, shortCount: shortCount,
                pct: pending > 0 ? Math.round(avail / pending * 100) : 100
            };
        }

        function isCompleted() { return data.DocStatus === "CO" || data.DocStatus === "CL"; }

        // ----------------------------------------------------------------- //
        //  Header                                                            //
        // ----------------------------------------------------------------- //

        // PriorityRule -> label + tone.
        function priorityMeta() {
            switch (data.PriorityRule) {
                case "1": return { label: getMsg("VAS_106_Urgent", "Urgent priority"), tone: "risk", icon: "chevUp" };
                case "3": return { label: getMsg("VAS_106_High", "High priority"), tone: "warning", icon: "chevUp" };
                case "5": return { label: getMsg("VAS_106_Medium", "Medium priority"), tone: "info", icon: null };
                case "7": return { label: getMsg("VAS_106_Low", "Low priority"), tone: "neutral", icon: null };
                default:  return { label: getMsg("VAS_106_Normal", "Normal priority"), tone: "neutral", icon: null };
            }
        }

        // SOCreditStatus -> label + tone.
        function creditMeta() {
            switch (data.CreditStatus) {
                case "O": return { label: getMsg("VAS_106_CreditOK", "Credit OK"), tone: "success" };
                case "H": return { label: getMsg("VAS_106_CreditHold", "Credit Hold"), tone: "risk" };
                case "S": return { label: getMsg("VAS_106_CreditStop", "Credit Stop"), tone: "risk" };
                case "W": return { label: getMsg("VAS_106_CreditWatch", "Credit Watch"), tone: "warning" };
                case "X": return { label: getMsg("VAS_106_NoCreditCheck", "No Credit Check"), tone: "neutral" };
                default:  return { label: getMsg("VAS_106_CreditUnknown", "Credit Unknown"), tone: "neutral" };
            }
        }

        // Live order-state pill (drives the header + action-bar message).
        function statusMeta() {
            var f = fulfilment();
            if (data.DocStatus === "VO") return { label: getMsg("VAS_106_Voided", "Voided"), tone: "risk" };
            if (data.DocStatus === "CL") return { label: getMsg("VAS_106_Closed", "Closed"), tone: "neutral" };
            if (data.DocStatus === "DR" || data.DocStatus === "IP")
                return { label: getMsg("VAS_106_Draft", "Draft"), tone: "neutral" };
            // Completed family — split by delivery progress.
            if (f.total > 0 && f.full >= f.total) return { label: getMsg("VAS_106_Completed", "Completed"), tone: "success" };
            if (f.full > 0) return { label: getMsg("VAS_106_PartiallyDelivered", "Partially Delivered"), tone: "warning" };
            return { label: getMsg("VAS_106_Confirmed", "Confirmed"), tone: "info" };
        }

        function renderHeader() {
            var $strip = $('<section class="MPC-vaso-hdr"></section>');
            var $top = $('<div class="MPC-vaso-hdrTop"></div>');

            var $tl = $('<div class="MPC-vaso-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vaso-hdrTitle"></div>').text(
                getMsg("VAS_106_SalesOrder", "Sales Order") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            if (data.POReference) subBits.push(getMsg("VAS_106_CustomerPO", "Customer PO") + " " + data.POReference);
            var od = formatDate(data.DateOrdered);
            if (od) subBits.push(getMsg("VAS_106_Ordered", "Ordered") + " " + od);
            if (subBits.length)
                $tl.append($('<div class="MPC-vaso-hdrSub"></div>').text(subBits.join(" · ")));
            $top.append($tl);

            var pm = priorityMeta(), cm = creditMeta(), sm = statusMeta();
            var $pills = $('<div class="MPC-vaso-hdrPills"></div>');
            $pills.append(headerPill(pm.label, pm.tone, pm.icon, false));
            $pills.append(headerPill(cm.label, cm.tone, null, true));
            $pills.append(headerPill(sm.label, sm.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vaso-hdrPill"></span>').addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vaso-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        // ----------------------------------------------------------------- //
        //  Customer identity card                                            //
        // ----------------------------------------------------------------- //

        // DeliveryRule -> shipping-rule label.
        function shippingRuleLabel() {
            switch (data.DeliveryRule) {
                case "A": return getMsg("VAS_106_ShipAvailability", "Availability");
                case "F": return getMsg("VAS_106_ShipForce", "Force");
                case "L": return getMsg("VAS_106_ShipCompleteLine", "Complete Line");
                case "M": return getMsg("VAS_106_ShipManual", "Manual");
                case "O": return getMsg("VAS_106_ShipCompleteOrder", "Complete Order");
                default:  return data.DeliveryRule || "—";
            }
        }

        function renderCustomerCard() {
            var $card = $('<section class="MPC-vaso-hdrCard"></section>');

            // Left: customer identity.
            var $left = $('<div class="MPC-vaso-hdrColL"></div>');
            $left.append($('<div class="MPC-vaso-fLabel"></div>').text(getMsg("VAS_106_Customer", "Customer")));
            $left.append($('<div class="MPC-vaso-custName"></div>').text(data.CustomerName || ""));

            var addrBits = [];
            if (data.BillToAddress) addrBits.push(getMsg("VAS_106_BillTo", "Bill to") + ": " + data.BillToAddress);
            if (data.ShipToAddress) addrBits.push(getMsg("VAS_106_ShipTo", "Ship to") + ": " + data.ShipToAddress);
            if (addrBits.length) {
                var $addr = $('<div class="MPC-vaso-custAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(addrBits.join(" · ")));
                $left.append($addr);
            }

            var $contact = $('<div class="MPC-vaso-custContact"></div>');
            appendContactBit($contact, "user", data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail", data.ContactEmail);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right: commercial terms.
            var $right = $('<div class="MPC-vaso-hdrColR"></div>');
            if (data.SalesRepName)    $right.append(headerField(getMsg("VAS_106_Salesperson", "Salesperson"), data.SalesRepName));
            if (data.PaymentTermName) $right.append(headerField(getMsg("VAS_106_PaymentTerms", "Payment Terms"), data.PaymentTermName));
            if (data.PriceListName)   $right.append(headerField(getMsg("VAS_106_Pricelist", "Pricelist"), data.PriceListName));
            if (data.WarehouseName)   $right.append(headerField(getMsg("VAS_106_FulfilmentWarehouse", "Fulfilment Warehouse"), data.WarehouseName));
            $right.append(headerField(getMsg("VAS_106_ShippingRule", "Shipping Rule"), shippingRuleLabel()));
            $card.append($right);

            $body.append($card);
        }

        function headerField(label, value) {
            var $f = $('<div class="MPC-vaso-hdrField"></div>');
            $f.append($('<div class="MPC-vaso-fLabel"></div>').text(label));
            $f.append($('<div class="MPC-vaso-fVal"></div>').text(value));
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vaso-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ----------------------------------------------------------------- //
        //  Created From (chip strip)                                         //
        // ----------------------------------------------------------------- //

        function renderCreatedFrom() {
            var $strip = $('<section class="MPC-vaso-genfrom"></section>');
            $strip.append($('<span class="MPC-vaso-gfLabel"></span>').text(getMsg("VAS_106_CreatedFrom", "Created From")));
            var $chips = $('<div class="MPC-vaso-gfChips"></div>');

            var any = false;
            if (data.QuotationNo) {
                $chips.append(originChip("doc", getMsg("VAS_106_Quotation", "Quotation"), data.QuotationNo,
                    pill(getMsg("VAS_106_Origin", "Origin"), "info"), "info", "C_Order", data.QuotationId));
                any = true;
            }
            if (data.OpportunityId) {
                $chips.append(originChip("target", getMsg("VAS_106_Opportunity", "Opportunity"),
                    data.OpportunityName || ("#" + data.OpportunityId), null, "success",
                    "VAS_Opportunity", data.OpportunityId));
                any = true;
            }
            if (data.ProjectId) {
                $chips.append(originChip("folder", getMsg("VAS_106_Project", "Project"),
                    data.ProjectName || ("#" + data.ProjectId), null, "muted",
                    "C_Project", data.ProjectId));
                any = true;
            }
            if (!any) {
                $chips.append(originChip("pencil", getMsg("VAS_106_Manual", "Manual"), null, null, "info", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId) {
            var $chip = $('<span class="MPC-vaso-chip"></span>').addClass("ic-" + (iconTone || "muted"));
            if (tableName && recordId) {
                $chip.addClass("is-link").attr("data-open-table", tableName).attr("data-open-id", recordId);
            }
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="MPC-vaso-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="MPC-vaso-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            return $chip;
        }

        // ----------------------------------------------------------------- //
        //  Workflow action bar                                               //
        // ----------------------------------------------------------------- //

        function renderActionBar() {
            var $bar = $('<section class="MPC-vaso-actionbar"></section>');

            var completed = isCompleted();
            var msg = completed
                ? getMsg("VAS_106_OrderCompletedMsg", "Order completed")
                : (data.DocStatus === "VO" ? getMsg("VAS_106_OrderVoidedMsg", "Order voided")
                   : getMsg("VAS_106_OrderInProgressMsg", "Order confirmed — fulfilment in progress"));
            var $note = $('<span class="MPC-vaso-cvNote"></span>');
            $note.append($('<span class="MPC-vaso-cvOk"></span>').append(svgIcon("check")));
            $note.append($('<span></span>').text(msg));
            $bar.append($note);

            var $actions = $('<div class="MPC-vaso-cvActions"></div>');

            // Complete Sales Order (wired).
            var $complete = $('<button class="MPC-vaso-btn primary" data-act="complete"></button>');
            $complete.append(svgIcon("checkCircle"));
            if (completed) {
                $complete.append($('<span></span>').text(getMsg("VAS_106_SalesOrderCompleted", "Sales Order Completed")));
                $complete.prop("disabled", true);
            } else if (data.DocStatus === "VO") {
                $complete.append($('<span></span>').text(getMsg("VAS_106_CompleteSalesOrder", "Complete Sales Order")));
                $complete.prop("disabled", true);
            } else {
                $complete.append($('<span></span>').text(getMsg("VAS_106_CompleteSalesOrder", "Complete Sales Order")));
            }
            $actions.append($complete);

            // Create Delivery (enabled from pending stock; generation wired later).
            var pendingToDeliver = readiness().pending > 0 || hasUndelivered();
            var $delivery = $('<button class="MPC-vaso-btn secondary" data-act="delivery"></button>');
            $delivery.append(svgIcon("truck"));
            $delivery.append($('<span></span>').text(getMsg("VAS_106_CreateDelivery", "Create Delivery")));
            $delivery.prop("disabled", !completed || !pendingToDeliver);
            $actions.append($delivery);

            // Create Invoice (enabled while not fully invoiced; generation wired later).
            var $invoice = $('<button class="MPC-vaso-btn secondary" data-act="invoice"></button>');
            $invoice.append(svgIcon("fileText"));
            $invoice.append($('<span></span>').text(getMsg("VAS_106_CreateInvoice", "Create Invoice")));
            $invoice.prop("disabled", !completed || invoiced().fully);
            $actions.append($invoice);

            $bar.append($actions);
            $body.append($bar);
        }

        function hasUndelivered() {
            var st = stockLines();
            for (var i = 0; i < st.length; i++) {
                if ((+st[i].QtyOrdered || 0) - (+st[i].QtyDelivered || 0) > 0) return true;
            }
            return false;
        }

        // ----------------------------------------------------------------- //
        //  KPI strip                                                         //
        // ----------------------------------------------------------------- //

        function renderKpis() {
            var $snap = $('<section class="MPC-vaso-snap"></section>');

            // Order Total.
            var totSub = (data.ISO_Code || "") + (data.ISO_Code ? " · " : "") + getMsg("VAS_106_InclTax", "incl. tax");
            $snap.append(metricCard("total", "coins", getMsg("VAS_106_OrderTotal", "Order Total"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                totSub, null));

            // Fulfilment.
            var f = fulfilment();
            $snap.append(metricCard("fulfil", "box", getMsg("VAS_106_Fulfilment", "Fulfilment"),
                f.pct + "%",
                f.full + " " + getMsg("VAS_106_Of", "of") + " " + f.total + " " + getMsg("VAS_106_StockLines", "stock lines delivered"),
                f.pct));

            // Invoiced.
            var iv = invoiced();
            $snap.append(metricCard("invoiced", "fileText", getMsg("VAS_106_Invoiced", "Invoiced"),
                formatAmount(iv.amount, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                iv.pct + "% " + getMsg("VAS_106_Billed", "billed") + " · " + iv.state, iv.pct));

            // Delivery Readiness.
            var rd = readiness();
            $snap.append(metricCard("readiness", "cube", getMsg("VAS_106_DeliveryReadiness", "Delivery Readiness"),
                rd.pct + "%",
                rd.available + " " + getMsg("VAS_106_Of", "of") + " " + rd.pending + " " + getMsg("VAS_106_PendingUnits", "pending units in stock"),
                rd.pct));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub, pct) {
            var $c = $('<div class="MPC-vaso-metric"></div>').addClass("tone-" + tone);
            var $head = $('<div class="MPC-vaso-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vaso-mLabel"></span>').text(label));
            $c.append($head);
            $c.append($('<div class="MPC-vaso-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vaso-mSub"></div>').text(sub));
            if (pct != null) {
                var $bar = $('<div class="MPC-vaso-mBar"><i></i></div>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $c.append($bar);
            }
            return $c;
        }

        // ----------------------------------------------------------------- //
        //  Order Progress stepper                                            //
        // ----------------------------------------------------------------- //

        function progressStages() {
            var f = fulfilment(), iv = invoiced(), inv = data.Invoices || [], dv = data.Deliveries || [];
            var confirmed = isCompleted();
            var shipped = dv.length > 0;
            var delivered = f.total > 0 ? (f.full >= f.total) : shipped;
            var invd = iv.amount > 0;
            var paid = inv.length > 0 && iv.state === getMsg("VAS_106_Paid", "paid");
            return [
                { key: "VAS_106_Drafted",   label: "Drafted",   done: true,      date: data.Created || data.DateOrdered },
                { key: "VAS_106_Confirmed", label: "Confirmed", done: confirmed, date: confirmed ? data.DateOrdered : null },
                { key: "VAS_106_Shipped",   label: "Shipped",   done: shipped,   date: null },
                { key: "VAS_106_Delivered", label: "Delivered", done: delivered, date: null,
                  meta: (f.total ? (f.full + "/" + f.total) : null) },
                { key: "VAS_106_Invoiced",  label: "Invoiced",  done: invd,      date: null,
                  meta: invd ? (iv.pct + "%") : null },
                { key: "VAS_106_Paid",      label: "Paid",      done: paid,      date: null }
            ];
        }

        function renderStepper() {
            var stages = progressStages();
            // Active = first not-done stage (else the last).
            var active = stages.length;
            for (var a = 0; a < stages.length; a++) { if (!stages[a].done) { active = a; break; } }

            var posted = data.Posted === "Y";
            var $sec = $('<section class="MPC-vaso-sec"></section>');
            var $head = $('<div class="MPC-vaso-secHead"></div>');
            var $title = $('<h2 class="MPC-vaso-secTitle"></h2>').text(getMsg("VAS_106_OrderProgress", "Order Progress"));
            $title.append(postBadge(posted));
            $head.append($title);
            $head.append($('<div class="MPC-vaso-secRight"></div>').append(
                $('<span class="MPC-vaso-secSummary"></span>').text(
                    getMsg("VAS_106_Stage", "Stage") + " " + Math.min(active + 1, stages.length) + " " +
                    getMsg("VAS_106_Of", "of") + " " + stages.length + " · " + statusMeta().label)));
            $sec.append($head);

            var $tl = $('<div class="MPC-vaso-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (i === active && !s.done) { stateCls = "is-active"; metaText = getMsg("VAS_106_InProgress", "In progress"); }
                else if (s.done) { stateCls = "is-done"; metaText = formatDate(s.date) || s.meta || getMsg("VAS_106_Done", "Done"); }
                else { stateCls = "is-pending"; metaText = s.meta || getMsg("VAS_106_Pending", "Pending"); }
                $tl.append(stepEntry(i + 1, getMsg(s.key, s.label), metaText, s.done, stateCls));
            }
            $sec.append($tl);
            $body.append($sec);
        }

        function postBadge(posted) {
            var $b = $('<span class="MPC-vaso-postBadge"></span>');
            if (posted) $b.addClass("posted").text(getMsg("VAS_106_Posted", "Posted"));
            else $b.text(getMsg("VAS_106_NotPosted", "Not Posted"));
            return $b;
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vaso-step"></div>').addClass(stateCls || "");
            var $rail = $('<div class="MPC-vaso-stepRail"></div>');
            $rail.append($('<span class="MPC-vaso-stepLine MPC-vaso-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vaso-stepDot"></span>');
            if (done) $dot.append(svgIcon("check")); else $dot.text(num);
            $rail.append($dot);
            $rail.append($('<span class="MPC-vaso-stepLine MPC-vaso-stepLine-r"></span>'));
            $entry.append($rail);
            var $lbl = $('<div class="MPC-vaso-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vaso-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vaso-stepMeta"></div>').text(meta));
            $entry.append($lbl);
            return $entry;
        }

        // ----------------------------------------------------------------- //
        //  Collapsible section shell                                         //
        // ----------------------------------------------------------------- //

        function collapsible(id, iconName, title, badgeText) {
            var $sec = $('<section class="MPC-vaso-dsec"></section>').attr("data-sec", id);
            var $head = $('<button type="button" class="MPC-vaso-dsecHead" aria-expanded="true"></button>');
            $head.append($('<span class="MPC-vaso-tIc"></span>').append(svgIcon(iconName)));
            $head.append($('<span class="MPC-vaso-dsecTitle"></span>').text(title));
            if (badgeText != null && badgeText !== "")
                $head.append($('<span class="MPC-vaso-badge"></span>').text(badgeText));
            $head.append($('<span class="MPC-vaso-dsecChev"></span>').append(svgIcon("chevUp")));
            var $bodyWrap = $('<div class="MPC-vaso-dsecBody"></div>');
            $sec.append($head).append($bodyWrap);
            $body.append($sec);
            return $bodyWrap;
        }

        function emptyRow(text) {
            return $('<div class="MPC-vaso-secEmpty"></div>').text(text);
        }

        // ----------------------------------------------------------------- //
        //  Order Lines                                                       //
        // ----------------------------------------------------------------- //

        function renderOrderLines() {
            var lines = data.Lines || [];
            var $wrap = collapsible("lines", "list", getMsg("VAS_106_OrderLines", "Order Lines"), lines.length);
            if (!lines.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoLines", "No order lines"))); return; }

            var $tbl = $('<div class="MPC-vaso-table MPC-vaso-linesTable"></div>');
            var $h = $('<div class="MPC-vaso-tRow MPC-vaso-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_ProductService", "Product / Service")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_106_Qty", "Qty")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_UnitPrice", "Unit price")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_106_Disc", "Disc")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_LineTotal", "Line total")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Delivered", "Delivered")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Contract", "Contract")));
            $tbl.append($h);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(lines[i]));
                var $form = buildContractForm(lines[i]);
                if ($form) $tbl.append($form);
            }

            // Totals footer.
            var $foot = $('<div class="MPC-vaso-tFoot"></div>');
            $foot.append(totalBit(getMsg("VAS_106_Subtotal", "Subtotal"),
                formatAmount(+data.TotalLines || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(totalBit(getMsg("VAS_106_Tax", "Tax"),
                formatAmount(+data.TaxAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(totalBit(getMsg("VAS_106_GrandTotal", "Grand Total"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            $wrap.append($tbl);

            // Contract hint (hidden once the order is completed).
            if (!isCompleted()) {
                var $hint = $('<div class="MPC-vaso-ccHint"></div>');
                $hint.append(svgIcon("info"));
                $hint.append($('<span></span>').text(getMsg("VAS_106_ContractHint",
                    "Contract applies to Service & Charge lines only — enable the toggle on the line; once the order is completed, fill the contract details below the line and click Create Contract")));
                $wrap.append($hint);
            }
        }

        function buildLineRow(ln) {
            var $tr = $('<div class="MPC-vaso-tRow MPC-vaso-tBody"></div>').attr("data-line", ln.C_OrderLine_ID);

            // Product / service identity.
            var $item = $('<span class="MPC-vaso-itItem"></span>');
            $item.append($('<div class="MPC-vaso-itName"></div>').text(ln.ProductName || ""));
            var sub;
            if (ln.LineType === "product") sub = getMsg("VAS_106_ProductTag", "PRODUCT") + (ln.ProductValue ? " · " + getMsg("VAS_106_SKU", "SKU") + " " + ln.ProductValue : "");
            else if (ln.LineType === "service") sub = getMsg("VAS_106_ServiceTag", "SERVICE") + (ln.ProductValue ? " · " + ln.ProductValue : "");
            else if (ln.LineType === "charge") sub = getMsg("VAS_106_ChargeTag", "CHARGE") + (ln.ChargeName ? " · " + ln.ChargeName : "");
            else sub = ln.ProductValue || "";
            if (sub) $item.append($('<div class="MPC-vaso-itSku"></div>').text(sub));
            $tr.append($item);

            var uomP = +ln.UOMPrecision || 0;
            $tr.append($('<span class="ta-c"></span>').text(formatNumber(+ln.QtyOrdered || 0, uomP)));
            $tr.append($('<span class="ta-r"></span>').text(formatAmount(+ln.PriceActual || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $tr.append($('<span class="ta-c"></span>').text((+ln.Discount ? formatNumber(+ln.Discount, 0) + "%" : "—")));
            $tr.append($('<span class="ta-r"></span>').text(formatAmount(+ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // Delivered — stockable lines only; service / charge show a dash.
            if (ln.LineType === "product") {
                var ordered = +ln.QtyOrdered || 0, delivered = +ln.QtyDelivered || 0;
                var pct = ordered > 0 ? Math.round(delivered / ordered * 100) : 0;
                var $prog = $('<span class="MPC-vaso-prog ta-r"></span>').addClass(ln.DeliveredState || "none");
                var $bar = $('<span class="MPC-vaso-progBar"><i></i></span>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $prog.append($bar);
                $prog.append(document.createTextNode(formatNumber(delivered, uomP) + "/" + formatNumber(ordered, uomP)));
                $tr.append($prog);
            } else {
                $tr.append($('<span class="ta-r MPC-vaso-naDash"></span>').text("—"));
            }

            // Contract — service / charge only.
            $tr.append(buildContractCell(ln));
            return $tr;
        }

        // Contract cell: created contract chip, or a toggle for service/charge,
        // or a muted dash for product lines.
        function buildContractCell(ln) {
            var $cell = $('<span class="ta-r"></span>');
            var isServiceCharge = (ln.LineType === "service" || ln.LineType === "charge");

            if (!isServiceCharge) {
                $cell.addClass("MPC-vaso-naDash").text("—");
                return $cell;
            }

            var $lc = $('<span class="MPC-vaso-lineContract"></span>');
            if (ln.C_Contract_ID > 0) {
                // Already contracted — locked toggle + link chip.
                $lc.append($('<span class="MPC-vaso-switch on locked"></span>'));
                var $link = $('<a class="MPC-vaso-ctrLink show" href="#"></a>')
                    .attr("data-open-table", "C_Contract").attr("data-open-id", ln.C_Contract_ID);
                $link.append(svgIcon("doc"));
                $link.append($('<span class="MPC-vaso-ctrNo"></span>').text(ln.ContractNo || ("#" + ln.C_Contract_ID)));
                $link.append(svgIcon("arrowUpRight"));
                $lc.append($link);
            } else {
                var $sw = $('<span class="MPC-vaso-switch" role="switch"></span>').attr("data-line", ln.C_OrderLine_ID);
                if (ln.IsContractFlag) $sw.addClass("on").attr("aria-checked", "true"); else $sw.attr("aria-checked", "false");
                $lc.append($sw);
                $lc.append($('<span class="MPC-vaso-swLabel"></span>').text(getMsg("VAS_106_Contract", "Contract")));
            }
            $cell.append($lc);
            return $cell;
        }

        // Inline contract form row — only for service/charge lines with no
        // contract yet. Opens when the order is completed and the toggle is on.
        function buildContractForm(ln) {
            var isServiceCharge = (ln.LineType === "service" || ln.LineType === "charge");
            if (!isServiceCharge || ln.C_Contract_ID > 0) return null;

            var open = isCompleted() && ln.IsContractFlag;
            var $form = $('<div class="MPC-vaso-contractForm"></div>').attr("data-line", ln.C_OrderLine_ID);
            if (open) $form.addClass("open");

            $form.append($('<div class="MPC-vaso-cfCap"></div>')
                .append(svgIcon("doc"))
                .append($('<span></span>').text(getMsg("VAS_106_ContractFor", "Contract") + " · " + (ln.ProductName || ""))));

            var $grid = $('<div class="MPC-vaso-formGrid"></div>');

            // Billing Frequency (required) — select from C_Frequency.
            var $freqWrap = fieldWrap(getMsg("VAS_106_BillingFrequency", "Billing Frequency"), true);
            var $sel = $('<select class="MPC-vaso-ffInput MPC-vaso-cfFreq" data-req></select>');
            $sel.append($('<option value="0"></option>').text(getMsg("VAS_106_SelectFrequency", "Select…")));
            var freqs = data.Frequencies || [];
            for (var i = 0; i < freqs.length; i++) {
                $sel.append($('<option></option>').attr("value", freqs[i].C_Frequency_ID).text(freqs[i].Name));
            }
            $freqWrap.find(".MPC-vaso-ffContent").append($sel);
            $grid.append($freqWrap);

            $grid.append(numField(getMsg("VAS_106_NoOfCycle", "No of Cycle"), "cfCycles", "1"));
            $grid.append(numField(getMsg("VAS_106_QtyPerCycle", "Quantity Per Cycle"), "cfQty",
                ln.QtyOrdered ? formatNumber(+ln.QtyOrdered, 0) : "1"));
            $grid.append(textField(getMsg("VAS_106_StartDate", "Start Date"), "cfStart", "DD-MM-YYYY", true));
            $grid.append(textField(getMsg("VAS_106_EndDate", "End Date"), "cfEnd", "DD-MM-YYYY", false));
            $form.append($grid);

            var $foot = $('<div class="MPC-vaso-cfFoot"></div>');
            $foot.append($('<span class="MPC-vaso-cfNote"></span>').text(getMsg("VAS_106_FillRequired", "Fill the required fields (*) to create the contract")));
            var $btn = $('<button type="button" class="MPC-vaso-btn primary sm MPC-vaso-cfCreate" data-line="' + ln.C_OrderLine_ID + '" disabled></button>');
            $btn.append(svgIcon("plus"));
            $btn.append($('<span></span>').text(getMsg("VAS_106_CreateContract", "Create Contract")));
            $foot.append($btn);
            $form.append($foot);

            if (open) setTimeout(function () { validateContractForm($form); }, 0);
            return $form;
        }

        function fieldWrap(label, required) {
            var $ff = $('<div class="MPC-vaso-ff"></div>');
            var $content = $('<div class="MPC-vaso-ffContent"></div>');
            var $lbl = $('<label></label>').text(label);
            if (required) { $lbl.addClass("req"); $lbl.append($('<span></span>').text("*")); }
            $content.append($lbl);
            $ff.append($content);
            return $ff;
        }

        function numField(label, cls, placeholder) {
            var $ff = fieldWrap(label, false);
            $ff.find(".MPC-vaso-ffContent").append(
                $('<input type="text" class="MPC-vaso-ffInput num ' + cls + '">').attr("placeholder", placeholder));
            return $ff;
        }

        function textField(label, cls, placeholder, required) {
            var $ff = fieldWrap(label, required);
            var $in = $('<input type="text" class="MPC-vaso-ffInput ' + cls + '">').attr("placeholder", placeholder);
            if (required) $in.attr("data-req", "");
            $ff.find(".MPC-vaso-ffContent").append($in);
            return $ff;
        }

        function totalBit(label, value, isGrand) {
            var $bit = $('<span class="MPC-vaso-tf"></span>');
            if (isGrand) $bit.addClass("is-grand");
            $bit.append(document.createTextNode(label));
            $bit.append($('<b></b>').text(value));
            return $bit;
        }

        // ----------------------------------------------------------------- //
        //  Delivery Readiness                                                //
        // ----------------------------------------------------------------- //

        function renderDeliveryReadiness() {
            var rows = data.DeliveryReadiness || [];
            var rd = readiness();
            var badge = rd.shortCount > 0 ? (rd.shortCount + " " + getMsg("VAS_106_Short", "short")) : rows.length;
            var $wrap = collapsible("readiness", "cube", getMsg("VAS_106_DeliveryReadiness", "Delivery Readiness"), badge);
            if (!rows.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoReadiness", "No stockable pending lines"))); return; }

            var $tbl = $('<div class="MPC-vaso-table MPC-vaso-rdTable"></div>');
            var $h = $('<div class="MPC-vaso-tRow MPC-vaso-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_Item", "Item")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Warehouse", "Warehouse")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_106_PendingToDeliver", "Pending to Deliver")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_106_OnHand", "On Hand")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Readiness", "Readiness")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) $tbl.append(buildReadinessRow(rows[i]));
            $wrap.append($tbl);

            var $note = $('<div class="MPC-vaso-note"></div>');
            $note.append(svgIcon("info"));
            $note.append($('<span></span>').text(getMsg("VAS_106_ReadinessNote",
                "On Hand = physical stock in the warehouse right now · Service lines excluded — readiness tracks stockable items against the next delivery")));
            $wrap.append($note);
        }

        function buildReadinessRow(rd) {
            var $tr = $('<div class="MPC-vaso-tRow MPC-vaso-tBody"></div>');
            var $item = $('<span class="MPC-vaso-itItem"></span>');
            $item.append($('<div class="MPC-vaso-itName"></div>').text(rd.ProductName || ""));
            if (rd.ProductValue) $item.append($('<div class="MPC-vaso-itSku"></div>').text(getMsg("VAS_106_SKU", "SKU") + " " + rd.ProductValue));
            $tr.append($item);
            $tr.append($('<span></span>').text(rd.WarehouseName || "—"));
            $tr.append($('<span class="ta-c"></span>').text(formatNumber(+rd.PendingQty || 0, 0)));
            $tr.append($('<span class="ta-c"></span>').text(formatNumber(+rd.QtyOnHand || 0, 0)));

            var tag, tone, dot = true;
            if (rd.Readiness === "ready") { tag = getMsg("VAS_106_FullyDelivered", "Fully delivered"); tone = "ready"; }
            else if (rd.Readiness === "instock") { tag = getMsg("VAS_106_Ready", "Ready to ship"); tone = "ready"; }
            else if (rd.Readiness === "short") { tag = getMsg("VAS_106_ShortBy", "Short by") + " " + formatNumber((+rd.PendingQty || 0) - (+rd.QtyOnHand || 0), 0); tone = "short"; }
            else { tag = getMsg("VAS_106_Awaited", "Awaited"); tone = "awaited"; }

            var $rt = $('<span class="ta-r"></span>');
            var $pill = $('<span class="MPC-vaso-rdTag"></span>').addClass(tone);
            if (dot) $pill.append($('<span class="MPC-vaso-rdDot"></span>'));
            $pill.append($('<span></span>').text(tag));
            $rt.append($pill);
            $tr.append($rt);
            return $tr;
        }

        // ----------------------------------------------------------------- //
        //  Deliveries                                                        //
        // ----------------------------------------------------------------- //

        function renderDeliveries() {
            var rows = data.Deliveries || [];
            var $wrap = collapsible("deliveries", "truck", getMsg("VAS_106_Deliveries", "Deliveries"), rows.length);
            if (!rows.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoDeliveries", "No deliveries yet"))); return; }

            var $tbl = $('<div class="MPC-vaso-table MPC-vaso-dvTable"></div>');
            var $h = $('<div class="MPC-vaso-tRow MPC-vaso-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_Delivery", "Delivery")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Warehouse", "Warehouse")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Tracking", "Tracking")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_106_Items", "Items")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Status", "Status")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                var dv = rows[i];
                var $tr = $('<div class="MPC-vaso-tRow MPC-vaso-tBody is-link"></div>')
                    .attr("data-open-table", "M_InOut").attr("data-open-id", dv.M_InOut_ID);
                var $item = $('<span class="MPC-vaso-itItem"></span>');
                $item.append($('<div class="MPC-vaso-itName"></div>').text(dv.DocumentNo || ""));
                var d = formatDate(dv.MovementDate);
                if (d) $item.append($('<div class="MPC-vaso-itSku"></div>').text(d));
                $tr.append($item);
                $tr.append($('<span></span>').text(dv.WarehouseName || "—"));
                $tr.append($('<span></span>').text(dv.TrackingNo || "—"));
                $tr.append($('<span class="ta-c"></span>').text(dv.LineCount || 0));
                $tr.append($('<span class="ta-r"></span>').append(docStatusPill(dv.DocStatus)));
                $tbl.append($tr);
            }
            $wrap.append($tbl);
        }

        // ----------------------------------------------------------------- //
        //  Invoices                                                          //
        // ----------------------------------------------------------------- //

        function renderInvoices() {
            var rows = data.Invoices || [];
            var $wrap = collapsible("invoices", "fileText", getMsg("VAS_106_Invoices", "Invoices"), rows.length);
            if (!rows.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoInvoices", "No invoices yet"))); return; }

            var $tbl = $('<div class="MPC-vaso-table MPC-vaso-invTable"></div>');
            var $h = $('<div class="MPC-vaso-tRow MPC-vaso-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_Invoice", "Invoice")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Issued", "Issued")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Amount", "Amount")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_106_Status", "Status")));
            $tbl.append($h);

            var iv = invoiced();
            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var $tr = $('<div class="MPC-vaso-tRow MPC-vaso-tBody is-link"></div>')
                    .attr("data-open-table", "C_Invoice").attr("data-open-id", r.C_Invoice_ID);
                $tr.append($('<span class="MPC-vaso-itName"></span>').text(r.DocumentNo || ""));
                $tr.append($('<span></span>').text(formatDate(r.DateInvoiced) || "—"));
                $tr.append($('<span class="ta-r"></span>').text(formatAmount(+r.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                var stTone = r.IsPaid ? "approved" : "sent";
                var stLabel = r.IsPaid ? getMsg("VAS_106_PaidTag", "Paid") : getMsg("VAS_106_UnpaidTag", "Unpaid");
                $tr.append($('<span class="ta-r"></span>').append(tagPill(stLabel, stTone)));
                $tbl.append($tr);
            }

            var $foot = $('<div class="MPC-vaso-tFoot"></div>');
            $foot.append(totalBit(getMsg("VAS_106_Invoiced", "Invoiced"),
                formatAmount(iv.amount, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(totalBit(getMsg("VAS_106_YetToInvoice", "Yet to invoice"),
                formatAmount(Math.max(0, (+data.GrandTotal || 0) - iv.amount), data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            $wrap.append($tbl);
        }

        // ----------------------------------------------------------------- //
        //  Activity                                                          //
        // ----------------------------------------------------------------- //

        var ACT_TYPES = {
            Note:     { tone: "info",    icon: "mail",  label: "Note" },
            Delivery: { tone: "success", icon: "truck", label: "Delivery" },
            Invoice:  { tone: "warning", icon: "fileText", label: "Invoice" },
            Created:  { tone: "neutral", icon: "plus",  label: "Created" },
            Updated:  { tone: "purple",  icon: "pencil", label: "Updated" }
        };

        function renderActivity() {
            var rows = data.Activity || [];
            var $wrap = collapsible("activity", "clock", getMsg("VAS_106_Activity", "Activity"), rows.length);
            if (!rows.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoActivity", "No activity yet"))); return; }

            var $card = $('<div class="MPC-vaso-panelcard"></div>');
            for (var i = 0; i < rows.length; i++) $card.append(activityRow(rows[i]));
            $wrap.append($card);
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.EventType] || ACT_TYPES.Updated;
            var $row = $('<div class="MPC-vaso-actRow"></div>');
            var $badge = $('<span class="MPC-vaso-actBadge"></span>').addClass("tone-" + meta.tone);
            $badge.append(svgIcon(meta.icon));
            $badge.append($('<span></span>').text(getMsg("VAS_106_Act" + a.EventType, meta.label)));
            $row.append($badge);

            var $main = $('<div class="MPC-vaso-actMain"></div>');
            var $wrapT = $('<div class="MPC-vaso-atWrap"></div>');
            $wrapT.append($('<span class="MPC-vaso-at"></span>').text(activityTitle(a, meta)));
            var when = formatDateTime(a.EventTime);
            if (a.ActorName) when += " · " + a.ActorName;
            $wrapT.append($('<span class="MPC-vaso-actTime"></span>').text(when));
            $main.append($wrapT);
            $row.append($main);
            return $row;
        }

        function activityTitle(a, meta) {
            if (a.EventType === "Note") return a.Title || getMsg("VAS_106_ActNote", "Note");
            if (a.EventType === "Invoice")
                return getMsg("VAS_106_ActInvoiceTxt", "Invoice") + " " + (a.Title || "") +
                       (a.Amount ? " (" + formatAmount(+a.Amount, data.CurSymbol, data.ISO_Code, data.StdPrecision) + ")" : "");
            if (a.EventType === "Delivery")
                return getMsg("VAS_106_ActDeliveryTxt", "Delivery") + " " + (a.Title || "");
            if (a.EventType === "Created")
                return getMsg("VAS_106_ActCreatedTxt", "Order created") + (a.Title ? " " + a.Title : "");
            if (a.EventType === "Updated")
                return getMsg("VAS_106_ActConfirmedTxt", "Order confirmed") + (a.Title ? " " + a.Title : "");
            return a.Title || "";
        }

        // ----------------------------------------------------------------- //
        //  Notes                                                             //
        // ----------------------------------------------------------------- //

        function renderNotes() {
            var rows = data.Notes || [];
            var $wrap = collapsible("notes", "note", getMsg("VAS_106_Notes", "Notes"), rows.length);
            if (!rows.length) { $wrap.append(emptyRow(getMsg("VAS_106_NoNotes", "No notes"))); return; }

            var $card = $('<div class="MPC-vaso-panelcard MPC-vaso-notesCard"></div>');
            var $notes = $('<div class="MPC-vaso-notesBody"></div>');
            for (var i = 0; i < rows.length; i++) $notes.append($('<p></p>').text(rows[i].Text));
            $card.append($notes);
            $wrap.append($card);
        }

        // ----------------------------------------------------------------- //
        //  Shared pills                                                      //
        // ----------------------------------------------------------------- //

        function pill(label, tone) {
            return $('<span class="MPC-vaso-pill"></span>').addClass("tone-" + (tone || "neutral")).text(label);
        }

        function tagPill(label, tone) {
            var $t = $('<span class="MPC-vaso-tag"></span>').addClass(tone);
            $t.append($('<span class="MPC-vaso-dot"></span>'));
            $t.append($('<span></span>').text(label));
            return $t;
        }

        function docStatusPill(docStatus) {
            var map = {
                "CO": { tone: "approved", label: getMsg("VAS_106_Completed", "Completed") },
                "CL": { tone: "approved", label: getMsg("VAS_106_Closed", "Closed") },
                "DR": { tone: "draft",    label: getMsg("VAS_106_Draft", "Draft") },
                "IP": { tone: "partial",  label: getMsg("VAS_106_InProgressShort", "In Progress") },
                "IN": { tone: "partial",  label: getMsg("VAS_106_InTransit", "Scheduled") }
            };
            var m = map[docStatus] || { tone: "draft", label: docStatus || "—" };
            return tagPill(m.label, m.tone);
        }

        // ----------------------------------------------------------------- //
        //  Events / actions                                                  //
        // ----------------------------------------------------------------- //

        function bindEvents() {
            // Section collapse.
            $root.on("click", ".MPC-vaso-dsecHead", function () {
                var $sec = $(this).closest(".MPC-vaso-dsec");
                var collapsed = $sec.toggleClass("collapsed").hasClass("collapsed");
                $(this).attr("aria-expanded", collapsed ? "false" : "true");
            });

            // Contract toggle (service / charge lines, before a contract exists).
            $root.on("click", ".MPC-vaso-switch:not(.locked)", function () {
                var lineId = $(this).attr("data-line");
                var on = $(this).toggleClass("on").hasClass("on");
                $(this).attr("aria-checked", on ? "true" : "false");
                var $form = $root.find('.MPC-vaso-contractForm[data-line="' + lineId + '"]');
                if (!$form.length) return;
                if (isCompleted() && on) { $form.addClass("open"); validateContractForm($form); }
                else if (!on) { $form.removeClass("open"); }
            });

            // Contract form validation.
            $root.on("input change", ".MPC-vaso-contractForm .MPC-vaso-ffInput", function () {
                validateContractForm($(this).closest(".MPC-vaso-contractForm"));
            });

            // Create Contract.
            $root.on("click", ".MPC-vaso-cfCreate:not([disabled])", function () {
                handleCreateContract($(this));
            });

            // Action-bar buttons.
            $root.on("click", '.MPC-vaso-btn[data-act="complete"]:not([disabled])', function () {
                handleCompleteSalesOrder();
            });
            $root.on("click", '.MPC-vaso-btn[data-act="delivery"]:not([disabled])', function () {
                toast(getMsg("VAS_106_DeliverySoon", "Create Delivery will be available shortly."), false);
            });
            $root.on("click", '.MPC-vaso-btn[data-act="invoice"]:not([disabled])', function () {
                toast(getMsg("VAS_106_InvoiceSoon", "Create Invoice will be available shortly."), false);
            });

            // Open linked records.
            $root.on("click", ".is-link[data-open-table], .MPC-vaso-chip.is-link, .MPC-vaso-ctrLink", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"));
            });
        }

        function validateContractForm($form) {
            var ok = true;
            $form.find(".MPC-vaso-ffInput[data-req]").each(function () {
                var v = ($(this).val() || "").toString().trim();
                if (!v || v === "0") ok = false;
            });
            var $btn = $form.find(".MPC-vaso-cfCreate");
            if (!$btn.hasClass("done")) $btn.prop("disabled", !ok);
            var $note = $form.find(".MPC-vaso-cfNote");
            if (!$btn.hasClass("done"))
                $note.text(ok ? getMsg("VAS_106_ReadyToCreate", "Ready — click Create Contract")
                              : getMsg("VAS_106_FillRequired", "Fill the required fields (*) to create the contract"));
        }

        function handleCompleteSalesOrder() {
            if (!window.confirm(getMsg("VAS_106_ConfirmComplete", "Complete this sales order? This cannot be undone.")))
                return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_106_OverviewSalesOrder/CompleteSalesOrder",
                type: "POST",
                dataType: "json",
                data: { C_Order_ID: data.C_Order_ID },
                success: function (raw) {
                    var res = parseResult(raw);
                    if (res && res.Success) {
                        toast(getMsg("VAS_106_OrderCompletedToast", "Sales order completed"), false);
                        $self.fetchData(data.C_Order_ID);
                    } else {
                        showBusy(false);
                        toast((res && res.Message) || getMsg("VAS_106_CompleteFailed", "Could not complete the order"), true);
                    }
                },
                error: function (err) { console.log(err); showBusy(false); toast(getMsg("VAS_106_CompleteFailed", "Could not complete the order"), true); }
            });
        }

        function handleCreateContract($btn) {
            var $form = $btn.closest(".MPC-vaso-contractForm");
            var lineId = $form.attr("data-line");
            var payload = {
                C_Order_ID: data.C_Order_ID,
                C_OrderLine_ID: lineId,
                C_Frequency_ID: parseInt($form.find(".MPC-vaso-cfFreq").val(), 10) || 0,
                noOfCycle: parseInt(($form.find(".MPC-vaso-cfCycles").val() || "0"), 10) || 0,
                qtyPerCycle: parseFloat(($form.find(".MPC-vaso-cfQty").val() || "0")) || 0,
                startDate: ($form.find(".MPC-vaso-cfStart").val() || "").trim(),
                endDate: ($form.find(".MPC-vaso-cfEnd").val() || "").trim()
            };
            $btn.prop("disabled", true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_106_OverviewSalesOrder/CreateContract",
                type: "POST",
                dataType: "json",
                data: payload,
                success: function (raw) {
                    var res = parseResult(raw);
                    if (res && res.Success) {
                        // Lock the form, then reload so the contract chip appears.
                        $form.find(".MPC-vaso-ffInput").prop("disabled", true);
                        $btn.addClass("done");
                        $btn.html("").append(svgIcon("check")).append($('<span></span>')
                            .text(getMsg("VAS_106_ContractCreated", "Contract Created")));
                        $form.find(".MPC-vaso-cfNote").text(
                            getMsg("VAS_106_ContractCreatedNote", "Contract") + " " + (res.DocumentNo || "") + " " +
                            getMsg("VAS_106_CreatedFromLine", "created from this line"));
                        toast(getMsg("VAS_106_ContractCreatedToast", "Contract created"), false);
                        setTimeout(function () { $self.fetchData(data.C_Order_ID); }, 900);
                    } else {
                        $btn.prop("disabled", false);
                        toast((res && res.Message) || getMsg("VAS_106_ContractFailed", "Could not create the contract"), true);
                    }
                },
                error: function (err) { console.log(err); $btn.prop("disabled", false); toast(getMsg("VAS_106_ContractFailed", "Could not create the contract"), true); }
            });
        }

        function parseResult(raw) {
            try { return (typeof raw === "string") ? jQuery.parseJSON(raw) : raw; }
            catch (e) { return null; }
        }

        // Open the record's window filtered to that row, using the platform's
        // zoom API (the same pattern as VAS_105_AccountRightPanel): resolve the
        // table's default zoom window, then start it with an equal-query on the
        // table's key column (TableName_ID). Degrades to a toast so a click
        // never throws.
        function openRecord(tableName, recordId) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = 0;
                if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, false) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_106_OpenRecord", "Open") + " " + tableName + " #" + recordId, false);
        }

        // Lightweight self-contained toast (no dependency on a host toast API).
        function toast(message, isError) {
            var $t = $('<div class="MPC-vaso-toast"></div>').addClass(isError ? "err" : "ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("show"); }, 10);
            setTimeout(function () { $t.removeClass("show"); setTimeout(function () { $t.remove(); }, 300); }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icons                                                             //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            cube:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>',
            fileText: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M12 18v-6M9 15h6"/></svg>',
            truck:    '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="1" y="3" width="15" height="13"/><path d="M16 8h4l3 3v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>',
            list:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
            clock:    '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></svg>',
            note:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>',
            doc:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/></svg>',
            target:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M9 12l2 2 4-4"/></svg>',
            folder:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4h5l2 3h9a1 1 0 0 1 1 1v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z"/></svg>',
            pencil:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            info:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>',
            check:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            checkCircle: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            plus:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vaso-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting helpers                                                //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(+value || 0);
            var cur = symbol || iso || "";
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        function formatDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try { return d.toLocaleDateString(window.navigator.language, { year: "numeric", month: "short", day: "2-digit" }); }
            catch (e) { return d.toDateString(); }
        }

        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "2-digit" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_106_OverviewSalesOrder.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_106_OverviewSalesOrder.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_106_OverviewSalesOrder.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_106_OverviewSalesOrder.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
