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
 *   VAI163   2026-08-03  - Activity shows the e-mails sent against the issue
 *                          (type "email"): the subject headlines the row, the
 *                          recipient runs underneath it and the timestamp /
 *                          sender sit where every other entry carries them. The
 *                          message body opens on click, headed by the full
 *                          From / To / Cc / Bcc set.
 *   VAI163   2026-08-05  - Issue lines page at 25 rows instead of 10, matching
 *                          the Purchase Order overview; the pager only appears
 *                          once an issue exceeds that.
 *                        - Notes shows every description entered against the
 *                          issue: the header's, then the one typed on each
 *                          line's child tab (payload Notes collection).
 *                        - No description is shown as a Reference any more. The
 *                          header card's Reference field no longer falls back to
 *                          the issue description, and the References section
 *                          drops the requisition-note row — both put free text
 *                          where the reader expects a document identifier.
 *   VAI163   2026-08-05  - Timestamps render in the viewer's local system zone.
 *                          The DB stores them in UTC and the server emits no
 *                          zone designator, so the browser was reading them as
 *                          local and the Activity feed showed the stored UTC
 *                          clock. parseDbDate tags a bare timestamp "Z"; a
 *                          date-only field is still parsed as-is so its calendar
 *                          day can never roll over.
 *                        - A long locator name ellipsises in its column and
 *                          carries the full name on a hover tooltip, so the row
 *                          layout is unchanged.
 *                        - Completed now reads green (tone success) like Closed.
 *                        - Issue Timeline's third stage is named "Posted".
 *                        - The details card drops its Posted field — the header
 *                          pill and the timeline already carry it.
 *   VAI163   2026-08-05  - The References card is now a Reference chip strip
 *                          built like the Purchase Order overview's Generated
 *                          From: same placement (directly under the header
 *                          card), same chip design, and every chip opens its
 *                          source record through the shared openRecord() zoom
 *                          path. An issue linked to nothing reads "Manual
 *                          Issue" instead of "no linked documents".
 *                          The requisition's dates and requester move onto the
 *                          chip's tooltip — the strip lists documents only.
 *                        - Line items show the Attribute Set Instance directly
 *                          under the product name, above the search key.
 *                        - The details card drops Origin and Reference (both
 *                          are in the chip strip now) and shows Warehouse where
 *                          Reference used to be.
 *   VAI163   2026-08-05  - Removed the Full / Partial / Short / Lines status
 *                          cards. The KPI snapshot already reports the issued
 *                          and outstanding quantities and the line count, and
 *                          every row carries its own status tag.
 *                        - Header pills run origin, status, then Posted — the
 *                          order the issue moves through. Posted used to
 *                          precede the status pill.
 *                        - Quantities and rates are reported in each line's
 *                          SELECTED UOM (model side), so a line keyed in mL
 *                          reads in mL against an mL rate. The line's value is
 *                          unchanged by the restatement.
 *                        - A production order origin (VAMFG_M_WorkOrder_ID)
 *                          gets its own clickable chip labelled "Production
 *                          Order"; it used to borrow the VA075 service work
 *                          order's field and read "Work Order".
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasiu- -> vas_102- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-06  Activity paginates at 15 rows a page (ACTIVITY_PER_PAGE),
 *                        reusing the lines pager. buildPager() is no longer tied to
 *                        linesPage / LINES_PER_PAGE: it takes the 0-based page and
 *                        an onGo(page) callback, so the lines table and the activity
 *                        feed page independently. A feed that fits on one page shows
 *                        no controls, and the section summary keeps counting the
 *                        whole feed. Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_102-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_102-tone-" + tone).
 *   VAI163   2026-08-11  The Reference strip gains a Project chip, drawn when the
 *                        issue carries C_Project_ID: the project's search key on
 *                        the chip, its name on the tooltip, and a click opens the
 *                        project record through the same openRecord() zoom path
 *                        as every other chip. An issue raised against a project
 *                        and nothing else used to read "Manual Issue". The header
 *                        pill reads "Project" for that origin (ORIGIN_MAP).
 *   VAI163   2026-08-11  The Production Order chip opens VAMFG_ProductionOrder
 *                        and the Project chip VAS_Project, both named in
 *                        WINDOW_NAME_BY_TABLE. Neither table's zoom target
 *                        resolves to a window, so both fell through to the
 *                        "Cannot open" toast on every click.
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

    VAS.VAS_102_OverviewInternalUse = function () {
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

        // Issue lines are paged client-side (the whole set arrives in one
        // payload). Page index resets whenever a different record is loaded.
        // 25 rows per page, matching the Purchase Order overview's line items:
        // the pager only appears once an issue actually exceeds that.
        var LINES_PER_PAGE = 25;
        var linesPage = 0;
        var activityPage = 0;   // current Activity page (0-based, like linesPage)

        this.init = function () {
            $root = $('<div class="vas_102-root"></div>');
            $body = $('<div class="vas_102-body"></div>');
            $emptyState = $('<div class="vas_102-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_102_NoData"));
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
                    activityPage = 0;
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
            activityPage = 0;
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
            var $sec = $('<section class="vas_102-sec"></section>');
            var $head = $('<div class="vas_102-secHead"></div>');
            $head.append($('<h2 class="vas_102-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_102-secSummary"></span>').text(opts.summary));
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

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_102_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_102_InProgress",          tone: "info" },
            "AP": { key: "VAS_102_Approved",            tone: "info" },
            // Completed is a good outcome, so it reads green like Closed rather
            // than the neutral blue an informational state gets.
            "CO": { key: "VAS_102_Completed",           tone: "success" },
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
            "PROJECT":     { key: "VAS_102_Project",         def: "Project" },
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

            var $strip = $('<section class="vas_102-hdr"></section>');
            var $top = $('<div class="vas_102-hdrTop"></div>');

            var $tl = $('<div class="vas_102-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_102-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_102_InternalUse") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_102_MovementDate") + " " + moved);
            if (data.IssuedBy) subBits.push(VIS.Msg.getMsg("VAS_102_IssuedBy") + " " + data.IssuedBy);
            if (subBits.length) {
                $tl.append($('<div class="vas_102-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            // Origin, then the document status, then Posted — the order the issue
            // actually moves through. Posted used to sit ahead of the status pill,
            // which read as though posting came before completion.
            var $pills = $('<div class="vas_102-hdrPills"></div>');
            $pills.append(headerPill(originLabel(), "info", "layers", false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_102_Posted"), "success", "check", false));
            }
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: warehouse identity (left) + document fields (right) ---
            var $card = $('<section class="vas_102-hdrCard"></section>');

            var $left = $('<div class="vas_102-hdrColL"></div>');
            $left.append($('<div class="vas_102-fLabel"></div>').text(VIS.Msg.getMsg("VAS_102_IssuedFrom")));
            $left.append($('<div class="vas_102-vendName"></div>').text(na(data.WarehouseName)));

            var $contact = $('<div class="vas_102-vendContact"></div>');
            appendContactBit($contact, "user", data.IssuedBy);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="vas_102-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_InternalUseNo"), na(data.DocumentNo), false));
            // Origin and Reference are deliberately absent: both now live in the
            // Reference chip strip under this card, which names every source
            // document and opens it on click. Repeating them as flat fields said
            // the same thing twice, and less usefully.
            $right.append(headerField(msg("VAS_102_Warehouse", "Warehouse"),
                na(data.WarehouseName), false));
            // Posted is deliberately not a field here: the header pill already
            // carries it and the Issue Timeline dates it, so a third copy on the
            // details card only repeated what was on screen twice over.
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_102-hdrPill"></span>')
                .addClass("vas_102-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_102-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_102-hdrField"></div>');
            $f.append($('<div class="vas_102-fLabel"></div>').text(label));
            var $v = $('<div class="vas_102-fVal"></div>').text(value);
            if (link) $v.addClass("vas_102-is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_102-contactBit"></span>');
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

        // Where this issue came from, in the Purchase Order overview's "Generated
        // From" shape: a labelled strip of chips directly under the header card,
        // one per source document, each opening that record on click.
        //
        // Only origins that exist are drawn. An issue linked to none of them was
        // raised by hand and says so — "Manual Issue" is a chip like any other,
        // not an apology for an empty card.
        function renderReferences() {
            var $strip = $('<section class="vas_102-genfrom"></section>');
            $strip.append($('<span class="vas_102-gfLabel"></span>')
                .text(msg("VAS_102_Reference", "Reference")));

            var $chips = $('<div class="vas_102-gfChips"></div>');
            var any = false;

            // Work order first — it is the stronger origin when both are present.
            // Its own reference rides along as a trailing pill, the way the PO
            // panel marks a requisition reached "via RFQ".
            if (data.WorkOrderNo) {
                $chips.append(originChip("wrench",
                    msg("VAS_102_WorkOrder", "Work Order"), workOrderLabel(),
                    data.WorkOrderRef ? chipPill(data.WorkOrderRef, "neutral") : null,
                    "info", "VA075_WorkOrder", data.VA075_WorkOrder_ID, null));
                any = true;
            }

            // Production order (M_InventoryLine.VAMFG_M_WorkOrder_ID) — a
            // manufacturing document, NOT the VA075 service work order above.
            // The two used to share one field on the payload, so an issue raised
            // against a production order was labelled "Work Order".
            if (data.ProductionOrderNo) {
                $chips.append(originChip("factory",
                    msg("VAS_102_ProductionOrder", "Production Order"),
                    productionOrderLabel(), null, "warning",
                    "VAMFG_M_WorkOrder", data.VAMFG_M_WorkOrder_ID, null));
                any = true;
            }

            if (data.RequisitionNo) {
                $chips.append(originChip("doc",
                    msg("VAS_102_Requisition", "Requisition"), requisitionLabel(),
                    null, "success", "M_Requisition", data.M_Requisition_ID,
                    requisitionTooltip()));
                any = true;
            }

            // The project the issue was raised for (M_Inventory.C_Project_ID).
            // Its search key identifies it on the chip and its name sits on the
            // tooltip, the way the requisition's detail does. An issue carrying
            // only a project used to read "Manual Issue".
            if (data.C_Project_ID > 0) {
                $chips.append(originChip("folder",
                    msg("VAS_102_Project", "Project"), projectLabel(), null,
                    "purple", "C_Project", data.C_Project_ID, projectTooltip()));
                any = true;
            }

            if (!any) {
                $chips.append(originChip("pencil",
                    msg("VAS_102_ManualIssue", "Manual Issue"), null, null,
                    "muted", null, 0, null));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // Context for the requisition link — its dates and who raised it. Kept on
        // the chip's tooltip rather than given chips of their own: the strip is a
        // row of source DOCUMENTS, and a date is not one.
        function requisitionTooltip() {
            var bits = [];
            var reqDate = formatDate(data.RequisitionDate);
            if (reqDate) {
                bits.push(msg("VAS_102_RequisitionDate", "Requisition Date") + ": " + reqDate);
            }
            var dueDate = formatDate(data.DateRequired);
            if (dueDate) {
                bits.push(msg("VAS_102_DateRequired", "Date Required") + ": " + dueDate);
            }
            if (data.RequestedBy) {
                bits.push(msg("VAS_102_RequestedBy", "Requested By") + ": " + data.RequestedBy);
            }
            return bits.join("\n");
        }

        // Origin chip: leading (tinted) icon + grey label + dark value, with an
        // optional trailing pill. Given a table and a record id it becomes a link
        // that opens that record, marked with a trailing arrow.
        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId, tooltip) {
            var $chip = $('<span class="vas_102-chip"></span>')
                .addClass("vas_102-ic-" + (iconTone || "muted"));

            var isLink = tableName && recordId && +recordId > 0;
            if (isLink) {
                $chip.addClass("vas_102-is-link")
                    .attr("data-open-table", tableName)
                    .attr("data-open-id", recordId);
            }

            $chip.append(svgIcon(icon));
            $chip.append($('<span class="vas_102-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="vas_102-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            if (isLink) $chip.append(svgIcon("arrowUpRight"));
            if (tooltip) $chip.attr("title", tooltip);
            return $chip;
        }

        function chipPill(text, tone) {
            return $('<span class="vas_102-chipPill"></span>')
                .addClass("vas_102-tone-" + (tone || "neutral")).text(text);
        }

        // The work order the issue services. As with requisitions, several can
        // feed one issue — the first is named and the rest counted ("WO-1 +2").
        function workOrderLabel() {
            if (!data || !data.WorkOrderNo) return "";
            var extra = (data.WorkOrderCount || 0) - 1;
            return extra > 0 ? (data.WorkOrderNo + " +" + extra) : data.WorkOrderNo;
        }

        // The project chip's value: the project's search key, falling back to its
        // name when the key is blank, so the chip is never a bare label.
        function projectLabel() {
            if (!data) return "";
            return (data.ProjectNo || "").trim() || (data.ProjectName || "").trim();
        }

        // The project's name, for the chip's tooltip — the strip itself lists the
        // identifier, as it does for every other document.
        function projectTooltip() {
            var name = (data && data.ProjectName || "").trim();
            if (!name || name === projectLabel()) return "";
            return msg("VAS_102_Project", "Project") + ": " + name;
        }

        // The production order the issue consumed material for, counted the same
        // way when more than one feeds the issue.
        function productionOrderLabel() {
            if (!data || !data.ProductionOrderNo) return "";
            var extra = (data.ProductionOrderCount || 0) - 1;
            return extra > 0 ? (data.ProductionOrderNo + " +" + extra) : data.ProductionOrderNo;
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_102-snap"></section>');
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
            var $c = $('<div class="vas_102-metric"></div>').addClass("vas_102-tone-" + tone);

            var $head = $('<div class="vas_102-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_102-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_102-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_102-mSub"></div>').text(sub));
            return $c;
        }

        // The Full / Partial / Short / Lines summary cards that used to sit here
        // are gone. Every figure they carried is already on screen: the KPI
        // snapshot above reports the issued and outstanding quantities and the
        // line count, and each row carries its own Full / Partial / Short tag.
        // The row restated all of it a second time, in a second shape.

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
                // "Posted", not "Posting": the stage names the milestone the way
                // the rest of the panel does, not the activity that reaches it.
                { key: "VAS_102_Posted",  done: data.Posted, date: data.PostedDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_102_IssueTimeline"), null);

            var $tl = $('<div class="vas_102-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "vas_102-is-done";
                    // A done stage with no stamp (e.g. completed outside the
                    // workflow engine) still reads as done.
                    metaText = formatDate(s.date) ||
                        (i === 2 ? VIS.Msg.getMsg("VAS_102_Posted")
                                 : VIS.Msg.getMsg("VAS_102_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "vas_102-is-active";
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
            var $entry = $('<div class="vas_102-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_102-stepRail"></div>');
            $rail.append($('<span class="vas_102-stepLine vas_102-stepLine-l"></span>'));
            var $dot = $('<span class="vas_102-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="vas_102-stepLine vas_102-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_102-stepLabel"></div>');
            $lbl.append($('<div class="vas_102-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_102-stepMeta"></div>').text(meta));
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

            var $tbl = $('<div class="vas_102-table"></div>');

            var $head = $('<div class="vas_102-tRow vas_102-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Locator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_UOM")));
            $head.append($('<span class="vas_102-ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Requested")));
            $head.append($('<span class="vas_102-ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Issued")));
            $head.append($('<span class="vas_102-ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Available")));
            $head.append($('<span class="vas_102-ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Value")));
            $head.append($('<span class="vas_102-ta-c"></span>').text(VIS.Msg.getMsg("VAS_102_Status")));
            $tbl.append($head);

            // Totals footer — always the whole issue, never just the page.
            var $foot = $('<div class="vas_102-tFoot"></div>');
            var $bit = $('<span class="vas_102-tf vas_102-is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_102_TotalIssuedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.TotalValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);

            // The pager sits outside the table: the table gets its own horizontal
            // scroll on narrow panels, and the controls must not scroll away with
            // the columns.
            var $pager = $('<div class="vas_102-pager"></div>');
            if (lines.length > LINES_PER_PAGE) $sec.append($pager);

            // Rows are replaced in place, ahead of the totals footer, so the
            // table's structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * LINES_PER_PAGE;
                var end = Math.min(lines.length, start + LINES_PER_PAGE);

                $tbl.find(".vas_102-tBody").remove();
                for (var i = start; i < end; i++) {
                    var ln = lines[i];
                    $foot.before(buildLineRow(
                        ln, lineStatus(+ln.RequestedQty || 0, +ln.IssuedQty || 0), cur));
                }

                buildPager($pager, linesPage, pageCount, lines.length, start, end,
                    function (p) { linesPage = p; paintPage(); });
            }

            paintPage();
        }

        // Renders the pager into $pager: a range caption on the left, Previous /
        // page-of / Next on the right. Rebuilt on every page change so the
        // disabled states stay accurate.
        //
        // `page` is the 0-based page being shown and `onGo` is handed the page to
        // move to, so each paged section owns its own page variable — the lines
        // table and the activity feed page independently of one another. Nothing
        // is drawn for a single-page list, so a short section shows no controls.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_102-pgRange"></span>').text(
                msg("VAS_102_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("VAS_102_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_102-pgCtrls"></span>');

            $ctrls.append(pagerButton(msg("VAS_102_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));

            $ctrls.append($('<span class="vas_102-pgPos"></span>').text(
                msg("VAS_102_Page", "Page") + " " + (page + 1) + " " +
                msg("VAS_102_Of", "of") + " " + pageCount));

            $ctrls.append(pagerButton(msg("VAS_102_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));

            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_102-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) {
                $b.addClass("vas_102-is-disabled");
            } else {
                $b.on("click", handler);
            }
            return $b;
        }

        function buildLineRow(ln, st, cur) {
            var $tr = $('<div class="vas_102-tRow vas_102-tBody"></div>');

            // Item: name, product search key, attribute set instance. The name
            // cell ellipsises, so the full product name goes on a hover tooltip —
            // it leaves the layout untouched.
            var $item = $('<span class="vas_102-itItem"></span>');
            var $name = $('<div class="vas_102-itName"></div>').text(na(ln.ProductName));
            if (ln.ProductName) $name.attr("title", ln.ProductName);
            $item.append($name);

            // Lot / serial / attributes sit directly under the product name — the
            // attribute qualifies WHICH stock was issued, so it belongs with the
            // name rather than below the search key.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi) {
                $item.append($('<div class="vas_102-itAttr"></div>').text(asi).attr("title", asi));
            }

            if (ln.ProductCode) {
                // The search key alone — no "SKU" prefix.
                $item.append($('<div class="vas_102-itSku"></div>').text(ln.ProductCode));
            } else if (ln.Description) {
                $item.append($('<div class="vas_102-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // Locator. The cell ellipsises on a narrow panel, so the full locator
            // name goes on a hover tooltip — the reader gets all of it without the
            // column widening and pushing the layout around.
            var $loc = $('<span class="vas_102-itLoc"></span>').text(na(ln.LocatorName));
            if (ln.LocatorName) $loc.attr("title", ln.LocatorName);
            $tr.append($loc);

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Requested
            $tr.append($('<span class="vas_102-ta-r"></span>').text(formatNumber(+ln.RequestedQty || 0, prec)));

            // Issued
            $tr.append($('<span class="vas_102-ta-r"></span>').text(formatNumber(+ln.IssuedQty || 0, prec)));

            // Available
            $tr.append($('<span class="vas_102-ta-r"></span>').text(formatNumber(+ln.AvailableQty || 0, prec)));

            // Value
            $tr.append($('<span class="vas_102-ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag
            var tagKey = st === "full" ? "VAS_102_Full"
                       : (st === "partial" ? "VAS_102_Partial" : "VAS_102_Short");
            var $q = $('<span class="vas_102-ta-c"></span>');
            $q.append($('<span class="vas_102-tag"></span>').addClass("vas_102-s-" + st)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Notes (issue header + line descriptions) ---------- //

        // Every description entered against the issue: the one typed on the
        // Inventory Use header, then the one typed on each line's child tab
        // (M_InventoryLine.Description), each already labelled server-side with its
        // line no and product. Skipped entirely when nothing was written, so an
        // empty card never trails the panel.
        //
        // Falls back to the header description alone when the payload predates the
        // Notes collection, so an older server never blanks the section.
        function renderNotes() {
            var notes = collectNotes();
            if (!notes.length) return;

            var $sec = section(msg("VAS_102_Notes", "Notes"), {
                summary: notes.length + " " + msg("VAS_102_NotesCount", "notes")
            });
            var $card = $('<div class="vas_102-textCard"></div>');
            for (var i = 0; i < notes.length; i++) {
                $card.append($('<p></p>').text(notes[i]));
            }
            $sec.append($card);
        }

        function collectNotes() {
            var out = [];
            var rows = (data && data.Notes) || null;
            if (rows && rows.length) {
                for (var i = 0; i < rows.length; i++) {
                    var t = (rows[i].Text || "").trim();
                    if (t) out.push(t);
                }
                return out;
            }
            var header = (data.Description || "").trim();
            if (header) out.push(header);
            return out;
        }

        // ---------- Activity (audit trail) ---------- //

        // Activity type -> tag label + tone + icon, and the sentence shown for it.
        var ACT_TYPES = {
            created:   { tone: "neutral", icon: "doc",    tagKey: "VAS_102_TagCreated",   tagText: "Created",   titleKey: "VAS_102_ActCreated",   titleText: "Inventory use created" },
            updated:   { tone: "info",    icon: "pencil", tagKey: "VAS_102_TagUpdated",   tagText: "Updated",   titleKey: "VAS_102_ActUpdated",   titleText: "Inventory use updated" },
            completed: { tone: "success", icon: "check",  tagKey: "VAS_102_TagCompleted", tagText: "Completed", titleKey: "VAS_102_ActCompleted", titleText: "Inventory use completed" },
            posted:    { tone: "purple",  icon: "coins",  tagKey: "VAS_102_TagPosted",    tagText: "Posted",    titleKey: "VAS_102_ActPosted",    titleText: "Posted to accounting" },
            note:      { tone: "neutral", icon: "mail",   tagKey: "VAS_102_TagNote",      tagText: "Note",      titleKey: null,                   titleText: "" },
            email:     { tone: "purple",  icon: "mail",   tagKey: "VAS_102_TagEmail",     tagText: "Email",     titleKey: null,                   titleText: "" }
        };

        // The issue's audit trail, newest first: who created it, who changed it and
        // when, when it was completed and posted, plus any notes and e-mails logged
        // against it.
        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A document accumulates every mail, status change and linked record, and
        // an unpaged feed made the panel scroll past everything below it. The
        // section summary still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_102_Activity", "Activity"), {
                summary: rows.length + " " + msg("VAS_102_Updates", "updates")
            });

            var $list = $('<div class="vas_102-actList"></div>');
            $sec.append($list);

            // The pager is a sibling of the list card, exactly as the lines pager
            // is a sibling of its table.
            var $pager = $('<div class="vas_102-pager"></div>');
            if (rows.length > ACTIVITY_PER_PAGE) $sec.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(rows.length / ACTIVITY_PER_PAGE));
                if (activityPage >= pageCount) activityPage = pageCount - 1;
                if (activityPage < 0) activityPage = 0;

                var start = activityPage * ACTIVITY_PER_PAGE;
                var end = Math.min(rows.length, start + ACTIVITY_PER_PAGE);

                $list.empty();
                for (var i = start; i < end; i++) {
                    $list.append(activityRow(rows[i]));
                    // An e-mail's body is heavy — it stays collapsed under its row
                    // and opens only when the reader asks for it.
                    var $mail = activityBody(rows[i]);
                    if ($mail) $list.append($mail);
                }

                buildPager($pager, activityPage, pageCount, rows.length, start, end,
                    function (p) { activityPage = p; paintPage(); });
            }

            paintPage();
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="vas_102-actRow"></div>');

            var $tag = $('<span class="vas_102-actTag"></span>').addClass("vas_102-tone-" + meta.tone);
            $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.tagKey, meta.tagText)));
            $row.append($tag);

            // For a note the title is the note text itself, for an e-mail its
            // subject; for everything else it is the event sentence. A tooltip
            // keeps a long line readable once the cell ellipsises.
            var title = activityTitle(a, meta);
            var $title = $('<span class="vas_102-actTitle"></span>');
            $title.append($('<span class="vas_102-actLead"></span>')
                .text(title).attr("title", title));

            // An e-mail names its recipients under the subject: the To list, plus
            // a count of the Cc / Bcc addresses so a reader can see at a glance
            // that others were copied. Every address itself is listed in the body.
            if (a.Type === "email") {
                var to = recipientSummary(a);
                if (to) {
                    $title.append($('<small class="vas_102-actSub"></small>')
                        .text(to).attr("title", allRecipients(a) || to));
                }
            }
            $row.append($title);

            // "when · by whom" — the audit trail's whole point. For an e-mail that
            // is when it went out and who sent it.
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when
                    ? when + " · " + msg("VAS_102_By", "by") + " " + a.UserName
                    : msg("VAS_102_By", "by") + " " + a.UserName;
            }
            $row.append($('<span class="vas_102-actWhen"></span>').text(when).attr("title", when));

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                $row.addClass("vas_102-is-openable");
                $row.attr("title", msg("VAS_102_ShowMailBody", "Click to read the message"));
                $row.append($('<span class="vas_102-actCaret"></span>').append(svgIcon("chevRight")));
                $row.on("click", function () {
                    var $panel = $row.next(".vas_102-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_102-is-open");
                    $row.toggleClass("vas_102-is-open", nowOpen)
                        .attr("title", nowOpen ? msg("VAS_102_HideMailBody", "Click to hide the message")
                                               : msg("VAS_102_ShowMailBody", "Click to read the message"));
                    $panel.toggle(nowOpen);
                });
            }

            return $row;
        }

        function activityTitle(a, meta) {
            if (a.Type === "note") return (a.Text || "").trim();
            if (a.Type === "email") {
                return (a.Text || "").trim() || msg("VAS_102_NoSubject", "(no subject)");
            }
            var title = meta.titleKey ? msg(meta.titleKey, meta.titleText) : (meta.titleText || "");
            if (a.DocumentNo) title += " — " + a.DocumentNo;
            return title;
        }

        // Only an e-mail carries a body worth opening; a mail stored without one
        // stays a plain, non-clickable row.
        function hasActivityBody(a) {
            return a && a.Type === "email" && !!(a.Body && String(a.Body).trim());
        }

        // The e-mail body, collapsed beneath its activity row. The full recipient
        // set (From / To / Cc / Bcc) heads it, so every address the mail went to is
        // on screen once the reader opens the message.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_102-actBody" style="display:none;"></div>');
            appendMailMeta($panel, "VAS_102_MailFrom", "From:", a.MailFrom);
            appendMailMeta($panel, "VAS_102_MailTo",   "To:",   a.MailTo);
            appendMailMeta($panel, "VAS_102_MailCc",   "Cc:",   a.MailCc);
            appendMailMeta($panel, "VAS_102_MailBcc",  "Bcc:",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        function appendMailMeta($panel, key, fallback, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_102-actMeta"></div>')
                .text(msg(key, fallback) + " " + String(value).trim()));
        }

        // Row sub-line: the To list, plus "+n more" covering the Cc / Bcc
        // addresses. Counting by comma / semicolon is enough for a summary — the
        // body lists the addresses verbatim.
        function recipientSummary(a) {
            var to = (a.MailTo || "").trim();
            var extra = countAddresses(a.MailCc) + countAddresses(a.MailBcc);
            if (!to && !extra) return "";
            var s = msg("VAS_102_MailTo", "To:") + " " + (to || VIS.Msg.getMsg("VAS_102_NA"));
            if (extra > 0) s += " +" + extra + " " + msg("VAS_102_MoreRecipients", "more");
            return s;
        }

        // Every address on the mail, for the row's hover tooltip.
        function allRecipients(a) {
            var bits = [];
            if (a.MailTo)  bits.push(msg("VAS_102_MailTo",  "To:")  + " " + a.MailTo);
            if (a.MailCc)  bits.push(msg("VAS_102_MailCc",  "Cc:")  + " " + a.MailCc);
            if (a.MailBcc) bits.push(msg("VAS_102_MailBcc", "Bcc:") + " " + a.MailBcc);
            return bits.join("\n");
        }

        function countAddresses(value) {
            if (!value || !String(value).trim()) return 0;
            var parts = String(value).split(/[;,]/);
            var n = 0;
            for (var i = 0; i < parts.length; i++) {
                if (parts[i].trim()) n++;
            }
            return n;
        }

        // Issue Stock / Post Inventory are not repeated here — both actions are
        // available on the window's header panel.

        // ----------------------------------------------------------------- //
        //  Events / record navigation                                        //
        // ----------------------------------------------------------------- //

        // Delegated, so chips rebuilt on every render stay clickable without
        // being re-bound.
        function bindEvents() {
            $root.on("click", ".vas_102-chip.vas_102-is-link, .vas_102-is-link[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"));
            });
        }

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does open. Anything absent here
        // falls through to the table's zoom target, which is what VA075_WorkOrder
        // relies on — that module ships its own window and is not part of this
        // solution, so its name cannot be hard-coded here.
        //
        // VAMFG_M_WorkOrder and C_Project are named because their zoom target
        // does not resolve: the production order chip reported "Cannot open"
        // on every click, which is what that fallback failing looks like.
        // Any further screen that needs naming belongs here — nothing else has
        // to change.
        var WINDOW_NAME_BY_TABLE = {
            "M_Requisition":     "VAS_Requisition",
            "VAMFG_M_WorkOrder": "VAMFG_ProductionOrder",
            "C_Project":         "VAS_Project"
        };

        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click.
        var windowIdByName = {};

        // Resolves a window id from its name through the panel's own endpoint.
        // Returns 0 when it cannot be resolved, which leaves openRecord() to fall
        // back to the table's zoom target.
        function resolveWindowIdByName(windowName) {
            if (!windowName) return 0;
            if (windowIdByName.hasOwnProperty(windowName)) {
                return windowIdByName[windowName] > 0 ? windowIdByName[windowName] : 0;
            }
            try {
                if (!(window.VIS && VIS.dataContext &&
                      typeof VIS.dataContext.getJSONRecord === "function")) {
                    return 0;
                }
                var id = VIS.dataContext.getJSONRecord(
                    "VAS_102_OverviewInternalUse/GetWindow_ID", windowName);
                id = parseInt(id, 10);
                if (isNaN(id) || id <= 0) {
                    windowIdByName[windowName] = -1;
                    console.log("resolveWindowIdByName: no window named " + windowName);
                    return 0;
                }
                windowIdByName[windowName] = id;
                return id;
            } catch (e) {
                windowIdByName[windowName] = -1;
                console.log(e);
                return 0;
            }
        }

        // Open the record's window filtered to that row: the window named for this
        // table when it has one, else the table's default zoom target. Either way
        // the window is started with an equal-query on the table's key column
        // (TableName_ID). Degrades to a toast so a click never throws.
        function openRecord(tableName, recordId) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = resolveWindowIdByName(WINDOW_NAME_BY_TABLE[tableName]);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, false) || 0;
                }
                if (windowId > 0 && VIS.viewManager &&
                    typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(msg("VAS_102_OpenRecord", "Cannot open") + " " + tableName + " #" + recordId, true);
        }

        function toast(message, isError) {
            var $t = $('<div class="vas_102-toast"></div>')
                .addClass(isError ? "vas_102-err" : "vas_102-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_102-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_102-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3200);
        }

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
            link:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>',
            factory:  '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 20V9l6 4V9l6 4V9l6 4v7Z"/><path d="M2 20h20"/><path d="M7 20v-4"/><path d="M12 20v-4"/><path d="M17 20v-4"/></svg>',
            folder:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2Z"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_102-ic"></span>');
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

        // Parses a .NET/Newtonsoft DB value into a Date.
        //
        // asUtc = true  → for genuine *timestamps* (Created, activity, completion
        //   and posting moments). The DB stores these in UTC and Newtonsoft emits
        //   no timezone designator (e.g. "2026-08-05T10:00:00"), which the browser
        //   would otherwise read as local — so the feed printed the stored UTC
        //   clock and every entry looked hours off. Tagging it "Z" makes
        //   toLocale* render it in the viewer's own system zone.
        // asUtc = false → for *date-only* fields (movement / requisition / required
        //   dates). These carry no meaningful time-of-day, so the wall-clock value
        //   is parsed as-is and never shifted — the calendar day shown always
        //   matches the day stored, whatever the viewer's zone.
        // Strings that already carry a "Z" or ±hh:mm offset are left untouched.
        function parseDbDate(value, asUtc) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            var isDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s);
            if (asUtc && isDateTime && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            } else if (!asUtc && isDateTime) {
                // Keep the calendar date: drop any timezone marker and parse the
                // date/time as local so no zone conversion can roll the day over.
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        // Date + time in the viewer's local system zone — the audit trail needs
        // the moment, not just the day.
        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
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
        // Watch the tab itself so New Record / Copy Record (neither of which
        // reliably calls refreshPanelData) still empty the panel.
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update tab panel based on selected record */
    VAS.VAS_102_OverviewInternalUse.prototype.refreshPanelData = function (recordID, selectedRow) {
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
    VAS.VAS_102_OverviewInternalUse.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_102_OverviewInternalUse.prototype.dispose = function () {
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
