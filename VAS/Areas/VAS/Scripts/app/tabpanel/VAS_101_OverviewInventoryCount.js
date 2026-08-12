/************************************************************
 * Module Name    : VAS
 * Purpose        : Inventory Count Overview tab panel. Renders a review-
 *                  oriented overview of the selected physical inventory count
 *                  (M_Inventory, IsInternalUse <> 'N'): header identity +
 *                  warehouse / count details card, a four-card KPI snapshot
 *                  (counted value, net variance qty, variance lines, total
 *                  lines), matched / short / excess status cards, a compact
 *                  count timeline (Count started -> Counted -> Posting), a
 *                  count-lines table with per-line signed variance and a
 *                  segmented All-lines / Variances-only filter, and — below the
 *                  lines — Related Documents, Notes and the Activity trail. Data
 *                  is fetched from
 *                  VAS_101_OverviewInventoryCount/GetInventoryCountOverview.
 *                  All on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_101_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasic- -> vas_101- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_101-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_101-tone-" + tone).
 *   VAI163   2026-08-12  - The details card drops its Reference field. It carried
 *                          the count's DESCRIPTION — free text where the reader
 *                          expects a document identifier — and that description is
 *                          now shown in full in the new Notes section.
 *                        - The status row drops its Lines card: the KPI snapshot
 *                          above already reports the line count, and the three
 *                          beside it (Matched / Short / Excess) are what a line
 *                          can turn out to be, where the total answered a
 *                          different question.
 *                        - Line items drop the "SKU" and "Locator" captions and
 *                          show the values alone; the sub-line carries the whole
 *                          of a long locator name on a hover tooltip, as the
 *                          product name does.
 *                        - Line items show the Attribute Set Instance directly
 *                          under the product name (model side, joined only for a
 *                          real instance so the dictionary's "--" row cannot
 *                          print).
 *                        - Removed the Complete / Post adjustments buttons and
 *                          actionButton() which built them. Both were
 *                          PRESENTATIONAL — nothing was wired behind either — so
 *                          the panel offered two controls that did nothing. Both
 *                          actions belong to the count's own screen.
 *                        - Added Related Documents below the lines: the project
 *                          the count was raised for (M_Inventory.C_Project_ID),
 *                          opening the record on click through the panel's new
 *                          record-open path (bindEvents / openRecord /
 *                          GetWindow_ID).
 *                        - Added Notes above Activity: the count's own
 *                          description followed by each line's, labelled with its
 *                          line no and product.
 *                        - Added Activity at the bottom: created / updated /
 *                          completed / posted milestones plus chat notes,
 *                          newest-first, paged at 15 rows. Every row carries
 *                          "when · by whom", and a note's text wraps to three
 *                          lines. Timestamps render in the viewer's local zone
 *                          (parseDbDate tags a bare stamp "Z"; the count DATE is
 *                          still parsed as it stands so its calendar day cannot
 *                          roll over).
 *   VAI163   2026-08-12  Activity also carries the e-mails sent against the count
 *                        (type "email", model side): the subject headlines the
 *                        row, every address on the To / Cc / Bcc lists runs
 *                        underneath it in full, and the timestamp / sender sit
 *                        where every other entry carries them. The message body
 *                        opens on click, headed by the full From / To / Cc / Bcc
 *                        set. Follows VAS_099 / VAS_102.
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

    VAS.VAS_101_OverviewInventoryCount = function () {
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
        var variancesOnly = false;   // Count-lines filter state.
        var activityPage = 0;        // current Activity page (0-based)

        this.init = function () {
            $root = $('<div class="vas_101-root"></div>');
            $body = $('<div class="vas_101-body"></div>');
            $emptyState = $('<div class="vas_101-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_101_NoData"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            bindEvents();
        };

        // ----------------------------------------------------------------- //
        //  Events / record navigation                                        //
        // ----------------------------------------------------------------- //

        // Delegated once on the root, so it survives every re-render: a Related
        // Documents row opens the record it points at.
        function bindEvents() {
            $root.on("click", ".vas_101-is-link[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"));
            });
        }

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does open. Any further screen that
        // needs naming belongs here; nothing else has to change.
        var WINDOW_NAME_BY_TABLE = {
            "C_Project": "VAS_Project"
        };

        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click.
        var windowIdByName = {};

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
                    "VAS_101_OverviewInventoryCount/GetWindow_ID", windowName);
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

        // Opens the record's window filtered to that row: the window named for this
        // table when it has one, else the table's default zoom target, then started
        // with an equal-query on the table's key column. Degrades to a toast so a
        // click never throws.
        function openRecord(tableName, recordId) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = resolveWindowIdByName(WINDOW_NAME_BY_TABLE[tableName]);
                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, false) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(msg("VAS_101_OpenRecord", "Open") + " " + tableName + " #" + recordId, false);
        }

        // Lightweight self-contained toast.
        function toast(message, isError) {
            var $t = $('<div class="vas_101-toast"></div>')
                .addClass(isError ? "vas_101-err" : "vas_101-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_101-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_101-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3200);
        }

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
                url: VIS.Application.contextUrl + "VAS_101_OverviewInventoryCount/GetInventoryCountOverview",
                type: "GET",
                dataType: "json",
                data: { M_Inventory_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    variancesOnly = false;
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
            variancesOnly = false;
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
            renderSnapshot();
            renderStatusCards();
            renderTimeline();
            renderLines();
            // Below the lines: what the count is linked to, what was written
            // against it, and finally its audit trail. Activity comes LAST — it is
            // the longest section and it pages, so anything under it would be
            // pushed off the bottom of the panel.
            renderRelatedDocuments();
            renderNotes();
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="vas_101-sec"></section>');
            var $head = $('<div class="vas_101-secHead"></div>');
            $head.append($('<h2 class="vas_101-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_101-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_101_NA")
                : value;
        }

        // Prefer the seeded AD_Message; else a readable English fallback; else the
        // key. For message keys that may not be seeded yet.
        //
        // VIS.Msg does NOT answer an unseeded key with the key itself — it answers
        // with the key bracketed and upper-cased ("[VAS_101_ACTIVITY]"). That is
        // never equal to the key, so a bracketed answer is treated as "not found"
        // or the panel renders raw keys at the user.
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

        // ---------- Status map (code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_101_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_101_InProgress",          tone: "info" },
            "AP": { key: "VAS_101_Approved",            tone: "info" },
            "CO": { key: "VAS_101_Completed",           tone: "info" },
            "CL": { key: "VAS_101_Closed",              tone: "success" },
            "VO": { key: "VAS_101_Voided",              tone: "risk" },
            "RE": { key: "VAS_101_Reversed",            tone: "risk" },
            "WC": { key: "VAS_101_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_101_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_101_Invalid",             tone: "risk" },
            "NA": { key: "VAS_101_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="vas_101-hdr"></section>');
            var $top = $('<div class="vas_101-hdrTop"></div>');

            var $tl = $('<div class="vas_101-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_101-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_101_InventoryCount") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var counted = formatDate(data.CountDate);
            if (counted) subBits.push(VIS.Msg.getMsg("VAS_101_CountDate") + " " + counted);
            if (data.CountedBy) subBits.push(VIS.Msg.getMsg("VAS_101_CountedBy") + " " + data.CountedBy);
            if (subBits.length) {
                $tl.append($('<div class="vas_101-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_101-hdrPills"></div>');
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_101_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: warehouse identity (left) + document fields (right) ---
            var $card = $('<section class="vas_101-hdrCard"></section>');

            var $left = $('<div class="vas_101-hdrColL"></div>');
            $left.append($('<div class="vas_101-fLabel"></div>').text(VIS.Msg.getMsg("VAS_101_Warehouse")));
            $left.append($('<div class="vas_101-vendName"></div>').text(na(data.WarehouseName)));

            var $contact = $('<div class="vas_101-vendContact"></div>');
            appendContactBit($contact, "user", data.CountedBy);
            appendContactBit($contact, "calendar", formatDate(data.CountDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="vas_101-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_CountNo"), na(data.DocumentNo), false));
            // The Reference field is deliberately absent. It carried the count's
            // DESCRIPTION — free text, where the reader expects a document
            // identifier — and that description is now shown in full in the Notes
            // section, with the line notes it belongs beside.
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_101_Posted")
                            : VIS.Msg.getMsg("VAS_101_NotPosted"), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_Lines"), (data.LineCount || 0) + "", false));
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_101-hdrPill"></span>')
                .addClass("vas_101-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_101-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_101-hdrField"></div>');
            $f.append($('<div class="vas_101-fLabel"></div>').text(label));
            var $v = $('<div class="vas_101-fVal"></div>').text(value);
            if (link) $v.addClass("vas_101-is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_101-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_101-snap"></section>');
            var cur = currencyToken();

            // Total counted value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_101_TotalValue"),
                formatAmount(+data.TotalValue || 0, cur, data.StdPrecision), ""));

            // Net count variance (signed qty), tinted by direction.
            var net = +data.NetVarianceQty || 0;
            var netTone = net < 0 ? "short" : (net > 0 ? "excess" : "total");
            $snap.append(metricCard(netTone, "delta", VIS.Msg.getMsg("VAS_101_NetVariance"),
                signedNumber(net, 0), VIS.Msg.getMsg("VAS_101_NetQtyDifference")));

            // Variance lines.
            $snap.append(metricCard("variance", "alert", VIS.Msg.getMsg("VAS_101_VarianceLines"),
                (data.VarianceLineCount || 0) + "",
                VIS.Msg.getMsg("VAS_101_Of") + " " + (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_101_LinesWord")));

            // Total lines.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_101_TotalLines"),
                (data.LineCount || 0) + "", ""));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="vas_101-metric"></div>').addClass("vas_101-tone-" + tone);

            var $head = $('<div class="vas_101-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_101-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_101-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_101-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Status summary cards (Matched / Short / Excess / Lines) ---------- //

        function renderStatusCards() {
            var $row = $('<section class="vas_101-status"></section>');
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Matched"), data.MatchedCount || 0, "match"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Short"),   data.ShortCount || 0,   "short"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Excess"),  data.ExcessCount || 0,  "excess"));
            // The line COUNT is not repeated here: the snapshot card above already
            // reports it, and it is not a count outcome like the three beside it —
            // Matched / Short / Excess are what a line can turn out to be, and the
            // total was a fourth card answering a different question.
            $body.append($row);
        }

        function statusCard(label, value, tone) {
            var $c = $('<div class="vas_101-statCard"></div>').addClass("vas_101-tone-" + tone);
            $c.append($('<div class="vas_101-statVal"></div>').text(value + ""));
            $c.append($('<div class="vas_101-statLbl"></div>').text(label));
            return $c;
        }

        // ---------- Count timeline (3-node stepper) ---------- //

        function renderTimeline() {
            var completed = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var stages = [
                { key: "VAS_101_CountStarted", done: true,          date: data.CountDate },
                { key: "VAS_101_Counted",      done: completed,     date: data.CountDate },
                { key: "VAS_101_Posting",      done: data.Posted,   date: data.CountDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_101_CountTimeline"), null);

            var $tl = $('<div class="vas_101-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "vas_101-is-done";
                    metaText = (i === 2)
                        ? VIS.Msg.getMsg("VAS_101_Posted")
                        : (formatDate(s.date) || VIS.Msg.getMsg("VAS_101_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "vas_101-is-active";
                    metaText = VIS.Msg.getMsg("VAS_101_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_101_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_101-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_101-stepRail"></div>');
            $rail.append($('<span class="vas_101-stepLine vas_101-stepLine-l"></span>'));
            var $dot = $('<span class="vas_101-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="vas_101-stepLine vas_101-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_101-stepLabel"></div>');
            $lbl.append($('<div class="vas_101-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_101-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Count lines (table + filter toggle) ---------- //

        var $linesTable = null;   // rebuilt in place when the filter toggles.

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            // Segmented filter: All lines | Variances only.
            var $seg = $('<span class="vas_101-seg"></span>');
            var $btnAll = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_101_AllLines")).toggleClass("vas_101-on", !variancesOnly);
            var $btnVar = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_101_VariancesOnly")).toggleClass("vas_101-on", variancesOnly);
            $btnAll.on("click", function () { if (variancesOnly) { variancesOnly = false; refreshTable($btnAll, $btnVar); } });
            $btnVar.on("click", function () { if (!variancesOnly) { variancesOnly = true; refreshTable($btnAll, $btnVar); } });
            $seg.append($btnAll).append($btnVar);

            var $sec = section(VIS.Msg.getMsg("VAS_101_CountLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_101_Items"),
                $right: $seg
            });

            $linesTable = buildLinesTable();
            $sec.append($linesTable);
        }

        function refreshTable($btnAll, $btnVar) {
            $btnAll.toggleClass("vas_101-on", !variancesOnly);
            $btnVar.toggleClass("vas_101-on", variancesOnly);
            if (!$linesTable) return;
            var $fresh = buildLinesTable();
            $linesTable.replaceWith($fresh);
            $linesTable = $fresh;
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];
            var cur = currencyToken();

            var $tbl = $('<div class="vas_101-table"></div>');

            var $head = $('<div class="vas_101-tRow vas_101-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_101_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_101_UOM")));
            $head.append($('<span class="vas_101-ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_SystemQty")));
            $head.append($('<span class="vas_101-ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_CountedQty")));
            $head.append($('<span class="vas_101-ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_Variance")));
            $head.append($('<span class="vas_101-ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_Value")));
            $head.append($('<span class="vas_101-ta-c"></span>').text(VIS.Msg.getMsg("VAS_101_Status")));
            $tbl.append($head);

            var shown = 0;
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var variance = +ln.VarianceQty || 0;
                if (variancesOnly && variance === 0) continue;
                $tbl.append(buildLineRow(ln, variance, cur));
                shown++;
            }

            if (shown === 0) {
                $tbl.append($('<div class="vas_101-tEmpty"></div>')
                    .text(VIS.Msg.getMsg("VAS_101_NoVarianceLines")));
            }

            // Totals footer
            var $foot = $('<div class="vas_101-tFoot"></div>');
            var $bit = $('<span class="vas_101-tf vas_101-is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_101_TotalCountedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.TotalValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            return $tbl;
        }

        function buildLineRow(ln, variance, cur) {
            var $tr = $('<div class="vas_101-tRow vas_101-tBody"></div>');

            // Item: product name, the attributes it was counted against, then its
            // search key and locator.
            var $item = $('<span class="vas_101-itItem"></span>');
            // The name cell ellipsises, so the full product name goes on a hover
            // tooltip — the reader gets all of it without the column widening.
            var pname = na(ln.ProductName);
            var $name = $('<div class="vas_101-itName"></div>').text(pname);
            if (ln.ProductName) $name.attr("title", ln.ProductName);
            $item.append($name);

            // Lot / serial / attributes sit directly under the product name — the
            // attribute qualifies WHICH stock was counted, so it belongs with the
            // name rather than below the search key.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="vas_101-itAttr"></div>').text(asi).attr("title", asi));
            }

            // The search key and the locator, each as its own VALUE — the "SKU" and
            // "Locator" captions that used to precede them are gone. The column is
            // the item, and both are self-evidently what they are; the words cost a
            // third of the line's width and said nothing.
            //
            // A locator name can be long (a full aisle / bay / level combination),
            // so the sub-line carries the whole of it on a hover tooltip while the
            // cell itself ellipsises.
            var metaBits = [];
            if (ln.ProductCode) metaBits.push(ln.ProductCode);
            if (ln.LocatorName) metaBits.push(ln.LocatorName);
            if (metaBits.length) {
                var meta = metaBits.join(" · ");
                $item.append($('<div class="vas_101-itSku"></div>').text(meta).attr("title", meta));
            } else if (ln.Description) {
                $item.append($('<div class="vas_101-itSku"></div>')
                    .text(ln.Description).attr("title", ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // System (book) qty
            $tr.append($('<span class="vas_101-ta-r"></span>').text(formatNumber(+ln.SystemQty || 0, prec)));

            // Counted qty
            $tr.append($('<span class="vas_101-ta-r"></span>').text(formatNumber(+ln.CountedQty || 0, prec)));

            // Variance (signed, colored)
            var vTone = variance < 0 ? "short" : (variance > 0 ? "excess" : "match");
            $tr.append($('<span class="vas_101-ta-r vas_101-var"></span>').addClass("vas_101-" + vTone)
                .text(signedNumber(variance, prec)));

            // Value
            $tr.append($('<span class="vas_101-ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag
            var tagKey = variance < 0 ? "VAS_101_Short" : (variance > 0 ? "VAS_101_Excess" : "VAS_101_Match");
            var $q = $('<span class="vas_101-ta-c"></span>');
            $q.append($('<span class="vas_101-tag"></span>').addClass("vas_101-s-" + vTone)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // The Complete / Post adjustments buttons are gone, with actionButton()
        // which built them. They were PRESENTATIONAL — nothing was wired behind
        // either — so the panel offered two controls that did nothing when clicked.
        // Both actions belong to the count's own screen, where they carry the
        // document's validation.

        // ---------- Related Documents ---------- //

        // What the count is linked to. Today that is the project it was raised for
        // (M_Inventory.C_Project_ID); the section is built as a list so a further
        // linked document has somewhere to go.
        //
        // Drawn only when there IS something to list — a count linked to nothing
        // shows no section rather than an empty frame.
        function renderRelatedDocuments() {
            var rows = [];
            if (data.C_Project_ID > 0) {
                rows.push({
                    icon:  "folder",
                    label: msg("VAS_101_Project", "Project"),
                    // The project's search key identifies it; its name is the
                    // caption underneath, the way a document number and its kind
                    // read on every other panel.
                    value: (data.ProjectNo || "").trim() || (data.ProjectName || "").trim(),
                    sub:   (data.ProjectName || "").trim(),
                    table: "C_Project",
                    id:    data.C_Project_ID
                });
            }
            if (!rows.length) return;

            var $sec = section(msg("VAS_101_RelatedDocuments", "Related Documents"), {
                summary: rows.length + " " + msg("VAS_101_DocumentsCount", "documents")
            });

            var $tbl = $('<div class="vas_101-table vas_101-docTable"></div>');
            for (var i = 0; i < rows.length; i++) $tbl.append(buildRelatedRow(rows[i]));
            $sec.append($tbl);
        }

        function buildRelatedRow(d) {
            var $tr = $('<div class="vas_101-docRow vas_101-tBody"></div>');

            var canOpen = d.table && +d.id > 0;
            if (canOpen) {
                $tr.addClass("vas_101-is-link")
                    .attr("data-open-table", d.table)
                    .attr("data-open-id", d.id);
            }

            var $item = $('<span class="vas_101-docItem"></span>');
            $item.append(svgIcon(d.icon));

            var $txt = $('<span class="vas_101-docTxt"></span>');
            $txt.append($('<div class="vas_101-itName"></div>').text(d.value || "").attr("title", d.value || ""));
            // The caption only earns its line when it says something the value
            // does not — a project whose key IS its name gets one line, not two.
            if (d.sub && d.sub !== d.value) {
                $txt.append($('<div class="vas_101-itSku"></div>').text(d.label + " · " + d.sub).attr("title", d.sub));
            } else {
                $txt.append($('<div class="vas_101-itSku"></div>').text(d.label));
            }
            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $tr.append($item);

            return $tr;
        }

        // ---------- Notes (count header + line descriptions) ---------- //

        // Every description entered against the count: the one typed on the header
        // first, then the one typed on each line, each labelled server-side with
        // its line no and product. Skipped entirely when nothing was written, so an
        // empty card never trails the panel.
        function renderNotes() {
            var rows = (data && data.Notes) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_101_Notes", "Notes"), {
                summary: rows.length + " " + msg("VAS_101_NotesCount", "notes")
            });

            var $card = $('<div class="vas_101-textCard"></div>');
            for (var i = 0; i < rows.length; i++) {
                var label = (rows[i].Label || "").trim();
                if (label) $card.append($('<div class="vas_101-noteLbl"></div>').text(label));
                $card.append($('<p></p>').text(rows[i].Text || ""));
            }
            $sec.append($card);
        }

        // ---------- Activity (audit trail) ---------- //

        // Activity type -> tag label + tone + icon, and the sentence shown for it.
        var ACT_TYPES = {
            created:   { tone: "neutral", icon: "user",  tagKey: "VAS_101_TagCreated",   tagText: "Created",   titleKey: "VAS_101_ActCreated",   titleText: "Inventory count created" },
            updated:   { tone: "info",    icon: "pencil", tagKey: "VAS_101_TagUpdated",  tagText: "Updated",   titleKey: "VAS_101_ActUpdated",   titleText: "Inventory count updated" },
            completed: { tone: "success", icon: "check", tagKey: "VAS_101_TagCompleted", tagText: "Completed", titleKey: "VAS_101_ActCompleted", titleText: "Inventory count completed" },
            posted:    { tone: "purple",  icon: "coins", tagKey: "VAS_101_TagPosted",    tagText: "Posted",    titleKey: "VAS_101_ActPosted",    titleText: "Posted to accounting" },
            note:      { tone: "neutral", icon: "note",  tagKey: "VAS_101_TagNote",      tagText: "Note",      titleKey: null,                   titleText: "" },
            email:     { tone: "purple",  icon: "mail",  tagKey: "VAS_101_TagEmail",     tagText: "Email",     titleKey: null,                   titleText: "" }
        };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // The section summary still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_101_Activity", "Activity"), {
                summary: rows.length + " " + msg("VAS_101_Updates", "updates")
            });

            var $list = $('<div class="vas_101-actList"></div>');
            $sec.append($list);

            // The pager is a sibling of the list card, so the controls keep their
            // place while the card's rows are replaced underneath them.
            var $pager = $('<div class="vas_101-pager"></div>');
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

            var $row = $('<div class="vas_101-actRow"></div>');

            var $tag = $('<span class="vas_101-actTag"></span>').addClass("vas_101-tone-" + meta.tone);
            $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.tagKey, meta.tagText)));
            $row.append($tag);

            // For a note the headline is the comment itself, for an e-mail its
            // subject; for everything else it is the event sentence. A tooltip
            // keeps a long line readable once the cell ellipsises.
            var title = activityTitle(a, meta);
            var $title = $('<span class="vas_101-actTitle"></span>');
            var $lead = $('<span class="vas_101-actLead"></span>').text(title).attr("title", title);
            if (a.Type === "note") $lead.addClass("vas_101-multiline");
            $title.append($lead);

            // An e-mail names its recipients under the subject — every address on
            // the To, Cc and Bcc lists, in full. The line wraps (stylesheet), so a
            // long list is read on the row itself rather than hidden behind a
            // count the reader would have to open the message to resolve.
            if (a.Type === "email") {
                var to = recipientSummary(a);
                if (to) $title.append($('<small class="vas_101-actSub"></small>').text(to));
            }
            $row.append($title);

            // "when · by whom" — the audit trail's whole point, in the same place on
            // every row. For an e-mail that is when it went out and who sent it.
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when ? when + " · " + msg("VAS_101_By", "by") + " " + a.UserName
                            : msg("VAS_101_By", "by") + " " + a.UserName;
            }
            $row.append($('<span class="vas_101-actWhen"></span>').text(when).attr("title", when));

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                $row.addClass("vas_101-is-openable");
                $row.attr("title", msg("VAS_101_ShowMailBody", "Click to read the message"));
                $row.append($('<span class="vas_101-actCaret"></span>').append(svgIcon("chevRight")));
                $row.on("click", function () {
                    var $panel = $row.next(".vas_101-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_101-is-open");
                    $row.toggleClass("vas_101-is-open", nowOpen)
                        .attr("title", nowOpen ? msg("VAS_101_HideMailBody", "Click to hide the message")
                                               : msg("VAS_101_ShowMailBody", "Click to read the message"));
                    $panel.toggle(nowOpen);
                });
            }

            return $row;
        }

        function activityTitle(a, meta) {
            if (a.Type === "note") return (a.Text || "").trim();
            if (a.Type === "email") {
                return (a.Text || "").trim() || msg("VAS_101_NoSubject", "(no subject)");
            }
            return meta.titleKey ? msg(meta.titleKey, meta.titleText) : (meta.titleText || "");
        }

        // Only an e-mail carries a body worth opening; a mail stored without one
        // stays a plain, non-clickable row.
        function hasActivityBody(a) {
            return !!(a && a.Type === "email" && a.Body && String(a.Body).trim());
        }

        // The e-mail body, collapsed beneath its activity row. The full recipient
        // set (From / To / Cc / Bcc) heads it, so every address the mail went to is
        // on screen once the reader opens the message.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_101-actBody" style="display:none;"></div>');
            appendMailMeta($panel, "VAS_101_MailFrom", "From:", a.MailFrom);
            appendMailMeta($panel, "VAS_101_MailTo",   "To:",   a.MailTo);
            appendMailMeta($panel, "VAS_101_MailCc",   "Cc:",   a.MailCc);
            appendMailMeta($panel, "VAS_101_MailBcc",  "Bcc:",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        function appendMailMeta($panel, key, fallback, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_101-actMeta"></div>')
                .text(msg(key, fallback) + " " + String(value).trim()));
        }

        // Row sub-line: every address the mail went to, written out in full — To,
        // then Cc, then Bcc, each behind its own label. A label with nothing behind
        // it is left out entirely: this line lists recipients, and an empty Cc is
        // not one.
        function recipientSummary(a) {
            var bits = [];
            appendAddressBit(bits, "VAS_101_MailTo",  "To:",  a.MailTo);
            appendAddressBit(bits, "VAS_101_MailCc",  "Cc:",  a.MailCc);
            appendAddressBit(bits, "VAS_101_MailBcc", "Bcc:", a.MailBcc);
            return bits.join(" · ");
        }

        function appendAddressBit(bits, key, fallback, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(msg(key, fallback) + " " + text);
        }

        // Renders the pager into $pager: a range caption on the left, Previous /
        // page-of / Next on the right. Rebuilt on every page change so the disabled
        // states stay accurate. Nothing is drawn for a single-page list.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_101-pgRange"></span>').text(
                msg("VAS_101_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("VAS_101_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_101-pgCtrls"></span>');
            $ctrls.append(pagerButton(msg("VAS_101_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_101-pgPos"></span>').text(
                msg("VAS_101_Page", "Page") + " " + (page + 1) + " " +
                msg("VAS_101_Of", "of") + " " + pageCount));
            $ctrls.append(pagerButton(msg("VAS_101_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_101-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_101-is-disabled");
            else $b.on("click", handler);
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
            delta:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4 3 20h18Z"/></svg>',
            alert:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            layers:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            // Related Documents, Notes and Activity.
            pencil:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            note:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h6"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            folder:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2Z"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>',
            chevLeft: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight:'<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_101-ic"></span>');
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

        // Signed number: "+N" for positive, "−N" for negative, "0" for zero.
        function signedNumber(value, precision) {
            var v = +value || 0;
            if (v === 0) return formatNumber(0, precision);
            var sign = v > 0 ? "+" : "−";
            return sign + formatNumber(Math.abs(v), precision);
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
        // asUtc = true  → for genuine TIMESTAMPS (the activity feed's moments). The
        //   DB stores these in UTC and the server emits no timezone designator, so
        //   the browser would read them as local and the feed would print the stored
        //   UTC clock. Tagging it "Z" renders it in the viewer's own system zone.
        // asUtc = false → for DATE-ONLY fields (the count date). These carry no
        //   meaningful time of day, so the value is parsed as it stands and never
        //   shifted — the calendar day shown always matches the day stored.
        // Strings already carrying a "Z" or a ±hh:mm offset are left untouched.
        function parseDbDate(value, asUtc) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            var isDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s);
            if (asUtc && isDateTime && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            } else if (!asUtc && isDateTime) {
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

        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "2-digit" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_101_OverviewInventoryCount.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_101_OverviewInventoryCount.prototype.refreshPanelData = function (recordID, selectedRow) {
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
    VAS.VAS_101_OverviewInventoryCount.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_101_OverviewInventoryCount.prototype.dispose = function () {
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
