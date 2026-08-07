/************************************************************
 * Module Name    : VAS
 * Purpose        : Delivery Order (DO) Overview tab panel. Renders a
 *                  review-oriented overview of the selected delivery order
 *                  (M_InOut, IsSOTrx = 'Y'): header identity + customer /
 *                  dispatch details card, a four-card KPI snapshot (delivery
 *                  value, lines, delivered qty, linked sales order), a compact
 *                  fulfilment lifecycle strip (Drafted -> Picked/Packed ->
 *                  In Transit -> Completed) driven by DocStatus + Processed,
 *                  the delivery line table and visual action buttons
 *                  (Print / Complete Delivery Order / Raise Invoice — the last
 *                  disabled until the DO is completed). Data is fetched from
 *                  VAS_100_OverviewDO/GetDOOverview. All on-screen strings are
 *                  resolved through VIS.Msg.getMsg("VAS_100_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasdo- -> vas_100- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_100-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_100-tone-" + tone).
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab is sitting on a row that has not been saved yet —
    // whether it came from New Record or from Copy Record.
    //
    // The authority is the GRID TABLE's insert flag: VIS.GridTable.dataNew()
    // raises it for both actions and clears it again on save, refresh or undo,
    // and GridTable.getIsInserting() reads it. GridTab does NOT expose that
    // method — it only holds the table as .gridTable — so asking the tab itself
    // always answers "no".
    //
    // The record id cannot answer this on its own: a copied row carries the
    // SOURCE record's field values, its key included, so the id handed to the
    // panel is the record that was copied FROM. Either way the panel would
    // otherwise show a saved record's details beside an unsaved new one.
    function isTabInserting(curTab) {
        if (!curTab) return false;
        try {
            if (curTab.gridTable && typeof curTab.gridTable.getIsInserting === "function"
                && curTab.gridTable.getIsInserting()) {
                return true;
            }
        } catch (e) { }

        var probes = ["getIsInserting", "isInserting", "getIsNew", "isNew"];
        for (var i = 0; i < probes.length; i++) {
            try {
                if (typeof curTab[probes[i]] === "function" && curTab[probes[i]]()) return true;
            } catch (e2) { }
        }
        return false;
    }

    VAS.VAS_100_OverviewDO = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;

        // The framework notifies a tab panel when the selected record changes
        // (refreshPanelData) but NOT when the user starts a new one:
        // GridController.dataNew() never reaches the tab panel, so the panel
        // would keep showing the previously selected record beside an empty new
        // one. Listening to the tab's own data-status events closes that gap.
        function onTabDataStatus(e) {
            var inserting = false;
            try {
                inserting = !!(e && typeof e.getIsInserting === "function" && e.getIsInserting());
            } catch (ex) {
                inserting = false;
            }
            // The event does not report insert state on every build, and a
            // COPIED row still carries the source record's key — so ask the tab
            // as well before trusting the id below.
            if (!inserting) inserting = isTabInserting($self.curTab);

            var rid = 0;
            try {
                if ($self.curTab && typeof $self.curTab.getRecord_ID === "function") {
                    rid = +$self.curTab.getRecord_ID() || 0;
                }
            } catch (ex2) {
                rid = 0;
            }

            if (inserting || rid <= 0) {
                // New (unsaved) record — nothing to show against it.
                if ($self.record_ID) {
                    $self.record_ID = 0;
                    $self.clear();
                }
                return;
            }
            if (rid !== $self.record_ID) {
                $self.record_ID = rid;
                $self.fetchData(rid);
            }
        }

        // Registered on the tab in startPanel, removed in dispose. Kept as an
        // object because the framework calls listener.dataStatusChanged(event).
        this.tabDataListener = { dataStatusChanged: function (e) { onTabDataStatus(e); } };
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;

        this.init = function () {
            $root = $('<div class="vas_100-root"></div>');
            $body = $('<div class="vas_100-body"></div>');
            $emptyState = $('<div class="vas_100-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_100_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_100_OverviewDO/GetDOOverview",
                type: "GET",
                dataType: "json",
                data: { M_InOut_ID: recordID },
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

            if (!data || !data.M_InOut_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Body is a flat stack of self-contained sections.
            renderHeader();
            renderSnapshot();
            renderLifecycle();
            renderLines();
            renderActions();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="vas_100-sec"></section>');
            var $head = $('<div class="vas_100-secHead"></div>');
            $head.append($('<h2 class="vas_100-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_100-secSummary"></span>').text(opts.summary));
            }
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Returns "N/A" for blank values so the layout never shows an empty cell.
        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_100_NA")
                : value;
        }

        // The currency token: prefer the linked order's symbol / ISO, else INR.
        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status / priority maps (codes -> label + tone) ---------- //

        // DocStatus code -> { key, tone }. tone drives the pill colour: Drafted
        // grey, Completed/Closed green, In Progress/Waiting amber, Voided/
        // Reversed/Invalid red.
        var STATUS_MAP = {
            "DR": { key: "VAS_100_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_100_InProgress",          tone: "warning" },
            "AP": { key: "VAS_100_Approved",            tone: "info" },
            "CO": { key: "VAS_100_Completed",           tone: "success" },
            "CL": { key: "VAS_100_Closed",              tone: "success" },
            "VO": { key: "VAS_100_Voided",              tone: "risk" },
            "RE": { key: "VAS_100_Reversed",            tone: "risk" },
            "WC": { key: "VAS_100_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_100_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_100_Invalid",             tone: "risk" },
            "NA": { key: "VAS_100_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // PriorityRule code -> { key, tone }.
        var PRIORITY_MAP = {
            "1": { key: "VAS_100_Urgent", tone: "risk" },
            "3": { key: "VAS_100_High",   tone: "warning" },
            "5": { key: "VAS_100_Medium", tone: "info" },
            "7": { key: "VAS_100_Low",    tone: "neutral" },
            "9": { key: "VAS_100_Minor",  tone: "neutral" }
        };

        function priorityMeta(code) {
            var m = PRIORITY_MAP[code];
            if (!m) return null;
            return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);
            var pm = priorityMeta(data.PriorityCode);

            // --- Title strip: title + subtitle (left), priority + status (right) ---
            var $strip = $('<section class="vas_100-hdr"></section>');
            var $top = $('<div class="vas_100-hdrTop"></div>');

            var $tl = $('<div class="vas_100-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_100-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_100_DeliveryOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_100_MovementDate") + " " + moved);
            if (data.OwnerName) subBits.push(VIS.Msg.getMsg("VAS_100_Owner") + " " + data.OwnerName);
            if (subBits.length) {
                $tl.append($('<div class="vas_100-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_100-hdrPills"></div>');
            if (pm) $pills.append(headerPill(pm.label, pm.tone, "chevUp", false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: customer identity (left) + dispatch fields (right) ---
            var $card = $('<section class="vas_100-hdrCard"></section>');

            // Left column: customer name + address + tracking / vehicle bits.
            var $left = $('<div class="vas_100-hdrColL"></div>');
            $left.append($('<div class="vas_100-fLabel"></div>').text(VIS.Msg.getMsg("VAS_100_Customer")));
            $left.append($('<div class="vas_100-vendName"></div>').text(na(data.CustomerName)));

            if (data.CustomerAddress) {
                var $addr = $('<div class="vas_100-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.CustomerAddress));
                $left.append($addr);
            }
            $left.append(headerField(VIS.Msg.getMsg("VAS_100_Packages"),
                (data.PackageCount ? data.PackageCount + "" : VIS.Msg.getMsg("VAS_100_NA")), false));
            $left.append(headerField(VIS.Msg.getMsg("VAS_100_TransportDoc"),
                na(data.TransportDoc), false));
            var $contact = $('<div class="vas_100-vendContact"></div>');
            appendContactBit($contact, "truck", data.VehicleNo);
            appendContactBit($contact, "tag",   data.TrackingNo);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right column: labelled dispatch / reference fields.
            var $right = $('<div class="vas_100-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_SalesOrder"),
                data.SONo || VIS.Msg.getMsg("VAS_100_NotLinked"), !!data.SONo));
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_Warehouse"), na(data.WarehouseName), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_MovementDate"),
                na(formatDate(data.MovementDate)), false));
           
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_100-hdrPill"></span>')
                .addClass("vas_100-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_100-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_100-hdrField"></div>');
            $f.append($('<div class="vas_100-fLabel"></div>').text(label));
            var $v = $('<div class="vas_100-fVal"></div>').text(value);
            if (link) $v.addClass("vas_100-is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_100-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_100-snap"></section>');
            var cur = currencyToken();

            // Delivery value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_100_DeliveryValue"),
                formatAmount(+data.DeliveryValue || 0, cur, data.StdPrecision),
                data.ISO_Code || ""));

            // Line count.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_100_Lines"),
                (data.LineCount || 0) + "",
                VIS.Msg.getMsg("VAS_100_LinesDelivered")));

            // Delivered quantity (units).
            $snap.append(metricCard("received", "truck", VIS.Msg.getMsg("VAS_100_DeliveredQty"),
                formatNumber(+data.DeliveredQty || 0, 0),
                VIS.Msg.getMsg("VAS_100_Units")));

            // Linked sales order.
            $snap.append(metricCard("order", "doc", VIS.Msg.getMsg("VAS_100_SalesOrder"),
                data.SONo || VIS.Msg.getMsg("VAS_100_NotLinked"),
                formatDate(data.SODateOrdered) || ""));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="vas_100-metric"></div>').addClass("vas_100-tone-" + tone);

            var $head = $('<div class="vas_100-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_100-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_100-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_100-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Fulfilment lifecycle (horizontal stepper) ---------- //

        // Four stages driven by DocStatus + Processed:
        //   Completed/Closed        -> all four done (active = Completed)
        //   Processed / IP / AP     -> Drafted + Picked done, In Transit active
        //   Waiting confirmation/pay-> Drafted done, Picked/Packed active
        //   otherwise (Drafted)     -> Drafted active
        // Voided / Reversed collapse to Drafted (no forward progress).
        function lifecycleReachedIndex() {
            var s = data.StatusCode;
            if (s === "CO" || s === "CL") return 3;
            if (s === "VO" || s === "RE" || s === "IN" || s === "NA") return 0;
            if (data.Processed || s === "IP" || s === "AP") return 2;
            if (s === "WC" || s === "WP") return 1;
            return 0;
        }

        function renderLifecycle() {
            var stageKeys = [
                "VAS_100_StageDrafted",
                "VAS_100_StagePickedPacked",
                "VAS_100_StageInTransit",
                "VAS_100_StageCompleted"
            ];
            var reached = lifecycleReachedIndex();
            var completed = data.StatusCode === "CO" || data.StatusCode === "CL";

            var $sec = section(VIS.Msg.getMsg("VAS_100_Lifecycle"), null);

            var $tl = $('<div class="vas_100-stepper"></div>');
            for (var i = 0; i < stageKeys.length; i++) {
                var stateCls, metaText, done;
                if (i < reached) {
                    stateCls = "vas_100-is-done"; done = true;
                    metaText = VIS.Msg.getMsg("VAS_100_Done");
                } else if (i === reached) {
                    // The final stage, once reached on a completed DO, reads done.
                    if (completed && i === stageKeys.length - 1) {
                        stateCls = "vas_100-is-done"; done = true;
                        metaText = formatDate(data.MovementDate) || VIS.Msg.getMsg("VAS_100_Done");
                    } else {
                        stateCls = "vas_100-is-active"; done = false;
                        metaText = VIS.Msg.getMsg("VAS_100_Current");
                    }
                } else {
                    stateCls = "is-pending"; done = false;
                    metaText = VIS.Msg.getMsg("VAS_100_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(stageKeys[i]), metaText, done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_100-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_100-stepRail"></div>');
            $rail.append($('<span class="vas_100-stepLine vas_100-stepLine-l"></span>'));
            var $dot = $('<span class="vas_100-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="vas_100-stepLine vas_100-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_100-stepLabel"></div>');
            $lbl.append($('<div class="vas_100-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_100-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Delivery lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var cur = currencyToken();

            var $sec = section(VIS.Msg.getMsg("VAS_100_LineItems"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_100_Items") + " · " +
                    formatNumber(+data.DeliveredQty || 0, 0) + " " + VIS.Msg.getMsg("VAS_100_Units")
            });

            var $tbl = $('<div class="vas_100-table"></div>');

            // Header row
            var $head = $('<div class="vas_100-tRow vas_100-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_100_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_100_UOM")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Ordered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Delivered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_LineValue")));
            $head.append($('<span class="vas_100-ta-c"></span>').text(VIS.Msg.getMsg("VAS_100_Status")));
            $tbl.append($head);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(lines[i], cur));
            }

            // Totals footer
            var $foot = $('<div class="vas_100-tFoot"></div>');
            var $bit = $('<span class="vas_100-tf vas_100-is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_100_TotalDeliveryValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.DeliveryValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);
        }

        function buildLineRow(ln, cur) {
            var $tr = $('<div class="vas_100-tRow vas_100-tBody"></div>');

            // Item (name + SKU / locator)
            var $item = $('<span class="vas_100-itItem"></span>');
            $item.append($('<div class="vas_100-itName"></div>').text(na(ln.ProductName)));
            var metaBits = [];
            if (ln.ProductCode) metaBits.push(VIS.Msg.getMsg("VAS_100_SKU") + " " + ln.ProductCode);
            if (ln.LocatorName) metaBits.push(VIS.Msg.getMsg("VAS_100_Locator") + " " + ln.LocatorName);
            if (metaBits.length) {
                $item.append($('<div class="vas_100-itSku"></div>').text(metaBits.join(" · ")));
            } else if (ln.Description) {
                $item.append($('<div class="vas_100-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Ordered
            $tr.append($('<span class="vas_100-ta-r"></span>').text(formatNumber(+ln.OrderedQty || 0, prec)));

            // Delivered (mini bar + delivered qty)
            var ordered = +ln.OrderedQty || 0;
            var delivered = +ln.DeliveredQty || 0;
            var pct = ordered > 0 ? Math.min(100, Math.round((delivered / ordered) * 100)) : (delivered > 0 ? 100 : 0);
            var recvState = ordered > 0 && delivered >= ordered ? "vas_100-full" : (delivered > 0 ? "vas_100-part" : "vas_100-none");
            var $recv = $('<span class="vas_100-recv vas_100-ta-r"></span>').addClass(recvState);
            var $bar = $('<span class="vas_100-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append(document.createTextNode(formatNumber(delivered, prec)));
            $tr.append($recv);

            // Line value
            $tr.append($('<span class="vas_100-ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag: Full / Partial / Short
            var $q = $('<span class="vas_100-ta-c"></span>');
            var tagCls, tagKey;
            if (ordered > 0 && delivered >= ordered) { tagCls = "vas_100-s-full"; tagKey = "VAS_100_Full"; }
            else if (delivered > 0)                  { tagCls = "vas_100-s-part"; tagKey = "VAS_100_Partial"; }
            else                                     { tagCls = "vas_100-s-short"; tagKey = "VAS_100_Short"; }
            $q.append($('<span class="vas_100-tag"></span>').addClass(tagCls)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only — the panel is a read-only overview. Document
        // actions run from the host window toolbar; these mirror them visually.
        // Raise Invoice is shown disabled until the DO is completed.
        function renderActions() {
            var completed = data.StatusCode === "CO" || data.StatusCode === "CL";

            var $bar = $('<section class="vas_100-actions"></section>');
            $bar.append(actionButton("printer", VIS.Msg.getMsg("VAS_100_Print"), "sec", false));
            $bar.append(actionButton("check",   VIS.Msg.getMsg("VAS_100_CompleteDO"), "pri", completed));
            $bar.append(actionButton("doc",     VIS.Msg.getMsg("VAS_100_RaiseInvoice"), "sec", !completed));
            $body.append($bar);
        }

        function actionButton(icon, label, kind, disabled) {
            var $b = $('<span class="vas_100-btn"></span>').addClass("vas_100-btn-" + (kind || "sec"));
            if (disabled) $b.addClass("vas_100-is-disabled");
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            truck:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 17h4V5H2v12h3"/><path d="M14 8h4l4 4v5h-3"/><circle cx="7.5" cy="17.5" r="1.5"/><circle cx="17.5" cy="17.5" r="1.5"/></svg>',
            tag:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12.59 2.59A2 2 0 0 0 11.17 2H4a2 2 0 0 0-2 2v7.17a2 2 0 0 0 .59 1.42l8.82 8.82a2 2 0 0 0 2.82 0l7.18-7.18a2 2 0 0 0 0-2.82Z"/><path d="M7 7h.01"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            printer:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_100-ic"></span>');
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

    VAS.VAS_100_OverviewDO.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
        // Watch the tab itself so New Record / Copy Record (neither of which
        // reliably calls refreshPanelData) still empty the panel.
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update tab panel based on selected record */
    VAS.VAS_100_OverviewDO.prototype.refreshPanelData = function (recordID, selectedRow) {
        // The insert check is what makes New Record / Copy Record behave:
        // the id handed in for an unsaved row can still be the previously
        // selected (or copied-from) record's, so the tab's own insert state
        // decides, not the id.
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_100_OverviewDO.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_100_OverviewDO.prototype.dispose = function () {
        if (this.curTab && typeof this.curTab.removeDataStatusListener === "function") {
            try { this.curTab.removeDataStatusListener(this.tabDataListener); } catch (e) { }
        }
        this.tabDataListener = null;
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
