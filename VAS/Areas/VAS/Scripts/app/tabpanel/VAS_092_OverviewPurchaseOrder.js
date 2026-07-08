/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Order Overview tab panel. Renders a
 *                  review-oriented overview of the selected purchase
 *                  order (C_Order, IsSoTrx = 'N'): identity, linked
 *                  origin docs, stat strip, 7-stage progress, line
 *                  items with received progress, landed cost, and a
 *                  terms / recent-activity area. Data is fetched from
 *                  VAS_OverviewPurchaseOrder/GetPurchaseOrderOverview.
 * Chronological development:
 *   VAI163   2026-06-10  Created
 *   VAI163   2026-06-15  Added inline icons (address, contact, linked
 *                        chips, activity badge) and a Production Order
 *                        linked chip to match the reference design.
 *   VAI163   2026-06-17  Terms & Notes description value now read from
 *                        C_Order.POReference instead of C_Order.Description.
 *   VAI163   2026-06-17  Reworked the landed-cost section into a per-component
 *                        view: component name + source, distribution-method
 *                        tag, expected, actual (invoiced / awaiting), variance,
 *                        a totals footer (expected / actual-to-date / open /
 *                        landed value) and a methodology note with invoiced
 *                        progress.
 *   VAI163   2026-06-22  Redesigned to the canonical windows-and-panels.md
 *                        Right Panel Body language: em-anchored body, flat
 *                        operational surfaces, and the named content
 *                        primitives — Hero Status Card (with embedded Metric
 *                        Grid), Section Headers (summary / action variants),
 *                        Compact List, Timeline (order progress + activity),
 *                        and Entity List (line items, landed cost) with
 *                        section summary rows. Glassmorphism surfaces removed.
 *   VAI163   2026-07-01  Reworked the header to the reference design: a soft-
 *                        gradient title strip (title + subtitle with priority +
 *                        delivery-status pills) above a white two-column details
 *                        card (vendor identity | payment / currency / ship-to /
 *                        bill-to). Replaces the Hero Status Card (grand-total
 *                        headline + metric grid) and the separate Vendor section.
 *   VAI163   2026-07-01  Added a snapshot section above Order Progress: the
 *                        Generated From row is now a horizontal chip strip
 *                        (Sales Order / Requisition / Production Order chips)
 *                        and is followed by a four-card metric grid — Order
 *                        Total, Expected Delivery, Line Items and Received
 *                        (with a receipt progress bar).
 *   VAI163   2026-07-01  Bottom reworked into a two-column row: Terms & Notes
 *                        (multi-paragraph) beside a typed Recent Activity feed
 *                        (note / grn / invoice / payment / approval / created),
 *                        each row a tag chip + title + timestamp.
 *   VAI163   2026-07-08  Generated From now shows a single "Manual" chip when
 *                        the PO has no origin document (no Sales Order,
 *                        Requisition or Production Order link) instead of three
 *                        "Not linked" chips.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_092_OverviewPurchaseOrder = function () {
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
            $root = $('<div class="MPC-vaspo-root"></div>');
            $body = $('<div class="MPC-vaspo-body"></div>');
            $emptyState = $('<div class="MPC-vaspo-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_092_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
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

            if (!data || !data.C_Order_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Body is a flat stack of self-contained sections. Each section is a
            // Section Header + one content primitive (Compact List / Metric Grid /
            // Timeline / Entity List), per windows-and-panels.md.
            renderHeader();
            renderLinked();
            renderSnapshot();
            renderProgress();
            renderLines();
            renderLandedCost();
            renderBottom();
        }

        // ----------------------------------------------------------------- //
        //  Section / primitive builders                                      //
        // ----------------------------------------------------------------- //

        // A headered section: Section Header (title + optional summary/action)
        // followed by a content node. Returns the section element so callers can
        // append additional bodies.
        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vaspo-sec"></section>');
            var $head = $('<div class="MPC-vaspo-secHead"></div>');
            $head.append($('<h2 class="MPC-vaspo-secTitle"></h2>').text(title));

            var $right = $('<div class="MPC-vaspo-secRight"></div>');
            if (opts.summary) {
                $right.append($('<span class="MPC-vaspo-secSummary"></span>').text(opts.summary));
            }
            if (opts.action) {
                $right.append($('<a class="MPC-vaspo-secAction"></a>').text(opts.action));
            }
            if (opts.summary || opts.action) $head.append($right);

            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Status pill (tinted). tone: info | success | warning | risk | neutral | purple
        function pill(label, tone) {
            return $('<span class="MPC-vaspo-pill"></span>')
                .addClass("tone-" + (tone || "neutral"))
                .text(label);
        }

        // ---------- Header (title strip + vendor / terms card) ---------- //

        // Maps the order's delivery / progress state to a semantic tone + label.
        function statusTone(d) {
            if (d.IsPaymentDone)
                return { tone: "success", label: VIS.Msg.getMsg("VAS_092_PaymentDone") };
            if (d.IsFullyDelivered)
                return { tone: "success", label: VIS.Msg.getMsg("VAS_092_Completed") };
            if (d.IsPartialDelivered)
                return { tone: "warning", label: VIS.Msg.getMsg("VAS_092_PartialDelivered") };
            if (d.IsWithVendor)
                return { tone: "info", label: VIS.Msg.getMsg("VAS_092_WithVendor") };
            return { tone: "neutral", label: VIS.Msg.getMsg("VAS_092_Drafted") };
        }

        // Priority pill descriptor: tone + optional leading chevron icon.
        function priorityMeta() {
            var prio = (data.Priority || "low").toLowerCase();
            if (prio === "high") return { tone: "warning", icon: "chevUp", label: VIS.Msg.getMsg("VAS_092_HighPriority") };
            if (prio === "med")  return { tone: "warning", icon: "chevUp", label: VIS.Msg.getMsg("VAS_092_MediumPriority") };
            return { tone: "neutral", icon: null, label: VIS.Msg.getMsg("VAS_092_LowPriority") };
        }

        // VAI163 2026-07-01  Header reworked to the reference design: a soft-gradient
        // title strip (title + subtitle, with priority + delivery-status pills on the
        // right) above a white two-column details card (vendor identity on the left,
        // payment / currency / ship-to / bill-to fields on the right). Replaces the
        // former Hero Status Card (grand-total headline + metric grid) and the
        // separate Vendor section.
        function renderHeader() {
            var st = statusTone(data);
            var pm = priorityMeta();

            // --- Title strip: title + subtitle (left), priority + status pills (right) ---
            var $strip = $('<section class="MPC-vaspo-hdr"></section>');
            var $top = $('<div class="MPC-vaspo-hdrTop"></div>');

            var $tl = $('<div class="MPC-vaspo-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vaspo-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_092_PurchaseOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var created = formatDate(data.Created || data.DateOrdered);
            if (created) subBits.push(VIS.Msg.getMsg("VAS_092_Created") + " " + created);
            if (data.BuyerName) subBits.push(VIS.Msg.getMsg("VAS_092_Buyer") + " " + data.BuyerName);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vaspo-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            // Pills: priority (chevron) then delivery status (leading dot).
            var $pills = $('<div class="MPC-vaspo-hdrPills"></div>');
            $pills.append(headerPill(pm.label, pm.tone, pm.icon, false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: vendor identity (left) + terms fields (right) ---
            if (!data.VendorName && !data.VendorAddress &&
                !data.ContactName && !data.ContactPhone && !data.ContactEmail &&
                !data.PaymentTermName && !data.WarehouseName && !data.OrgName &&
                !data.ISO_Code) {
                return;
            }

            var $card = $('<section class="MPC-vaspo-hdrCard"></section>');

            // Left column: vendor name + address + contact bits.
            var $left = $('<div class="MPC-vaspo-hdrColL"></div>');
            $left.append($('<div class="MPC-vaspo-fLabel"></div>').text(VIS.Msg.getMsg("VAS_092_Vendor")));
            $left.append($('<div class="MPC-vaspo-vendName"></div>').text(data.VendorName || ""));

            if (data.VendorAddress) {
                var $addr = $('<div class="MPC-vaspo-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.VendorAddress));
                $left.append($addr);
            }

            var $contact = $('<div class="MPC-vaspo-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $left.append($contact);
            if (data.OrgName) $left.append(headerField(VIS.Msg.getMsg("VAS_092_BillTo"), data.OrgName));
            $card.append($left);

            // Right column: labelled term fields.
            var $right = $('<div class="MPC-vaspo-hdrColR"></div>');
            if (data.PaymentTermName) {
                $right.append(headerField(VIS.Msg.getMsg("VAS_092_PaymentTerms"), data.PaymentTermName));
            }
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.trim()) $right.append(headerField(VIS.Msg.getMsg("VAS_092_Currency"), cur));
            if (data.WarehouseName) $right.append(headerField(VIS.Msg.getMsg("VAS_092_ShipTo"), data.WarehouseName));            
            if ($right.children().length) $card.append($right);

            $body.append($card);
        }

        // Header pill: tinted chip with an optional leading chevron icon (priority)
        // or a leading dot (delivery status).
        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vaspo-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vaspo-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        // Labelled field block for the details card's right column.
        function headerField(label, value) {
            var $f = $('<div class="MPC-vaspo-hdrField"></div>');
            $f.append($('<div class="MPC-vaspo-fLabel"></div>').text(label));
            $f.append($('<div class="MPC-vaspo-fVal"></div>').text(value));
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vaspo-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Generated From (chip strip) ---------- //

        // VAI163 2026-07-01  Reworked from a vertical Compact List into a
        // horizontal chip strip (one chip per origin document) to match the
        // reference design, sitting directly above the metric snapshot.
        function renderLinked() {
            var $strip = $('<section class="MPC-vaspo-genfrom"></section>');
            $strip.append($('<span class="MPC-vaspo-gfLabel"></span>')
                .text(VIS.Msg.getMsg("VAS_092_GeneratedFrom")));

            var $chips = $('<div class="MPC-vaspo-gfChips"></div>');

            // VAI163 2026-07-08  When the PO is not generated from any origin
            // document (no Sales Order, no Requisition and no Production Order),
            // it was created manually — show a single "Manual" chip instead of
            // three "Not linked" chips. Production Order is never carried in the
            // overview payload, so it is always treated as not linked here.
            var hasSalesOrder = !!data.RefOrderDocNo;
            var hasRequisition = (data.RequisitionLineCount > 0);
            if (!hasSalesOrder && !hasRequisition) {
                $chips.append(originChip("pencil", VIS.Msg.getMsg("VAS_092_Manual"),
                    null, null, "info"));
                $strip.append($chips);
                $body.append($strip);
                return;
            }

            // Sales Order (origin) — from Ref_Order_ID.
            if (data.RefOrderDocNo) {
                $chips.append(originChip("doc", VIS.Msg.getMsg("VAS_092_SalesOrder"),
                    data.RefOrderDocNo, pill(VIS.Msg.getMsg("VAS_092_Origin"), "info"), "info"));
            } else {
                $chips.append(originChip("doc", VIS.Msg.getMsg("VAS_092_SalesOrder"),
                    VIS.Msg.getMsg("VAS_092_NotLinked"), null, "muted"));
            }

            // Requisition — present when any requisition line points at this PO.
            if (data.RequisitionLineCount > 0) {
                $chips.append(originChip("clipboardCheck", VIS.Msg.getMsg("VAS_092_Requisition"),
                    VIS.Msg.getMsg("VAS_092_Linked"), null, "success"));
            } else {
                $chips.append(originChip("clipboardCheck", VIS.Msg.getMsg("VAS_092_Requisition"),
                    VIS.Msg.getMsg("VAS_092_NotLinked"), null, "muted"));
            }

            // Production Order — overview payload carries no production-order link.
            $chips.append(originChip("factory", VIS.Msg.getMsg("VAS_092_ProductionOrder"),
                VIS.Msg.getMsg("VAS_092_NotLinked"), null, "muted"));

            $strip.append($chips);
            $body.append($strip);
        }

        // Origin chip: leading icon (tinted by iconTone) + grey label + dark
        // value, with an optional trailing status pill.
        function originChip(icon, label, value, $statusPill, iconTone) {
            var $chip = $('<span class="MPC-vaspo-chip"></span>').addClass("ic-" + (iconTone || "muted"));
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="MPC-vaspo-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="MPC-vaspo-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            return $chip;
        }

        // ---------- Snapshot (metric grid) ---------- //

        // Four-card metric strip: Order Total, Expected Delivery, Line Items and
        // Received (with a receipt progress bar). Sits directly above Order
        // Progress per the reference design.
        function renderSnapshot() {
            var $snap = $('<section class="MPC-vaspo-snap"></section>');

            // Order Total — grand total (tax/freight inclusive).
            var totalSub = (data.ISO_Code || "");
            var incl = VIS.Msg.getMsg("VAS_092_InclTaxFreight");
            totalSub = totalSub ? totalSub + " · " + incl : incl;
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_092_OrderTotal"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                totalSub, null));

            // Expected Delivery — promised date + delivery-status caption.
            var st = statusTone(data);
            $snap.append(metricCard("delivery", "calendar", VIS.Msg.getMsg("VAS_092_ExpectedDelivery"),
                formatDate(data.DatePromised) || "—", st.label, null));

            // Line Items — line count + total units ordered.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_092_LineItems"),
                (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_092_Lines"),
                formatNumber(+data.TotalQtyOrdered || 0, 0) + " " + VIS.Msg.getMsg("VAS_092_UnitsOrdered"),
                null));

            // Received — delivered/ordered + percent and fully-received line count.
            var ordered = +data.TotalQtyOrdered || 0;
            var delivered = +data.TotalQtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((delivered / ordered) * 100) : 0;
            var recvSub = pct + "% · " + (data.FullyReceivedLineCount || 0) + " " +
                VIS.Msg.getMsg("VAS_092_Of") + " " + (data.LineCount || 0) + " " +
                VIS.Msg.getMsg("VAS_092_Lines");
            $snap.append(metricCard("received", "inbox", VIS.Msg.getMsg("VAS_092_Received"),
                formatNumber(delivered, 0) + " / " + formatNumber(ordered, 0), recvSub, pct));

            $body.append($snap);
        }

        // Metric card: colour-accented left border (via tone class), a header
        // (icon + label), a large value, a caption and an optional receipt
        // progress bar (pct 0..100, or null for no bar).
        function metricCard(tone, icon, label, value, sub, pct) {
            var $c = $('<div class="MPC-vaspo-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vaspo-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vaspo-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vaspo-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vaspo-mSub"></div>').text(sub));

            if (pct != null) {
                var $bar = $('<div class="MPC-vaspo-mBar"><i></i></div>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $c.append($bar);
            }
            return $c;
        }

        // ---------- Order Progress (Timeline) ---------- //

        // 7-stage progress (mirrors the reference design order). `date` is the
        // action date for the stage; for Expected Delivery it is the required
        // (promised) date. Drafted falls back to the order date.
        function progressStages() {
            return [
                { key: "VAS_092_Drafted",           done: true,                     active: data.CurrentStage === 1, date: data.Created || data.DateOrdered },
                { key: "VAS_092_Completed",         done: data.IsCompleted,         active: data.CurrentStage === 2, date: data.DateOrdered },
                { key: "VAS_092_WithVendor",        done: data.IsWithVendor,        active: data.CurrentStage === 3, date: data.DateOrdered },
                { key: "VAS_092_ExpectedDelivery",  done: data.IsExpectedDelivery,  active: data.CurrentStage === 4, date: data.DatePromised, required: true },
                { key: "VAS_092_PartialDelivered",  done: data.IsPartialDelivered,  active: data.CurrentStage === 5, date: data.LastReceiptDate },
                { key: "VAS_092_InvoiceRaised",     done: data.IsInvoiceRaised,     active: data.CurrentStage === 6, date: data.LastInvoiceDate },
                { key: "VAS_092_PaymentDone",       done: data.IsPaymentDone,       active: data.CurrentStage === 7, date: data.LastPaymentDate }
            ];
        }

        function renderProgress() {
            var stages = progressStages();
            var st = statusTone(data);

            var $sec = section(VIS.Msg.getMsg("VAS_092_OrderProgress"), {
                summary: VIS.Msg.getMsg("VAS_092_Stage") + " " + (data.CurrentStage || 1) +
                    " " + VIS.Msg.getMsg("VAS_092_Of") + " " + stages.length + " · " + st.label
            });

            // Horizontal stepper: numbered circles joined by connector rails.
            // Done stages show a check, the active stage shows its number in an
            // amber ring, pending stages show a muted number.
            var $tl = $('<div class="MPC-vaspo-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];

                var stateCls, statusText;
                if (s.active) {
                    stateCls = "is-active"; statusText = VIS.Msg.getMsg("VAS_092_InProgress");
                } else if (s.done) {
                    stateCls = "is-done"; statusText = VIS.Msg.getMsg("VAS_092_Completed");
                } else {
                    stateCls = "is-pending"; statusText = VIS.Msg.getMsg("VAS_092_Pending");
                }

                // Done stages surface their action date; in-progress / pending
                // stages surface the status word instead.
                var dateText = formatDate(s.date);
                var metaText = statusText;
                if (s.done && dateText) {
                    metaText = s.required
                        ? VIS.Msg.getMsg("VAS_092_Required") + " " + dateText
                        : dateText;
                }

                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        // Stepper node: connector rail (left line + circle + right line) above a
        // centred label (title + meta). The circle shows a check when the stage
        // is done, otherwise its 1-based step number.
        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vaspo-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vaspo-stepRail"></div>');
            $rail.append($('<span class="MPC-vaspo-stepLine MPC-vaspo-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vaspo-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vaspo-stepLine MPC-vaspo-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vaspo-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vaspo-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vaspo-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Line Items (table) ---------- //

        // Kept as a multi-column table per request; restyled to the flat
        // windows-and-panels surface (white card, #D9E2EB border, #E2EAF1 row
        // dividers, em sizing) rather than the Entity List primitive.
        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var $sec = section(VIS.Msg.getMsg("VAS_092_LineItems"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_092_Items") + " · " +
                    formatNumber(+data.TotalQtyOrdered || 0, 0) + " " + VIS.Msg.getMsg("VAS_092_Units") + " · " +
                    formatNumber(+data.TotalQtyDelivered || 0, 0) + " " + VIS.Msg.getMsg("VAS_092_Received")
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-itTable"></div>');

            // Header row
            var $head = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_092_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_092_UnitPrice")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_092_Qty")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_092_ExpDelivery")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_092_LineTotal")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_092_Received")));
            $tbl.append($head);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(lines[i]));
            }

            // Totals footer
            var $foot = $('<div class="MPC-vaspo-tFoot"></div>');
            $foot.append(buildTotalBit(VIS.Msg.getMsg("VAS_092_Subtotal"),
                formatAmount(+data.TotalLines || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(VIS.Msg.getMsg("VAS_092_Tax"),
                formatAmount(+data.TaxAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(VIS.Msg.getMsg("VAS_092_GrandTotal"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            $sec.append($tbl);
        }

        function buildLineRow(ln) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            // Item (name + SKU)
            var $item = $('<span class="MPC-vaspo-itItem"></span>');
            $item.append($('<div class="MPC-vaspo-itName"></div>').text(ln.ProductName || ""));
            if (ln.ProductValue) {
                $item.append($('<div class="MPC-vaspo-itSku"></div>')
                    .text(VIS.Msg.getMsg("VAS_092_SKU") + " " + ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vaspo-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            // Unit price
            $tr.append($('<span></span>').text(formatAmount(
                +ln.PriceActual || 0, data.CurSymbol, data.ISO_Code,
                ln.PricePrecision != null ? ln.PricePrecision : data.StdPrecision)));

            // Qty (centered)
            $tr.append($('<span class="ta-c"></span>').text(
                formatNumber(+ln.QtyOrdered || 0, +ln.UOMPrecision || 0)));

            // Expected delivery (date + small status)
            var $exp = $('<span class="MPC-vaspo-expDate"></span>');
            $exp.append(document.createTextNode(formatDate(ln.DatePromised) || "—"));
            $exp.append($('<small></small>').text(recvLabel(ln.RecvState)));
            $tr.append($exp);

            // Line total (right)
            $tr.append($('<span class="ta-r"></span>').text(formatAmount(
                +ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // Received (right — mini bar + received/ordered)
            var ordered = +ln.QtyOrdered || 0;
            var delivered = +ln.QtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((delivered / ordered) * 100) : 0;
            var $recv = $('<span class="MPC-vaspo-recv ta-r"></span>').addClass(ln.RecvState || "none");
            var $bar = $('<span class="MPC-vaspo-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append(document.createTextNode(
                formatNumber(delivered, +ln.UOMPrecision || 0) + "/" +
                formatNumber(ordered, +ln.UOMPrecision || 0)));
            $tr.append($recv);

            return $tr;
        }

        function recvLabel(state) {
            if (state === "full") return VIS.Msg.getMsg("VAS_092_Delivered");
            if (state === "part") return VIS.Msg.getMsg("VAS_092_Partial");
            return VIS.Msg.getMsg("VAS_092_Awaiting");
        }

        function buildTotalBit(label, value, isGrand) {
            var $bit = $('<span class="MPC-vaspo-tf"></span>');
            if (isGrand) $bit.addClass("is-grand");
            $bit.append(document.createTextNode(label));
            $bit.append($('<b></b>').text(value));
            return $bit;
        }

        // ---------- Landed Cost (table) ---------- //

        // Landed cost distribution-method codes -> display label key + tone.
        var LC_METHODS = {
            "I": { key: "VAS_092_ByValue",    tone: "info"    },   // by value / invoice value
            "Q": { key: "VAS_092_ByQuantity", tone: "success" },
            "W": { key: "VAS_092_ByWeight",   tone: "purple"  },
            "V": { key: "VAS_092_ByVolume",   tone: "warning" },
            "L": { key: "VAS_092_Equally",    tone: "neutral" },   // by line / equally
            "C": { key: "VAS_092_ByCosts",    tone: "neutral" }
        };

        function methodLabel(code) {
            var m = LC_METHODS[code];
            if (m) return VIS.Msg.getMsg(m.key);
            return code ? code : VIS.Msg.getMsg("VAS_092_NotSet");
        }

        function methodTone(code) {
            var m = LC_METHODS[code];
            return m ? m.tone : "neutral";
        }

        // One Entity-List row per cost component. Expected comes from
        // C_ExpectedCost (once the PO is completed); actual replaces it once an
        // invoice-linked C_LandedCostAllocation exists, otherwise the component
        // is "Awaiting invoice". Closes with a section summary and a methodology
        // note. When there are no components an explanatory empty state is shown.
        function renderLandedCost() {
            var comps = (data && data.LandedCostComponents) || [];
            if (!comps.length) return;

            var $sec = section(VIS.Msg.getMsg("VAS_092_LandedCost"), {
                summary: buildLandedSummary(comps)
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-ldTable"></div>');

            // Header row
            var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $h.append($('<span></span>').text(VIS.Msg.getMsg("VAS_092_CostComponent")));
            $h.append($('<span></span>').text(VIS.Msg.getMsg("VAS_092_DistributionMethod")));
            $h.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_092_Expected")));
            $h.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_092_Actual")));
            $h.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_092_Variance")));
            $tbl.append($h);

            for (var i = 0; i < comps.length; i++) {
                $tbl.append(buildComponentRow(comps[i]));
            }

            $tbl.append(buildLandedFooter());
            $sec.append($tbl);

            $sec.append(buildLandedNote(comps));
        }

        // Section meta caption: "{n} components · basis: {method}" when all
        // components share a distribution method, otherwise "· mixed basis".
        function buildLandedSummary(comps) {
            if (!comps.length) return "";
            var seen = {};
            for (var i = 0; i < comps.length; i++) {
                seen[methodLabel(comps[i].DistributionCode)] = true;
            }
            var methods = [];
            for (var k in seen) { if (seen.hasOwnProperty(k)) methods.push(k); }

            var count = comps.length + " " + VIS.Msg.getMsg("VAS_092_Components");
            if (methods.length === 1) {
                return count + " · " + VIS.Msg.getMsg("VAS_092_Basis") + ": " + methods[0];
            }
            return count + " · " + VIS.Msg.getMsg("VAS_092_MixedBasis");
        }

        function buildComponentRow(c) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            // Component name + source / vendor sub-label
            var $name = $('<span class="MPC-vaspo-itItem"></span>');
            $name.append($('<div class="MPC-vaspo-itName"></div>')
                .text(c.ComponentName || VIS.Msg.getMsg("VAS_092_LandedCost")));
            if (c.SourceLabel) {
                $name.append($('<div class="MPC-vaspo-itSku"></div>').text(c.SourceLabel));
            }
            $tr.append($name);

            // Distribution-method pill (tinted, semantic tone)
            $tr.append($('<span></span>').append(
                pill(methodLabel(c.DistributionCode), methodTone(c.DistributionCode))));

            // Expected
            $tr.append($('<span class="ta-r MPC-vaspo-ldExp"></span>').text(
                formatAmount(+c.ExpectedAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // Actual (invoiced amount, or an "Awaiting invoice" placeholder)
            var $act = $('<span class="ta-r MPC-vaspo-ldAct"></span>');
            if (c.IsInvoiced) {
                $act.append($('<span class="MPC-vaspo-ldAmt"></span>').text(
                    formatAmount(+c.ActualAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                $act.append($('<span class="MPC-vaspo-ldFlag inv"></span>')
                    .text(VIS.Msg.getMsg("VAS_092_Invoiced")));
            } else {
                $act.addClass("is-pending");
                $act.append($('<span class="MPC-vaspo-ldAmt"></span>').text("—"));
                $act.append($('<span class="MPC-vaspo-ldFlag wait"></span>')
                    .text(VIS.Msg.getMsg("VAS_092_AwaitingInvoice")));
            }
            $tr.append($act);

            // Variance (only once actualised)
            $tr.append(buildVarianceCell(c));
            return $tr;
        }

        function buildVarianceCell(c) {
            var $v = $('<span class="ta-r MPC-vaspo-ldVar"></span>');
            var amt = formatAmount(Math.abs(+c.VarianceAmt || 0),
                data.CurSymbol, data.ISO_Code, data.StdPrecision);
            if (c.VarianceStatus === "over") {
                $v.addClass("over").text("+" + amt);
            } else if (c.VarianceStatus === "under") {
                $v.addClass("under").text("−" + amt);
            } else if (c.VarianceStatus === "on_budget") {
                $v.addClass("flat").text(VIS.Msg.getMsg("VAS_092_OnBudget"));
            } else {
                $v.addClass("flat").text("—");
            }
            return $v;
        }

        function buildLandedFooter() {
            var $foot = $('<div class="MPC-vaspo-tFoot MPC-vaspo-ldFoot"></div>');
            $foot.append(buildLandedTotal(VIS.Msg.getMsg("VAS_092_ExpectedLandedCost"),
                formatAmount(+data.ExpectedLandedCost || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(VIS.Msg.getMsg("VAS_092_ActualToDate"),
                formatAmount(+data.ActualToDate || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(VIS.Msg.getMsg("VAS_092_OpenNotInvoiced"),
                formatAmount(+data.OpenNotInvoiced || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, true));
            $foot.append(buildLandedTotal(VIS.Msg.getMsg("VAS_092_LandedValue"),
                formatAmount(+data.LandedValue || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                true, false));
            return $foot;
        }

        function buildLandedTotal(label, value, isGrand, isWarn) {
            var $bit = $('<span class="MPC-vaspo-tf MPC-vaspo-lf"></span>');
            if (isGrand) $bit.addClass("is-grand");
            $bit.append(document.createTextNode(label));
            var $b = $('<b></b>').text(value);
            if (isWarn) $b.addClass("warn");
            $bit.append($b);
            return $bit;
        }

        // Quiet caption explaining the expected -> actual lifecycle with a live
        // invoiced-progress count.
        function buildLandedNote(comps) {
            var $note = $('<div class="MPC-vaspo-note"></div>');
            $note.append(svgIcon("info"));

            var invoiced = data.InvoicedComponentCount || 0;
            var total = data.LandedComponentCount || comps.length;
            var text = VIS.Msg.getMsg("VAS_092_LandedMethodology") + " " +
                invoiced + " " + VIS.Msg.getMsg("VAS_092_Of") + " " + total + " " +
                VIS.Msg.getMsg("VAS_092_ComponentsInvoiced") + ".";
            $note.append($('<span></span>').text(text));
            return $note;
        }

        // ---------- Bottom row (Terms & Notes | Recent Activity) ---------- //

        // Two-column footer, per the reference design: Terms & Notes on the left
        // and the typed Recent Activity feed on the right. The grid auto-fits, so
        // a single present section fills the row and both stack on narrow panels.
        function renderBottom() {
            var termsText = data.POReference || data.OrderDescription;
            var activity = (data && data.Activity) || [];
            if (!termsText && !activity.length) return;

            var $row = $('<div class="MPC-vaspo-bottom"></div>');
            $body.append($row);

            if (termsText) renderTerms($row, termsText);
            if (activity.length) renderActivity($row, activity);
        }

        // ---------- Terms & Notes ---------- //

        // VAI163 2026-06-17  Terms & Notes description value sourced from
        // C_Order.POReference (was C_Order.Description) per design correction.
        // VAI163 2026-07-01  Renders one paragraph per line break and lives in
        // the left column of the bottom row.
        function renderTerms($parent, text) {
            var $sec = section(VIS.Msg.getMsg("VAS_092_TermsAndNotes"), null, $parent);
            var $card = $('<div class="MPC-vaspo-textCard"></div>');

            var paras = String(text).split(/\r?\n+/);
            for (var i = 0; i < paras.length; i++) {
                var t = paras[i].trim();
                if (t) $card.append($('<p></p>').text(t));
            }
            if (!$card.children().length) $card.append($('<p></p>').text(text));

            $sec.append($card);
        }

        // ---------- Recent Activity (typed feed) ---------- //

        // Type -> tag descriptor. tone drives the tag colour; icon is the leading
        // glyph; tagKey is the short chip label; titleKey is the sentence shown
        // as the row title (notes carry their own free text instead).
        var ACT_TYPES = {
            note:     { tone: "info",    icon: "mail",  tagKey: "VAS_092_TagNote",     titleKey: null },
            grn:      { tone: "success", icon: "inbox", tagKey: "VAS_092_TagGRN",      titleKey: "VAS_092_ActGRN" },
            invoice:  { tone: "info",    icon: "doc",   tagKey: "VAS_092_TagInvoice",  titleKey: "VAS_092_ActInvoice" },
            payment:  { tone: "success", icon: "coins", tagKey: "VAS_092_TagPayment",  titleKey: "VAS_092_ActPayment" },
            approval: { tone: "purple",  icon: "check", tagKey: "VAS_092_TagApproval", titleKey: "VAS_092_ActApproval" },
            created:  { tone: "neutral", icon: "doc",   tagKey: "VAS_092_TagCreated",  titleKey: "VAS_092_ActCreated" }
        };

        function renderActivity($parent, activity) {
            var $sec = section(VIS.Msg.getMsg("VAS_092_RecentActivity"), {
                summary: activity.length + " " + VIS.Msg.getMsg("VAS_092_Updates")
            }, $parent);

            var $card = $('<div class="MPC-vaspo-actList"></div>');
            for (var i = 0; i < activity.length; i++) {
                $card.append(activityRow(activity[i]));
            }
            $sec.append($card);
        }

        // Activity row: tag chip (icon + short label) + title + right-aligned
        // timestamp (with actor when present).
        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="MPC-vaspo-actRow"></div>');
            $row.append(activityTag(meta));
            $row.append($('<span class="MPC-vaspo-actTitle"></span>').text(activityTitle(a, meta)));

            var when = formatDateTime(a.Created);
            if (a.UserName) when += " · " + a.UserName;
            $row.append($('<span class="MPC-vaspo-actWhen"></span>').text(when));
            return $row;
        }

        function activityTag(meta) {
            var $t = $('<span class="MPC-vaspo-actTag"></span>').addClass("tone-" + meta.tone);
            if (meta.icon) $t.append(svgIcon(meta.icon));
            $t.append($('<span></span>').text(VIS.Msg.getMsg(meta.tagKey)));
            return $t;
        }

        // Notes show their own text; event rows build a sentence from the type's
        // title message, appending the GRN line count and any related document no.
        function activityTitle(a, meta) {
            if (!meta.titleKey) return a.Text || VIS.Msg.getMsg("VAS_092_TagNote");

            var s = VIS.Msg.getMsg(meta.titleKey);
            if (a.Type === "grn" && a.Count > 0) {
                s += " · " + a.Count + " " + VIS.Msg.getMsg("VAS_092_Lines");
            }
            if (a.DocumentNo) s += " (" + a.DocumentNo + ")";
            return s;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        // Static inline SVG markup (stroke uses currentColor so colour is
        // driven by the wrapping CSS class).
        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            box:      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            info:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            clipboardCheck: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            factory:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 20V9l6 4V9l6 4V9l6 4v7Z"/><path d="M2 20h20"/><path d="M7 20v-4"/><path d="M12 20v-4"/><path d="M17 20v-4"/></svg>',
            pencil:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            calendar: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            inbox:    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/></svg>'
        };

        // Returns a span wrapping the named inline SVG. innerHTML is used so the
        // browser parses the SVG in HTML context (no namespace juggling).
        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vaspo-ic"></span>');
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

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var cur = symbol || iso || "";
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

        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                var datePart = d.toLocaleDateString(window.navigator.language, {
                    month: "short", day: "2-digit"
                });
                var timePart = d.toLocaleTimeString(window.navigator.language, {
                    hour: "2-digit", minute: "2-digit"
                });
                return datePart + ", " + timePart;
            } catch (e) {
                return d.toString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_092_OverviewPurchaseOrder.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
