/************************************************************
 * Module Name    : VAS
 * Purpose        : Goods Receipt Note (GRN) Overview tab panel. Renders a
 *                  review-oriented overview of the selected goods receipt
 *                  (M_InOut, IsSOTrx = 'N'): header identity + supplier /
 *                  receipt details card, a four-card KPI snapshot (received
 *                  value, lines, received qty, quality-check applicability),
 *                  a compact receipt timeline (PO date -> expected -> received
 *                  -> posted), the material line table and visual action
 *                  buttons (Print / Complete GRN / Generate Invoice). Data is
 *                  fetched from VAS_099_OverviewGRN/GetGRNOverview.
 *                  All on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_099_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_099_OverviewGRN = function () {
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
            $root = $('<div class="MPC-vasgrn-root"></div>');
            $body = $('<div class="MPC-vasgrn-body"></div>');
            $emptyState = $('<div class="MPC-vasgrn-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_099_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_099_OverviewGRN/GetGRNOverview",
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
            renderTimeline();
            renderLines();
            renderActions();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        // A headered section: Section Header (title + optional summary) followed
        // by a content node. Returns the section element so callers can append.
        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasgrn-sec"></section>');
            var $head = $('<div class="MPC-vasgrn-secHead"></div>');
            $head.append($('<h2 class="MPC-vasgrn-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasgrn-secSummary"></span>').text(opts.summary));
            }
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Returns "N/A" for blank values so the layout never shows an empty cell.
        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_099_NA")
                : value;
        }

        // The currency token: prefer the linked order's symbol / ISO, else INR.
        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status / priority maps (codes -> label + tone) ---------- //

        // DocStatus code -> { key, tone }. tone drives the pill colour.
        var STATUS_MAP = {
            "DR": { key: "VAS_099_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_099_InProgress",          tone: "info" },
            "AP": { key: "VAS_099_Approved",            tone: "info" },
            "CO": { key: "VAS_099_Completed",           tone: "success" },
            "CL": { key: "VAS_099_Closed",              tone: "neutral" },
            "VO": { key: "VAS_099_Voided",              tone: "risk" },
            "RE": { key: "VAS_099_Reversed",            tone: "risk" },
            "WC": { key: "VAS_099_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_099_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_099_Invalid",             tone: "risk" },
            "NA": { key: "VAS_099_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // PriorityRule code -> { key, tone }.
        var PRIORITY_MAP = {
            "1": { key: "VAS_099_Urgent", tone: "risk" },
            "3": { key: "VAS_099_High",   tone: "warning" },
            "5": { key: "VAS_099_Medium", tone: "info" },
            "7": { key: "VAS_099_Low",    tone: "neutral" },
            "9": { key: "VAS_099_Minor",  tone: "neutral" }
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
            var $strip = $('<section class="MPC-vasgrn-hdr"></section>');
            var $top = $('<div class="MPC-vasgrn-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasgrn-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasgrn-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_099_GoodsReceiptNote") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var received = formatDate(data.MovementDate);
            if (received) subBits.push(VIS.Msg.getMsg("VAS_099_Received") + " " + received);
            if (data.ReceivedBy) subBits.push(VIS.Msg.getMsg("VAS_099_ReceivedBy") + " " + data.ReceivedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasgrn-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasgrn-hdrPills"></div>');
            if (pm) $pills.append(headerPill(pm.label, pm.tone, "chevUp", false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: supplier identity (left) + receipt fields (right) ---
            var $card = $('<section class="MPC-vasgrn-hdrCard"></section>');

            // Left column: supplier name + GSTIN + address + contact bits.
            var $left = $('<div class="MPC-vasgrn-hdrColL"></div>');
            $left.append($('<div class="MPC-vasgrn-fLabel"></div>').text(VIS.Msg.getMsg("VAS_099_Supplier")));
            $left.append($('<div class="MPC-vasgrn-vendName"></div>').text(na(data.SupplierName)));

            if (data.SupplierTaxID) {
                var $gst = $('<div class="MPC-vasgrn-vendGst"></div>');
                $gst.append($('<span class="MPC-vasgrn-gstLbl"></span>')
                    .text(VIS.Msg.getMsg("VAS_099_GSTIN") + " "));
                $gst.append($('<span></span>').text(data.SupplierTaxID));
                $left.append($gst);
            }

            if (data.SupplierAddress) {
                var $addr = $('<div class="MPC-vasgrn-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.SupplierAddress));
                $left.append($addr);
            }
            $left.append(headerField(VIS.Msg.getMsg("VAS_099_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_099_Posted")
                    : VIS.Msg.getMsg("VAS_099_NotPosted"), false));
            var $contact = $('<div class="MPC-vasgrn-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right column: labelled receipt / reference fields.
            var $right = $('<div class="MPC-vasgrn-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_AgainstPO"),
                data.PONo || VIS.Msg.getMsg("VAS_099_NotLinked"), !!data.PONo));
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_Warehouse"), na(data.WarehouseName), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_ReceivedDate"),
                na(formatDate(data.MovementDate)), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_ReferenceInvoice"),
                na(data.ReferenceInvoice), false));            
            $card.append($right);

            $body.append($card);
        }

        // Header pill: tinted chip with an optional leading chevron (priority) or
        // a leading dot (status).
        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasgrn-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasgrn-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        // Labelled field block for the details card's right column. When `link`
        // is set the value is rendered in the link colour (e.g. the PO number).
        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasgrn-hdrField"></div>');
            $f.append($('<div class="MPC-vasgrn-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasgrn-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasgrn-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasgrn-snap"></section>');
            var cur = currencyToken();

            // Received value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_099_ReceivedValue"),
                formatAmount(+data.ReceivedValue || 0, cur, data.StdPrecision),
                data.ISO_Code || ""));

            // Lines received.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_099_Lines"),
                (data.LineCount || 0) + "",
                VIS.Msg.getMsg("VAS_099_LinesReceived")));

            // Received quantity (units).
            $snap.append(metricCard("received", "inbox", VIS.Msg.getMsg("VAS_099_ReceivedQty"),
                formatNumber(+data.ReceivedQty || 0, 0),
                VIS.Msg.getMsg("VAS_099_Units")));

            // Quality check applicability (Applicable when any QC line exists).
            var qc = (data.QcLineCount || 0) > 0;
            $snap.append(metricCard("quality", "clipboardCheck", VIS.Msg.getMsg("VAS_099_QualityCheck"),
                qc ? VIS.Msg.getMsg("VAS_099_Applicable") : VIS.Msg.getMsg("VAS_099_NotApplicable"),
                qc ? ((data.QcLineCount || 0) + " " + VIS.Msg.getMsg("VAS_099_QCLines")) : ""));

            $body.append($snap);
        }

        // Metric card: colour-accented left border (via tone class), a header
        // (icon + label), a large value and a caption.
        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasgrn-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasgrn-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasgrn-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasgrn-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasgrn-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Receipt timeline (horizontal stepper) ---------- //

        // Four stages from the dates the receipt actually carries. A stage is
        // "done" once its date is present (posting once the Posted flag is set);
        // the active stage is the last done one.
        function timelineStages() {
            return [
                { key: "VAS_099_PODate",            date: data.PODate,       done: !!data.PODate },
                { key: "VAS_099_ExpectedDelivery",  date: data.ExpectedDate, done: !!data.ExpectedDate },
                { key: "VAS_099_Received",          date: data.MovementDate, done: !!data.MovementDate },
                { key: "VAS_099_Posting",           date: data.PostingDate,  done: !!data.Posted }
            ];
        }

        function renderTimeline() {
            var stages = timelineStages();

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) {
                if (stages[k].done) activeIdx = k;
            }

            var $sec = section(VIS.Msg.getMsg("VAS_099_ReceiptTimeline"), null);

            var $tl = $('<div class="MPC-vasgrn-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];

                var stateCls, metaText;
                if (i === activeIdx) {
                    stateCls = "is-active";
                    metaText = formatDate(s.date) || VIS.Msg.getMsg("VAS_099_Done");
                } else if (s.done) {
                    stateCls = "is-done";
                    metaText = formatDate(s.date) || VIS.Msg.getMsg("VAS_099_Done");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_099_Pending");
                }

                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        // Stepper node: connector rail (left line + circle + right line) above a
        // centred label. The circle shows a check when done, else its number.
        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasgrn-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasgrn-stepRail"></div>');
            $rail.append($('<span class="MPC-vasgrn-stepLine MPC-vasgrn-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasgrn-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasgrn-stepLine MPC-vasgrn-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasgrn-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasgrn-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasgrn-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Material lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var cur = currencyToken();

            var $sec = section(VIS.Msg.getMsg("VAS_099_MaterialLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_099_Items") + " · " +
                    formatNumber(+data.ReceivedQty || 0, 0) + " " + VIS.Msg.getMsg("VAS_099_Units")
            });

            var $tbl = $('<div class="MPC-vasgrn-table"></div>');

            // Header row
            var $head = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_099_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_099_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Ordered")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Received")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Rate")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Amount")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_099_Quality")));
            $tbl.append($head);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(lines[i], cur));
            }

            // Totals footer
            var $foot = $('<div class="MPC-vasgrn-tFoot"></div>');
            var $bit = $('<span class="MPC-vasgrn-tf is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_099_TotalReceivedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.ReceivedValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);
        }

        function buildLineRow(ln, cur) {
            var $tr = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tBody"></div>');

            // Item (name + SKU / locator)
            var $item = $('<span class="MPC-vasgrn-itItem"></span>');
            $item.append($('<div class="MPC-vasgrn-itName"></div>').text(na(ln.ProductName)));
            var metaBits = [];
            if (ln.ProductCode) metaBits.push(VIS.Msg.getMsg("VAS_099_SKU") + " " + ln.ProductCode);
            if (ln.LocatorName) metaBits.push(VIS.Msg.getMsg("VAS_099_Locator") + " " + ln.LocatorName);
            if (metaBits.length) {
                $item.append($('<div class="MPC-vasgrn-itSku"></div>').text(metaBits.join(" · ")));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasgrn-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Ordered
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.OrderedQty || 0, prec)));

            // Received (mini bar + received/ordered)
            var ordered = +ln.OrderedQty || 0;
            var received = +ln.ReceivedQty || 0;
            var pct = ordered > 0 ? Math.min(100, Math.round((received / ordered) * 100)) : (received > 0 ? 100 : 0);
            var recvState = ordered > 0 && received >= ordered ? "full" : (received > 0 ? "part" : "none");
            var $recv = $('<span class="MPC-vasgrn-recv ta-r"></span>').addClass(recvState);
            var $bar = $('<span class="MPC-vasgrn-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append(document.createTextNode(formatNumber(received, prec)));
            $tr.append($recv);

            // Rate
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.UnitRate || 0, cur, data.StdPrecision)));

            // Amount
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Quality marker
            var $q = $('<span class="ta-c"></span>');
            if (ln.QualityApplicable) {
                $q.append($('<span class="MPC-vasgrn-tag q-on"></span>')
                    .text(VIS.Msg.getMsg("VAS_099_Applicable")));
            } else {
                $q.append($('<span class="MPC-vasgrn-tag q-off"></span>')
                    .text(VIS.Msg.getMsg("VAS_099_NotApplicable")));
            }
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only — the panel is a read-only overview. Document
        // actions run from the host window toolbar; these mirror them visually.
        function renderActions() {
            var $bar = $('<section class="MPC-vasgrn-actions"></section>');
            $bar.append(actionButton("printer", VIS.Msg.getMsg("VAS_099_Print"), "sec"));
            $bar.append(actionButton("check",   VIS.Msg.getMsg("VAS_099_CompleteGRN"), "pri"));
            $bar.append(actionButton("doc",     VIS.Msg.getMsg("VAS_099_GenerateInvoice"), "sec"));
            $body.append($bar);
        }

        function actionButton(icon, label, kind) {
            var $b = $('<span class="MPC-vasgrn-btn"></span>').addClass("btn-" + (kind || "sec"));
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            inbox:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/></svg>',
            clipboardCheck: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            printer:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>'
        };

        // Returns a span wrapping the named inline SVG (innerHTML so the browser
        // parses the SVG in HTML context — no namespace juggling).
        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasgrn-ic"></span>');
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

    VAS.VAS_099_OverviewGRN.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_099_OverviewGRN.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_099_OverviewGRN.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_099_OverviewGRN.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
