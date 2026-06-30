/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Order Overview tab panel. Renders a
 *                  review-oriented overview of the selected purchase
 *                  order (C_Order, IsSoTrx = 'N'): identity, linked
 *                  origin docs, stat strip, 7-stage progress, line
 *                  items with received progress, landed cost, and a
 *                  terms / recent-activity area. Data is fetched from
 *                  VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview.
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
            renderHero();
            renderVendor();
            renderLinked();
            renderProgress();
            renderLines();
            renderLandedCost();
            renderTerms();
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Section / primitive builders                                      //
        // ----------------------------------------------------------------- //

        // A headered section: Section Header (title + optional summary/action)
        // followed by a content node. Returns the section element so callers can
        // append additional bodies.
        function section(title, opts) {
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
            $body.append($sec);
            return $sec;
        }

        // Status pill (tinted). tone: info | success | warning | risk | neutral | purple
        function pill(label, tone) {
            return $('<span class="MPC-vaspo-pill"></span>')
                .addClass("tone-" + (tone || "neutral"))
                .text(label);
        }

        // ---------- Hero Status Card ---------- //

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

        function priorityLabel() {
            var prio = (data.Priority || "low").toLowerCase();
            if (prio === "high") return VIS.Msg.getMsg("VAS_092_HighPriority");
            if (prio === "med")  return VIS.Msg.getMsg("VAS_092_MediumPriority");
            return VIS.Msg.getMsg("VAS_092_LowPriority");
        }

        function renderHero() {
            var st = statusTone(data);

            var $hero = $('<section class="MPC-vaspo-hero"></section>')
                .addClass("tone-" + st.tone);

            // Top row: title + subtitle (left) and status pill (right).
            var $top = $('<div class="MPC-vaspo-heroTop"></div>');
            var $tl = $('<div class="MPC-vaspo-heroTitleWrap"></div>');
            $tl.append($('<div class="MPC-vaspo-heroTitle"></div>').text(
                VIS.Msg.getMsg("VAS_092_PurchaseOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var created = formatDate(data.DateOrdered);
            if (created) subBits.push(VIS.Msg.getMsg("VAS_092_Created") + " " + created);
            if (data.BuyerName) subBits.push(VIS.Msg.getMsg("VAS_092_Buyer") + " " + data.BuyerName);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vaspo-heroSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);
            $top.append($('<span class="MPC-vaspo-heroPill"></span>').text(st.label));
            $hero.append($top);

            // Emphasis row: grand total headline.
            var $emph = $('<div class="MPC-vaspo-heroEmph"></div>');
            $emph.append($('<span class="MPC-vaspo-heroEmphVal"></span>').text(
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $emph.append($('<span class="MPC-vaspo-heroEmphQual"></span>').text(
                (data.ISO_Code || "") + " · " + VIS.Msg.getMsg("VAS_092_InclTaxFreight")));
            $hero.append($emph);

            // Embedded Metric Grid (the one allowed nesting case).
            var ordered  = +data.TotalQtyOrdered || 0;
            var received = +data.TotalQtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((received / ordered) * 100) : 0;

            var cells = [
                [VIS.Msg.getMsg("VAS_092_ExpectedDelivery"), formatDate(data.DatePromised) || "—"],
                [VIS.Msg.getMsg("VAS_092_LineItems"),
                    (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_092_Lines")],
                [VIS.Msg.getMsg("VAS_092_Received"),
                    formatNumber(received, 0) + " / " + formatNumber(ordered, 0) + " · " + pct + "%"],
                [VIS.Msg.getMsg("Priority"), priorityLabel()]
            ];
            $hero.append(metricGrid(cells));

            $body.append($hero);
        }

        // 2-col Metric Grid from an array of [label, value] pairs.
        function metricGrid(cells) {
            var $g = $('<div class="MPC-vaspo-mgrid"></div>');
            for (var i = 0; i < cells.length; i++) {
                if (!cells[i]) continue;
                var $c = $('<div class="MPC-vaspo-mcell"></div>');
                $c.append($('<div class="MPC-vaspo-mLabel"></div>').text(cells[i][0]));
                $c.append($('<div class="MPC-vaspo-mVal"></div>').text(cells[i][1]));
                $g.append($c);
            }
            return $g;
        }

        // ---------- Vendor ---------- //

        function renderVendor() {
            // Hide the whole section when no vendor data is present.
            if (!data.VendorName && !data.VendorAddress &&
                !data.ContactName && !data.ContactPhone && !data.ContactEmail &&
                !data.PaymentTermName && !data.WarehouseName && !data.OrgName &&
                !data.ISO_Code) {
                return;
            }

            var $sec = section(VIS.Msg.getMsg("VAS_092_Vendor"));

            // Identity block: name + address + contact bits.
            var $id = $('<div class="MPC-vaspo-vendId"></div>');
            $id.append($('<div class="MPC-vaspo-vendName"></div>').text(data.VendorName || ""));

            if (data.VendorAddress) {
                var $addr = $('<div class="MPC-vaspo-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.VendorAddress));
                $id.append($addr);
            }

            var $contact = $('<div class="MPC-vaspo-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $id.append($contact);
            $sec.append($id);

            // Terms as a Metric Grid (2 or 4 cells — never odd).
            var cells = [];
            if (data.PaymentTermName) cells.push([VIS.Msg.getMsg("VAS_092_PaymentTerms"), data.PaymentTermName]);
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.trim()) cells.push([VIS.Msg.getMsg("VAS_092_Currency"), cur]);
            if (data.WarehouseName) cells.push([VIS.Msg.getMsg("VAS_092_ShipTo"), data.WarehouseName]);
            if (data.OrgName) cells.push([VIS.Msg.getMsg("VAS_092_BillTo"), data.OrgName]);
            // Keep the grid even (drop the trailing cell if odd) to avoid a broken row.
            if (cells.length % 2 === 1) cells.pop();
            if (cells.length) $sec.append(metricGrid(cells));
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vaspo-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Generated From (Compact List) ---------- //

        function renderLinked() {
            // Production Order is never linked in this payload, so the section
            // only carries data when a sales order or requisition is linked.
            var hasSales = !!data.RefOrderDocNo;
            var hasReq = data.RequisitionLineCount > 0;
            if (!hasSales && !hasReq) return;

            var $sec = section(VIS.Msg.getMsg("VAS_092_GeneratedFrom"));
            var $list = $('<div class="MPC-vaspo-clist"></div>');

            // Sales Order (origin) — from Ref_Order_ID.
            if (data.RefOrderDocNo) {
                $list.append(compactRow(VIS.Msg.getMsg("VAS_092_SalesOrder"), null,
                    data.RefOrderDocNo, pill(VIS.Msg.getMsg("VAS_092_Origin"), "info")));
            } else {
                $list.append(compactRow(VIS.Msg.getMsg("VAS_092_SalesOrder"), null,
                    null, pill(VIS.Msg.getMsg("VAS_092_NotLinked"), "neutral")));
            }

            // Requisition — present when any requisition line points at this PO.
            if (data.RequisitionLineCount > 0) {
                $list.append(compactRow(VIS.Msg.getMsg("VAS_092_Requisition"), null,
                    null, pill(VIS.Msg.getMsg("VAS_092_Linked"), "success")));
            } else {
                $list.append(compactRow(VIS.Msg.getMsg("VAS_092_Requisition"), null,
                    null, pill(VIS.Msg.getMsg("VAS_092_NotLinked"), "neutral")));
            }

            // Production Order — overview payload carries no production-order link.
            $list.append(compactRow(VIS.Msg.getMsg("VAS_092_ProductionOrder"), null,
                null, pill(VIS.Msg.getMsg("VAS_092_NotLinked"), "neutral")));

            $sec.append($list);
        }

        // Compact List row: left (primary + optional meta), right (optional
        // trailing value + optional status pill).
        function compactRow(primary, meta, trailingValue, $statusPill) {
            var $row = $('<div class="MPC-vaspo-crow"></div>');

            var $left = $('<div class="MPC-vaspo-cLeft"></div>');
            $left.append($('<div class="MPC-vaspo-cPri"></div>').text(primary));
            if (meta) $left.append($('<div class="MPC-vaspo-cMeta"></div>').text(meta));
            $row.append($left);

            var $right = $('<div class="MPC-vaspo-cRight"></div>');
            if ($statusPill) $right.append($statusPill);
            if (trailingValue) $right.append($('<span class="MPC-vaspo-cVal"></span>').text(trailingValue));
            $row.append($right);

            return $row;
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

        // ---------- Terms & Notes ---------- //

        // VAI163 2026-06-17  Terms & Notes description value sourced from
        // C_Order.POReference (was C_Order.Description) per design correction.
        function renderTerms() {
            if (!data.POReference) return;

            var $sec = section(VIS.Msg.getMsg("VAS_092_TermsAndNotes"));
            var $card = $('<div class="MPC-vaspo-textCard"></div>');
            $card.append($('<p></p>').text(data.POReference));
            $sec.append($card);
        }

        // ---------- Recent Activity (Timeline) ---------- //

        function renderActivity() {
            var activity = (data && data.Activity) || [];
            if (!activity.length) return;

            var $sec = section(VIS.Msg.getMsg("VAS_092_RecentActivity"), {
                summary: activity.length + " " + VIS.Msg.getMsg("VAS_092_Updates")
            });

            var $tl = $('<div class="MPC-vaspo-tl"></div>');
            for (var i = 0; i < activity.length; i++) {
                var a = activity[i];
                // Activity rows come from CM_ChatEntry (notes/log) — a single
                // neutral "Note" tag rather than inferring a type.
                var meta = formatDateTime(a.Created);
                if (a.UserName) meta += " · " + a.UserName;
                $tl.append(timelineEntry(a.Text || VIS.Msg.getMsg("VAS_092_Note"),
                    pill(VIS.Msg.getMsg("VAS_092_Note"), "neutral"), meta, "is-done"));
            }
            $sec.append($tl);
        }

        // Vertical timeline entry (used by Recent Activity): rail (dot + trail)
        // + card (title + tag pill + meta).
        function timelineEntry(title, $tagPill, meta, stateCls) {
            var $entry = $('<div class="MPC-vaspo-tlEntry"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vaspo-tlRail"></div>');
            $rail.append($('<span class="MPC-vaspo-tlDot"></span>'));
            $rail.append($('<span class="MPC-vaspo-tlTrail"></span>'));
            $entry.append($rail);

            var $card = $('<div class="MPC-vaspo-tlCard"></div>');
            var $tcTop = $('<div class="MPC-vaspo-tlTop"></div>');
            $tcTop.append($('<span class="MPC-vaspo-tlTitle"></span>').text(title));
            if ($tagPill) $tcTop.append($tagPill);
            $card.append($tcTop);
            if (meta) $card.append($('<div class="MPC-vaspo-tlMeta"></div>').text(meta));
            $entry.append($card);

            return $entry;
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
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>'
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
