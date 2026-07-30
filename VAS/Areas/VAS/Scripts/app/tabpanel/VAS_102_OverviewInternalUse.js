/************************************************************
 * Module Name    : VAS
 * Purpose        : Internal Use / Material Issue Overview tab panel. Renders a
 *                  review-oriented overview of the selected internal-use material
 *                  issue (M_Inventory, IsInternalUse = 'Y'): header identity +
 *                  warehouse / issue details card, a four-card KPI snapshot
 *                  (issued value, quantity issued, quantity not fully issued,
 *                  total lines), Full / Partial / Short status cards, a
 *                  References section (linked requisition / work order), a
 *                  compact issue timeline (Created -> Issued -> Posting) and an
 *                  issue-lines table with per-line requested / issued /
 *                  available / value. Data is fetched from
 *                  VAS_102_OverviewInternalUse/GetInternalUseOverview. All
 *                  on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_102_...").
 * Chronological development:
 *   VAI163   2026-07-07  Created
 *   VAI163   2026-07-29  - Requested quantity now shows what the linked
 *                          requisition asked for, so a partially issued line reads
 *                          e.g. requested 7 / issued 5 (model side).
 *                        - Not Fully Issued, Partial and Short now report the
 *                          outstanding QUANTITY (requested - issued) instead of a
 *                          line count; the line count moves to the card caption.
 *                        - Added the References section (requisition no, dates,
 *                          requested by, work order, note); the header card's
 *                          Reference field shows the requisition no instead of
 *                          N/A when the issue came from one.
 *                        - Removed the Pending Only filter and the Issue Stock /
 *                          Post Inventory buttons (both live in the header panel).
 *   VAI163   2026-07-29  - Issue Timeline reads real stamps: Created shows the
 *                          record's creation moment (not the movement date),
 *                          Issued shows the completion moment and Posting shows
 *                          when posting actually ran.
 *                        - Line items drop the "SKU" prefix before the product
 *                          search key.
 *                        - Added the Notes section (the issue header's
 *                          description) and, at the bottom, the Activity section:
 *                          created / updated / completed / posted milestones plus
 *                          chat notes, newest-first.
 *   VAI163   2026-07-29  - References / Origin read the VA075 service work order
 *                          (document no + its reference); an issue raised against
 *                          one no longer reads "Manual Issue".
 *                        - Line items show the Attribute Set Instance sub-line and
 *                          carry the full product name as a hover tooltip.
 *                        - Issue lines page client-side at 10 rows with a
 *                          Previous / Next pager; the KPI cards and the totals
 *                          footer always cover the whole issue, never the page.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_102_OverviewInternalUse = function () {
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

        // Issue lines are paged client-side (the whole set arrives in one
        // payload). Page index resets whenever a different record is loaded.
        var LINES_PER_PAGE = 10;
        var linesPage = 0;

        this.init = function () {
            $root = $('<div class="MPC-vasiu-root"></div>');
            $body = $('<div class="MPC-vasiu-body"></div>');
            $emptyState = $('<div class="MPC-vasiu-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_102_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_102_OverviewInternalUse/GetInternalUseOverview",
                type: "GET",
                dataType: "json",
                data: { M_Inventory_ID: recordID },
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
            $body.empty();

            if (!data || !data.M_Inventory_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHeader();
            renderReferences();
            renderSnapshot();
            renderStatusCards();
            renderTimeline();
            renderLines();
            renderNotes();
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasiu-sec"></section>');
            var $head = $('<div class="MPC-vasiu-secHead"></div>');
            $head.append($('<h2 class="MPC-vasiu-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasiu-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_102_NA")
                : value;
        }

        // Prefer the seeded AD_Message; else a readable English fallback; else the
        // key. Used for message keys that may not be seeded yet.
        //
        // An unseeded key comes back either as the key itself or wrapped in square
        // brackets ("[VAS_102_References]") depending on the platform build — both
        // count as "not found", or the panel renders raw keys at the user.
        function msg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m) {
                    var bare = (m.charAt(0) === "[" && m.charAt(m.length - 1) === "]")
                        ? m.substring(1, m.length - 1) : m;
                    if (bare.toUpperCase() !== String(key).toUpperCase()) return m;
                }
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : key;
        }

        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // Decimals for a quantity that spans several lines: the widest UOM
        // precision on the issue, so a summed figure never loses a fraction.
        function qtyPrecision() {
            var lines = (data && data.Lines) || [];
            var p = 0;
            for (var i = 0; i < lines.length; i++) {
                p = Math.max(p, +lines[i].UOMPrecision || 0);
            }
            return p;
        }

        // Outstanding quantity on a line: what was requested and has not been
        // issued/consumed yet. Never negative (over-issue counts as nothing due).
        function pendingQty(ln) {
            var diff = (+ln.RequestedQty || 0) - (+ln.IssuedQty || 0);
            return diff > 0 ? diff : 0;
        }

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_102_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_102_InProgress",          tone: "info" },
            "AP": { key: "VAS_102_Approved",            tone: "info" },
            "CO": { key: "VAS_102_Completed",           tone: "info" },
            "CL": { key: "VAS_102_Closed",              tone: "success" },
            "VO": { key: "VAS_102_Voided",              tone: "risk" },
            "RE": { key: "VAS_102_Reversed",            tone: "risk" },
            "WC": { key: "VAS_102_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_102_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_102_Invalid",             tone: "risk" },
            "NA": { key: "VAS_102_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // ---------- Origin map (code -> label) ---------- //

        var ORIGIN_MAP = {
            "WORKORDER":   { key: "VAS_102_WorkOrder",      def: "Work Order" },
            "PRODUCTION":  { key: "VAS_102_ProductionOrder", def: "Production Order" },
            "REQUISITION": { key: "VAS_102_Requisition",     def: "Requisition" },
            "MANUAL":      { key: "VAS_102_ManualIssue",     def: "Manual Issue" }
        };

        function originLabel() {
            var m = ORIGIN_MAP[data.OriginCode] || ORIGIN_MAP.MANUAL;
            return msg(m.key, m.def);
        }

        // Line status derived from requested vs issued.
        // Full: issued >= requested; Short: issued <= 0; Partial: otherwise.
        function lineStatus(requested, issued) {
            if (issued >= requested) return "full";
            if (issued <= 0) return "short";
            return "partial";
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="MPC-vasiu-hdr"></section>');
            var $top = $('<div class="MPC-vasiu-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasiu-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasiu-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_102_InternalUse") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_102_MovementDate") + " " + moved);
            if (data.IssuedBy) subBits.push(VIS.Msg.getMsg("VAS_102_IssuedBy") + " " + data.IssuedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasiu-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasiu-hdrPills"></div>');
            $pills.append(headerPill(originLabel(), "info", "layers", false));
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_102_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: warehouse identity (left) + document fields (right) ---
            var $card = $('<section class="MPC-vasiu-hdrCard"></section>');

            var $left = $('<div class="MPC-vasiu-hdrColL"></div>');
            $left.append($('<div class="MPC-vasiu-fLabel"></div>').text(VIS.Msg.getMsg("VAS_102_IssuedFrom")));
            $left.append($('<div class="MPC-vasiu-vendName"></div>').text(na(data.WarehouseName)));

            var $contact = $('<div class="MPC-vasiu-vendContact"></div>');
            appendContactBit($contact, "user", data.IssuedBy);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="MPC-vasiu-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_InternalUseNo"), na(data.DocumentNo), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Origin"), originLabel(), false));
            // Reference: the source document the issue was raised against — the
            // work order, else the requisition; only an issue linked to neither
            // falls back to the header description.
            var srcRef = data.WorkOrderNo || requisitionLabel();
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Reference"),
                na(srcRef || data.Description), !!srcRef));
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_102_Posted")
                            : VIS.Msg.getMsg("VAS_102_NotPosted"), false));
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasiu-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasiu-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasiu-hdrField"></div>');
            $f.append($('<div class="MPC-vasiu-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasiu-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasiu-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- References (linked source documents) ---------- //

        // The requisition the issue was raised against. Several requisitions can
        // feed one issue — the first is named and the rest counted ("REQ-1 +2").
        function requisitionLabel() {
            if (!data || !data.RequisitionNo) return "";
            var extra = (data.RequisitionCount || 0) - 1;
            return extra > 0 ? (data.RequisitionNo + " +" + extra) : data.RequisitionNo;
        }

        // Where this issue came from: the work order it services (document no +
        // its own reference, read from VA075_WorkOrder_ID on the lines) and/or the
        // requisition (number, dates, who raised it, its note).
        //
        // Only links that actually exist are drawn — a manual issue used to render
        // a row of five N/A cells, which read as broken rather than as "nothing is
        // linked". That case now gets a single explanatory line instead.
        function renderReferences() {
            var items = [];

            // Work order first — it is the stronger origin when both are present.
            if (data.WorkOrderNo) {
                items.push({ icon: "wrench", link: true,
                    label: msg("VAS_102_WorkOrderNo", "Work Order"),
                    value: workOrderLabel() });
            }
            if (data.WorkOrderRef) {
                items.push({ icon: "tag",
                    label: msg("VAS_102_WorkOrderRef", "Work Order Reference"),
                    value: data.WorkOrderRef });
            }
            if (data.RequisitionNo) {
                items.push({ icon: "doc", link: true,
                    label: msg("VAS_102_RequisitionNo", "Requisition No"),
                    value: requisitionLabel() });
            }
            var reqDate = formatDate(data.RequisitionDate);
            if (reqDate) {
                items.push({ icon: "calendar",
                    label: msg("VAS_102_RequisitionDate", "Requisition Date"),
                    value: reqDate });
            }
            var dueDate = formatDate(data.DateRequired);
            if (dueDate) {
                items.push({ icon: "calendar",
                    label: msg("VAS_102_DateRequired", "Date Required"),
                    value: dueDate });
            }
            if (data.RequestedBy) {
                items.push({ icon: "user",
                    label: msg("VAS_102_RequestedBy", "Requested By"),
                    value: data.RequestedBy });
            }
            // The requisition's own note. The issue's description is not repeated
            // here — the Notes section below carries it.
            var note = (data.RequisitionNote || "").trim();
            if (note) {
                items.push({ icon: "tag",
                    label: msg("VAS_102_Reference", "Reference"),
                    value: note });
            }

            var $sec = section(msg("VAS_102_References", "References"), null);
            var $card = $('<div class="MPC-vasiu-refCard"></div>');

            if (!items.length) {
                $card.addClass("is-empty");
                var $empty = $('<div class="MPC-vasiu-refEmpty"></div>');
                $empty.append(svgIcon("link"));
                $empty.append($('<span></span>').text(
                    msg("VAS_102_NoReferences", "No linked documents — raised manually")));
                $card.append($empty);
            } else {
                for (var i = 0; i < items.length; i++) $card.append(refItem(items[i]));
            }

            $sec.append($card);
        }

        // One reference: a tinted icon bubble, the field name and its value. The
        // value ellipsises with the full text on a tooltip.
        function refItem(it) {
            var $row = $('<div class="MPC-vasiu-refItem"></div>');

            var $ico = $('<span class="MPC-vasiu-refIco"></span>');
            $ico.append(svgIcon(it.icon));
            $row.append($ico);

            var $txt = $('<div class="MPC-vasiu-refTxt"></div>');
            $txt.append($('<div class="MPC-vasiu-fLabel"></div>').text(it.label));
            var $val = $('<div class="MPC-vasiu-fVal"></div>')
                .text(it.value).attr("title", it.value);
            if (it.link) $val.addClass("is-link");
            $txt.append($val);
            $row.append($txt);

            return $row;
        }

        // The work order the issue services. As with requisitions, several can
        // feed one issue — the first is named and the rest counted ("WO-1 +2").
        function workOrderLabel() {
            if (!data || !data.WorkOrderNo) return "";
            var extra = (data.WorkOrderCount || 0) - 1;
            return extra > 0 ? (data.WorkOrderNo + " +" + extra) : data.WorkOrderNo;
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasiu-snap"></section>');
            var cur = currencyToken();

            // Total issued value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_102_TotalValue"),
                formatAmount(+data.TotalValue || 0, cur, data.StdPrecision),
                VIS.Msg.getMsg("VAS_102_IssuedValue")));

            // Quantity issued (across N lines).
            $snap.append(metricCard("issued", "box", VIS.Msg.getMsg("VAS_102_QuantityIssued"),
                formatNumber(+data.IssuedQty || 0, 0),
                VIS.Msg.getMsg("VAS_102_Across") + " " + (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_102_LinesWord")));

            // Total lines.
            $snap.append(metricCard("lines", "layers", VIS.Msg.getMsg("VAS_102_Lines"),
                (data.LineCount || 0) + "", VIS.Msg.getMsg("VAS_102_OnThisIssue")));

            // Not fully issued — the requested-minus-issued quantity still due,
            // with the number of lines it spans as the caption.
            $snap.append(metricCard("pending", "alert", VIS.Msg.getMsg("VAS_102_NotFullyIssued"),
                formatNumber(+data.NotFullQty || 0, qtyPrecision()),
                (data.NotFullCount || 0) + " " + VIS.Msg.getMsg("VAS_102_LinesWord") +
                " · " + VIS.Msg.getMsg("VAS_102_ShortOfRequest")));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasiu-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasiu-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasiu-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasiu-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasiu-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Status summary cards (Full / Partial / Short / Lines) ---------- //

        // Full and Lines are line counts; Partial and Short report the quantity
        // still to be issued/consumed on those lines (the requested-minus-issued
        // remainder), with the line count as the caption.
        function renderStatusCards() {
            var st = summariseLineStatuses();
            var prec = qtyPrecision();
            var linesWord = VIS.Msg.getMsg("VAS_102_LinesWord");
            var pendingWord = msg("VAS_102_QtyPending", "Qty pending");

            var $row = $('<section class="MPC-vasiu-status"></section>');
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Full"), st.full.count + "",
                "full", linesWord));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Partial"),
                formatNumber(st.partial.qty, prec), "partial",
                pendingWord + " · " + st.partial.count + " " + linesWord));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Short"),
                formatNumber(st.short.qty, prec), "short",
                pendingWord + " · " + st.short.count + " " + linesWord));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Lines"), (data.LineCount || 0) + "",
                "neutral", VIS.Msg.getMsg("VAS_102_OnThisIssue")));
            $body.append($row);
        }

        // Line count and outstanding quantity per status bucket.
        function summariseLineStatuses() {
            var out = {
                full:    { count: 0, qty: 0 },
                partial: { count: 0, qty: 0 },
                short:   { count: 0, qty: 0 }
            };
            var lines = (data && data.Lines) || [];
            for (var i = 0; i < lines.length; i++) {
                var s = lineStatus(+lines[i].RequestedQty || 0, +lines[i].IssuedQty || 0);
                out[s].count++;
                out[s].qty += pendingQty(lines[i]);
            }
            return out;
        }

        function statusCard(label, value, tone, sub) {
            var $c = $('<div class="MPC-vasiu-statCard"></div>').addClass("tone-" + tone);
            $c.append($('<div class="MPC-vasiu-statVal"></div>').text(value + ""));
            $c.append($('<div class="MPC-vasiu-statLbl"></div>').text(label));
            if (sub) $c.append($('<div class="MPC-vasiu-statSub"></div>').text(sub));
            return $c;
        }

        // ---------- Issue timeline (3-node stepper) ---------- //

        // Each stage captions with the moment it actually happened: when the
        // record was created, when it was completed, when posting ran. The
        // movement date is a document field, not a milestone, so it is not used
        // here — it stays on the header card.
        function renderTimeline() {
            var issued = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var stages = [
                { key: "VAS_102_Created", done: true,        date: data.CreatedDate },
                { key: "VAS_102_Issued",  done: issued,      date: data.CompletedDate },
                { key: "VAS_102_Posting", done: data.Posted, date: data.PostedDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_102_IssueTimeline"), null);

            var $tl = $('<div class="MPC-vasiu-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "is-done";
                    // A done stage with no stamp (e.g. completed outside the
                    // workflow engine) still reads as done.
                    metaText = formatDate(s.date) ||
                        (i === 2 ? VIS.Msg.getMsg("VAS_102_Posted")
                                 : VIS.Msg.getMsg("VAS_102_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "is-active";
                    metaText = VIS.Msg.getMsg("VAS_102_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_102_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasiu-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasiu-stepRail"></div>');
            $rail.append($('<span class="MPC-vasiu-stepLine MPC-vasiu-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasiu-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasiu-stepLine MPC-vasiu-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasiu-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasiu-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasiu-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Issue lines (table + pager) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var cur = currencyToken();

            var $sec = section(VIS.Msg.getMsg("VAS_102_IssueLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_102_Items")
            });

            var $tbl = $('<div class="MPC-vasiu-table"></div>');

            var $head = $('<div class="MPC-vasiu-tRow MPC-vasiu-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Locator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Requested")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Issued")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Available")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Value")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_102_Status")));
            $tbl.append($head);

            // Totals footer — always the whole issue, never just the page.
            var $foot = $('<div class="MPC-vasiu-tFoot"></div>');
            var $bit = $('<span class="MPC-vasiu-tf is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_102_TotalIssuedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.TotalValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);

            // The pager sits outside the table: the table gets its own horizontal
            // scroll on narrow panels, and the controls must not scroll away with
            // the columns.
            var $pager = $('<div class="MPC-vasiu-pager"></div>');
            if (lines.length > LINES_PER_PAGE) $sec.append($pager);

            // Rows are replaced in place, ahead of the totals footer, so the
            // table's structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * LINES_PER_PAGE;
                var end = Math.min(lines.length, start + LINES_PER_PAGE);

                $tbl.find(".MPC-vasiu-tBody").remove();
                for (var i = start; i < end; i++) {
                    var ln = lines[i];
                    $foot.before(buildLineRow(
                        ln, lineStatus(+ln.RequestedQty || 0, +ln.IssuedQty || 0), cur));
                }

                buildPager($pager, lines.length, pageCount, start, end, paintPage);
            }

            paintPage();
        }

        // Renders the pager into $pager: a range caption on the left, Previous /
        // page-of / Next on the right. Rebuilt on every page change so the
        // disabled states stay accurate.
        function buildPager($pager, total, pageCount, start, end, onChange) {
            $pager.empty();
            if (total <= LINES_PER_PAGE) return;

            $pager.append($('<span class="MPC-vasiu-pgRange"></span>').text(
                msg("VAS_102_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("VAS_102_Of", "of") + " " + total));

            var $ctrls = $('<span class="MPC-vasiu-pgCtrls"></span>');

            $ctrls.append(pagerButton(msg("VAS_102_Previous", "Previous"), "chevLeft",
                linesPage <= 0, function () { linesPage--; onChange(); }));

            $ctrls.append($('<span class="MPC-vasiu-pgPos"></span>').text(
                msg("VAS_102_Page", "Page") + " " + (linesPage + 1) + " " +
                msg("VAS_102_Of", "of") + " " + pageCount));

            $ctrls.append(pagerButton(msg("VAS_102_Next", "Next"), "chevRight",
                linesPage >= pageCount - 1, function () { linesPage++; onChange(); }));

            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="MPC-vasiu-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) {
                $b.addClass("is-disabled");
            } else {
                $b.on("click", handler);
            }
            return $b;
        }

        function buildLineRow(ln, st, cur) {
            var $tr = $('<div class="MPC-vasiu-tRow MPC-vasiu-tBody"></div>');

            // Item: name, product search key, attribute set instance. The name
            // cell ellipsises, so the full product name goes on a hover tooltip —
            // it leaves the layout untouched.
            var $item = $('<span class="MPC-vasiu-itItem"></span>');
            var $name = $('<div class="MPC-vasiu-itName"></div>').text(na(ln.ProductName));
            if (ln.ProductName) $name.attr("title", ln.ProductName);
            $item.append($name);

            if (ln.ProductCode) {
                // The search key alone — no "SKU" prefix.
                $item.append($('<div class="MPC-vasiu-itSku"></div>').text(ln.ProductCode));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasiu-itSku"></div>').text(ln.Description));
            }

            // Lot / serial / attributes, only for a product that carries them.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi) {
                $item.append($('<div class="MPC-vasiu-itAttr"></div>').text(asi).attr("title", asi));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // Locator
            $tr.append($('<span></span>').text(na(ln.LocatorName)));

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Requested
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.RequestedQty || 0, prec)));

            // Issued
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.IssuedQty || 0, prec)));

            // Available
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.AvailableQty || 0, prec)));

            // Value
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag
            var tagKey = st === "full" ? "VAS_102_Full"
                       : (st === "partial" ? "VAS_102_Partial" : "VAS_102_Short");
            var $q = $('<span class="ta-c"></span>');
            $q.append($('<span class="MPC-vasiu-tag"></span>').addClass("s-" + st)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Notes (issue header description) ---------- //

        // The description typed on the Inventory Use header. Skipped when blank so
        // an empty card never trails the panel.
        function renderNotes() {
            var text = (data.Description || "").trim();
            if (!text) return;

            var $sec = section(msg("VAS_102_Notes", "Notes"), null);
            var $card = $('<div class="MPC-vasiu-textCard"></div>');
            $card.append($('<p></p>').text(text));
            $sec.append($card);
        }

        // ---------- Activity (audit trail) ---------- //

        // Activity type -> tag label + tone + icon, and the sentence shown for it.
        var ACT_TYPES = {
            created:   { tone: "neutral", icon: "doc",    tagKey: "VAS_102_TagCreated",   tagText: "Created",   titleKey: "VAS_102_ActCreated",   titleText: "Inventory use created" },
            updated:   { tone: "info",    icon: "pencil", tagKey: "VAS_102_TagUpdated",   tagText: "Updated",   titleKey: "VAS_102_ActUpdated",   titleText: "Inventory use updated" },
            completed: { tone: "success", icon: "check",  tagKey: "VAS_102_TagCompleted", tagText: "Completed", titleKey: "VAS_102_ActCompleted", titleText: "Inventory use completed" },
            posted:    { tone: "purple",  icon: "coins",  tagKey: "VAS_102_TagPosted",    tagText: "Posted",    titleKey: "VAS_102_ActPosted",    titleText: "Posted to accounting" },
            note:      { tone: "neutral", icon: "mail",   tagKey: "VAS_102_TagNote",      tagText: "Note",      titleKey: null,                   titleText: "" }
        };

        // The issue's audit trail, newest first: who created it, who changed it and
        // when, when it was completed and posted, plus any notes logged against it.
        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_102_Activity", "Activity"), {
                summary: rows.length + " " + msg("VAS_102_Updates", "updates")
            });

            var $list = $('<div class="MPC-vasiu-actList"></div>');
            for (var i = 0; i < rows.length; i++) {
                $list.append(activityRow(rows[i]));
            }
            $sec.append($list);
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="MPC-vasiu-actRow"></div>');

            var $tag = $('<span class="MPC-vasiu-actTag"></span>').addClass("tone-" + meta.tone);
            $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.tagKey, meta.tagText)));
            $row.append($tag);

            // For a note the title is the note text itself; for everything else it
            // is the event sentence. A tooltip keeps a long line readable once the
            // cell ellipsises.
            var title = activityTitle(a, meta);
            $row.append($('<span class="MPC-vasiu-actTitle"></span>')
                .text(title).attr("title", title));

            // "when · by whom" — the audit trail's whole point.
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when
                    ? when + " · " + msg("VAS_102_By", "by") + " " + a.UserName
                    : msg("VAS_102_By", "by") + " " + a.UserName;
            }
            $row.append($('<span class="MPC-vasiu-actWhen"></span>').text(when).attr("title", when));

            return $row;
        }

        function activityTitle(a, meta) {
            if (a.Type === "note") return (a.Text || "").trim();
            var title = meta.titleKey ? msg(meta.titleKey, meta.titleText) : (meta.titleText || "");
            if (a.DocumentNo) title += " — " + a.DocumentNo;
            return title;
        }

        // Issue Stock / Post Inventory are not repeated here — both actions are
        // available on the window's header panel.

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            calendar: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            alert:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            layers:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            pencil:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            chevLeft: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight:'<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>',
            wrench:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76Z"/></svg>',
            tag:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12.6 2.6a2 2 0 0 0-1.4-.6H4a2 2 0 0 0-2 2v7.2a2 2 0 0 0 .6 1.4l8.2 8.2a2 2 0 0 0 2.8 0l7.2-7.2a2 2 0 0 0 0-2.8Z"/><circle cx="6.5" cy="6.5" r="1.5"/></svg>',
            link:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasiu-ic"></span>');
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

        // Date + time — the audit trail needs the moment, not just the day.
        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                }) + " " + d.toLocaleTimeString(window.navigator.language, {
                    hour: "2-digit", minute: "2-digit"
                });
            } catch (e) {
                return d.toString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_102_OverviewInternalUse.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_102_OverviewInternalUse.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_102_OverviewInternalUse.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_102_OverviewInternalUse.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
