/************************************************************
 * Module Name    : VAS
 * Purpose        : Material Transfer Overview tab panel. Renders a review-
 *                  oriented overview of the selected stock movement
 *                  (M_Movement): header identity + transfer type / warehouse
 *                  details card, a From -> To route strip (source warehouse /
 *                  locator to destination warehouse / locator with a movement-
 *                  stage route tag), a "Generated from" origin-chip row
 *                  (Requisition / Production Order, muted when not linked), a
 *                  four-card KPI snapshot (lines, transfer quantity, transfer
 *                  value, confirmations), a five-node lifecycle stepper
 *                  (Drafted -> Confirmed -> Completed -> Received -> Posted) and
 *                  a transfer-lines table with per-line from/to locator,
 *                  quantity, value and source badge, plus visual action buttons
 *                  (Print / Receive Transfer — the latter disabled once posted).
 *                  Data is fetched from
 *                  VAS_103_MaterialTransfer/GetMaterialTransferOverview. All
 *                  on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_103_...").
 * Chronological development:
 *   VAI163   2026-07-07  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_103_MaterialTransfer = function () {
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
            $root = $('<div class="MPC-vasmt-root"></div>');
            $body = $('<div class="MPC-vasmt-body"></div>');
            $emptyState = $('<div class="MPC-vasmt-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_103_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_103_MaterialTransfer/GetMaterialTransferOverview",
                type: "GET",
                dataType: "json",
                data: { M_Movement_ID: recordID },
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

            if (!data || !data.M_Movement_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHeader();
            renderRoute();
            renderGeneratedFrom();
            renderSnapshot();
            renderTimeline();
            renderLines();
            renderActions();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasmt-sec"></section>');
            var $head = $('<div class="MPC-vasmt-secHead"></div>');
            $head.append($('<h2 class="MPC-vasmt-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasmt-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_103_NA")
                : value;
        }

        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_103_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_103_InProgress",          tone: "info" },
            "AP": { key: "VAS_103_Approved",            tone: "info" },
            "CO": { key: "VAS_103_Completed",           tone: "success" },
            "CL": { key: "VAS_103_Closed",              tone: "success" },
            "VO": { key: "VAS_103_Voided",              tone: "risk" },
            "RE": { key: "VAS_103_Reversed",            tone: "risk" },
            "WC": { key: "VAS_103_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_103_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_103_Invalid",             tone: "risk" },
            "NA": { key: "VAS_103_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        function transferTypeLabel() {
            return (data.TransferTypeCode === "INTRA")
                ? VIS.Msg.getMsg("VAS_103_IntraWarehouse")
                : VIS.Msg.getMsg("VAS_103_InterWarehouse");
        }

        // Movement-stage booleans derived from status / processing / confirms.
        function stageFlags() {
            var code = data.StatusCode;
            var completed = data.Processed || code === "CO" || code === "CL";
            return {
                drafted:   true,
                confirmed: completed || code !== "DR",
                completed: completed,
                received:  (data.ConfirmationCount || 0) > 0,
                posted:    !!data.Posted
            };
        }

        // Route tag reflecting the current movement stage.
        function routeStageLabel() {
            var f = stageFlags();
            if (data.TransferTypeCode === "INTRA") return VIS.Msg.getMsg("VAS_103_IntraWH");
            if (f.posted)   return VIS.Msg.getMsg("VAS_103_Posted");
            if (f.received) return VIS.Msg.getMsg("VAS_103_Received");
            if (f.completed) return VIS.Msg.getMsg("VAS_103_Dispatched");
            if (f.confirmed) return VIS.Msg.getMsg("VAS_103_Picked");
            return VIS.Msg.getMsg("VAS_103_Drafted");
        }

        // Per-line origin: Requisition / Production Order / Manual.
        function lineOrigin(ln) {
            if (ln.RequisitionLineID > 0) return "req";
            if (ln.WorkOrderID > 0)       return "prod";
            return "manual";
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="MPC-vasmt-hdr"></section>');
            var $top = $('<div class="MPC-vasmt-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasmt-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasmt-hdrEyebrow"></div>').text(VIS.Msg.getMsg("VAS_103_MaterialTransfer")));
            $tl.append($('<div class="MPC-vasmt-hdrTitle"></div>').text(na(data.DocumentNo)));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_103_MovementDate") + " " + moved);
            if (data.RequestedBy) subBits.push(VIS.Msg.getMsg("VAS_103_RequestedBy") + " " + data.RequestedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasmt-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasmt-hdrPills"></div>');
            $pills.append(headerPill(transferTypeLabel(), "info", "transfer", false));
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_103_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: transfer identity (left) + document fields (right) ---
            var $card = $('<section class="MPC-vasmt-hdrCard"></section>');

            var $left = $('<div class="MPC-vasmt-hdrColL"></div>');
            $left.append($('<div class="MPC-vasmt-fLabel"></div>').text(VIS.Msg.getMsg("VAS_103_TransferType")));
            $left.append($('<div class="MPC-vasmt-vendName"></div>').text(transferTypeLabel()));

            var $contact = $('<div class="MPC-vasmt-vendContact"></div>');
            appendContactBit($contact, "user", data.RequestedBy);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="MPC-vasmt-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_103_TransferNo"), na(data.DocumentNo), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_103_Reference"), na(data.Description), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_103_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_103_Posted")
                            : VIS.Msg.getMsg("VAS_103_NotPosted"), false));
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasmt-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasmt-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasmt-hdrField"></div>');
            $f.append($('<div class="MPC-vasmt-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasmt-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasmt-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Route strip (From -> To) ---------- //

        function renderRoute() {
            var $sec = section(VIS.Msg.getMsg("VAS_103_Route"), null);

            var $route = $('<div class="MPC-vasmt-route"></div>');
            $route.append(routeNode(VIS.Msg.getMsg("VAS_103_From"), na(data.FromWarehouseName)));

            var $arrow = $('<div class="MPC-vasmt-routeArrow"></div>');
            $arrow.append($('<span class="MPC-vasmt-routeTag"></span>').text(routeStageLabel()));
            $arrow.append(svgIcon("arrow"));
            $route.append($arrow);

            $route.append(routeNode(VIS.Msg.getMsg("VAS_103_To"), na(data.ToWarehouseName)));
            $sec.append($route);
        }

        function routeNode(label, warehouse) {
            var $n = $('<div class="MPC-vasmt-routeNode"></div>');
            $n.append($('<div class="MPC-vasmt-routeLbl"></div>').text(label));
            $n.append($('<div class="MPC-vasmt-routeWh"></div>').text(warehouse));
            return $n;
        }

        // ---------- Generated from (origin chips) ---------- //

        function renderGeneratedFrom() {
            var $sec = section(VIS.Msg.getMsg("VAS_103_GeneratedFrom"), null);
            var $row = $('<div class="MPC-vasmt-origins"></div>');
            $row.append(originChip(VIS.Msg.getMsg("VAS_103_Requisition"), data.HasRequisition));
            $row.append(originChip(VIS.Msg.getMsg("VAS_103_ProductionOrder"), data.HasWorkOrder));
            $sec.append($row);
        }

        function originChip(label, linked) {
            var $c = $('<span class="MPC-vasmt-originChip"></span>')
                .toggleClass("is-linked", !!linked)
                .toggleClass("is-muted", !linked);
            $c.append(svgIcon("link"));
            $c.append($('<span class="MPC-vasmt-originLbl"></span>').text(label));
            $c.append($('<span class="MPC-vasmt-originState"></span>').text(
                linked ? VIS.Msg.getMsg("VAS_103_Origin") : VIS.Msg.getMsg("VAS_103_NotLinked")));
            return $c;
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasmt-snap"></section>');
            var cur = currencyToken();

            // Lines.
            $snap.append(metricCard("lines", "layers", VIS.Msg.getMsg("VAS_103_Lines"),
                (data.LineCount || 0) + "", VIS.Msg.getMsg("VAS_103_OnThisTransfer")));

            // Transfer quantity.
            $snap.append(metricCard("qty", "box", VIS.Msg.getMsg("VAS_103_TransferQty"),
                formatNumber(+data.TransferQty || 0, 0), VIS.Msg.getMsg("VAS_103_UnitsMoved")));

            // Transfer value.
            $snap.append(metricCard("value", "coins", VIS.Msg.getMsg("VAS_103_TransferValue"),
                formatAmount(+data.TransferValue || 0, cur, data.StdPrecision), ""));

            // Confirmations.
            $snap.append(metricCard("confirm", "check", VIS.Msg.getMsg("VAS_103_Confirmations"),
                (data.ConfirmationCount || 0) + "", VIS.Msg.getMsg("VAS_103_ReceiptRecords")));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasmt-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasmt-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasmt-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasmt-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasmt-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Lifecycle stepper (5 nodes) ---------- //

        function renderTimeline() {
            var f = stageFlags();
            var stages = [
                { key: "VAS_103_Drafted",   done: f.drafted,   date: data.MovementDate },
                { key: "VAS_103_Confirmed", done: f.confirmed, date: data.MovementDate },
                { key: "VAS_103_Completed", done: f.completed, date: data.MovementDate },
                { key: "VAS_103_Received",  done: f.received,  date: data.MovementDate },
                { key: "VAS_103_Posted",    done: f.posted,    date: data.MovementDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_103_Lifecycle"), null);

            var $tl = $('<div class="MPC-vasmt-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "is-done";
                    metaText = (i === 4)
                        ? VIS.Msg.getMsg("VAS_103_Posted")
                        : (formatDate(s.date) || VIS.Msg.getMsg("VAS_103_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "is-active";
                    metaText = VIS.Msg.getMsg("VAS_103_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_103_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasmt-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasmt-stepRail"></div>');
            $rail.append($('<span class="MPC-vasmt-stepLine MPC-vasmt-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasmt-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasmt-stepLine MPC-vasmt-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasmt-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasmt-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasmt-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Transfer lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var $sec = section(VIS.Msg.getMsg("VAS_103_TransferLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_103_Items")
            });

            $sec.append(buildLinesTable());
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];
            var cur = currencyToken();

            var $tbl = $('<div class="MPC-vasmt-table"></div>');

            var $head = $('<div class="MPC-vasmt-tRow MPC-vasmt-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_FromLocator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_ToLocator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_103_Quantity")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_103_Value")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_103_Source")));
            $tbl.append($head);

            var breakdown = { req: 0, prod: 0, manual: 0 };
            var totQty = 0, totVal = 0;

            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var origin = lineOrigin(ln);
                breakdown[origin]++;
                totQty += (+ln.MovementQty || 0);
                totVal += (+ln.LineValue || 0);
                $tbl.append(buildLineRow(ln, origin, cur));
            }

            // Totals footer with source breakdown.
            var $foot = $('<div class="MPC-vasmt-tFoot"></div>');

            var breakBits = [];
            if (breakdown.req)    breakBits.push(VIS.Msg.getMsg("VAS_103_Requisition") + " (" + breakdown.req + ")");
            if (breakdown.prod)   breakBits.push(VIS.Msg.getMsg("VAS_103_ProductionOrder") + " (" + breakdown.prod + ")");
            if (breakdown.manual) breakBits.push(VIS.Msg.getMsg("VAS_103_Manual") + " (" + breakdown.manual + ")");
            if (breakBits.length) {
                $foot.append($('<span class="MPC-vasmt-tf"></span>').text(
                    VIS.Msg.getMsg("VAS_103_From") + " " + breakBits.join(", ")));
            }

            var $qty = $('<span class="MPC-vasmt-tf"></span>');
            $qty.append(document.createTextNode(VIS.Msg.getMsg("VAS_103_TotalQty")));
            $qty.append($('<b></b>').text(formatNumber(totQty, 0)));
            $foot.append($qty);

            var $val = $('<span class="MPC-vasmt-tf is-grand"></span>');
            $val.append(document.createTextNode(VIS.Msg.getMsg("VAS_103_TotalValue")));
            $val.append($('<b></b>').text(formatAmount(totVal, cur, data.StdPrecision)));
            $foot.append($val);

            $tbl.append($foot);

            return $tbl;
        }

        function buildLineRow(ln, origin, cur) {
            var $tr = $('<div class="MPC-vasmt-tRow MPC-vasmt-tBody"></div>');

            // Item (name + SKU)
            var $item = $('<span class="MPC-vasmt-itItem"></span>');
            $item.append($('<div class="MPC-vasmt-itName"></div>').text(na(ln.ProductName)));
            if (ln.ProductCode) {
                $item.append($('<div class="MPC-vasmt-itSku"></div>')
                    .text(VIS.Msg.getMsg("VAS_103_SKU") + " " + ln.ProductCode));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasmt-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // From locator
            $tr.append($('<span></span>').text(na(ln.FromLocatorName)));

            // To locator
            $tr.append($('<span></span>').text(na(ln.ToLocatorName)));

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Quantity
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.MovementQty || 0, prec)));

            // Value
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Source badge
            var srcKey = origin === "req" ? "VAS_103_Requisition"
                       : (origin === "prod" ? "VAS_103_ProductionOrder" : "VAS_103_Manual");
            var $q = $('<span class="ta-c"></span>');
            $q.append($('<span class="MPC-vasmt-tag"></span>').addClass("s-" + origin)
                .text(VIS.Msg.getMsg(srcKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only. Receive Transfer is disabled once the movement is
        // already posted / received.
        function renderActions() {
            var f = stageFlags();
            var received = f.posted || f.received;

            var $bar = $('<section class="MPC-vasmt-actions"></section>');
            $bar.append(actionButton("print", VIS.Msg.getMsg("VAS_103_Print"), "sec", false));
            $bar.append(actionButton("download", VIS.Msg.getMsg("VAS_103_ReceiveTransfer"), "pri", received));
            $body.append($bar);
        }

        function actionButton(icon, label, kind, disabled) {
            var $b = $('<span class="MPC-vasmt-btn"></span>').addClass("btn-" + (kind || "sec"));
            if (disabled) $b.addClass("is-disabled");
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            calendar: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            layers:   '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            transfer: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M8 3 4 7l4 4"/><path d="M4 7h16"/><path d="m16 21 4-4-4-4"/><path d="M20 17H4"/></svg>',
            arrow:    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m13 5 7 7-7 7"/></svg>',
            link:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            print:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>',
            download: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="M7 10l5 5 5-5"/><path d="M12 15V3"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasmt-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting helpers                                                //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        function formatAmount(value, cur, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        function formatDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
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

    VAS.VAS_103_MaterialTransfer.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_103_MaterialTransfer.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_103_MaterialTransfer.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_103_MaterialTransfer.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
