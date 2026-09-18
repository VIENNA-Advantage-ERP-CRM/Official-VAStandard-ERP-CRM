/************************************************************
 * Module Name    : VAS
 * Purpose        : Journal details right panel for the selected GL_Journal
 *                  record.
 *
 *                  Composition follows the approved reference
 *                  (gl-journal-panel-shell.html), section for section:
 *                    hero status card (+ metric grid) -> action pill row ->
 *                    journal lines data grid -> approval routing step flow.
 *                  The reference's section-intro line is deliberately NOT
 *                  drawn. Tokens come from design.md (windows-and-panels.md >
 *                  Right Panel Body), not from the prototype.
 *
 *                  The panel chrome (shell width, collapse strip, header, close
 *                  button, panel switcher) belongs to the VIS tab-panel host,
 *                  not to this file - every existing VAS right panel hands
 *                  that to the host, and this one does the same. The root this
 *                  file returns IS the panel body.
 *
 *                  Data comes from VAS_291_GLJournalRightPanel/GetJournalOverview
 *                  in one read. The Approval routing steps are composed HERE
 *                  from the facts the server reports (creator, human workflow
 *                  activities, document status, posting status + moment) so
 *                  every step title is localised through VIS.Msg. Exactly one
 *                  step is ever "active": the first one that is neither done
 *                  nor blocked. Drawn as the rail the payment panel (VAS_191)
 *                  uses - a state-toned marker node, a hairline connector and
 *                  plain title + meta text, no tinted card per step.
 *
 *                  Print voucher and Download PDF both run the hosting tab's
 *                  print process through JsonData/GeneratePrint (same path as
 *                  VAS_189 / VAS_191); Print loads the produced PDF into a
 *                  hidden frame and asks the browser to print it, Download
 *                  saves it. Both are drawn disabled when the tab has no print
 *                  process. Open Line Details switches the HOSTING window to
 *                  the journal's own line tab - the same thing a click on that
 *                  tab header does - and never opens a second GL Journal
 *                  instance: the APanel is reached through the GridController
 *                  the host registered on curTab, the line tab is found by its
 *                  table (GL_JournalLine) under the current tab, and its tab
 *                  action (windowNo_AD_Tab_ID) is raised through
 *                  aPanel.onTabChange. No window id, tab id or tab name is
 *                  hard-coded.
 *
 *                  Journal lines page on the SERVER, 50 rows per request
 *                  (data.LinesPageSize): the initial payload carries page 0
 *                  plus the count and Dr / Cr totals over every line, and the
 *                  compact footer pager fetches further pages through
 *                  GetJournalLines, caching each page until the record
 *                  changes. The panel body owns scrolling and no section grows
 *                  its own scrollbar. The lines section is hidden when a
 *                  non-editable journal has no lines; a draft with no lines
 *                  shows its empty state instead.
 *
 *                  All on-screen strings resolve through VIS.Msg.getMsg with an
 *                  English fallback, so an unseeded AD_Message key never renders
 *                  as a raw key.
 * Class Used     : VAS.VAS_291_GLJournalRightPanel
 * Chronological development:
 *   VAI145   2026-09-18  Created.
 *   VAI145   2026-09-18  Section intro removed; hero subtitle = document type ·
 *                        posting type; lines paged on the server at 50 per
 *                        request; "Open Line Details" switches the hosting
 *                        window to the line tab instead of zooming.
 *   VAI145   2026-09-18  Approval routing redrawn as the VAS_191 rail (same
 *                        steps and states, no tinted cards); Period close
 *                        lock step dropped.
 *
 * -- Labels / Message Keys ---------------------------------------------------
 *  Panel
 *   No journal selected                   | VAS_291_NoData
 *   Could not load the journal details.   | VAS_291_LoadFailed
 *   The action could not be completed.    | VAS_291_ActionFailed
 *
 *  Hero card
 *   debits = credits                      | VAS_291_Balanced
 *   out of balance by {0}                 | VAS_291_OutOfBalance
 *   Posting date                          | VAS_291_PostingDate
 *   Accounting book                       | VAS_291_AccountingBook
 *   Currency                              | VAS_291_Currency
 *   Period                                | VAS_291_Period
 *
 *  Actions
 *   Print voucher                         | VAS_291_PrintVoucher
 *   Download PDF                          | VAS_291_DownloadPDF
 *
 *  Journal lines
 *   Journal lines                         | VAS_291_JournalLines
 *   {0} lines                             | VAS_291_LineCount
 *   Open Line Details                     | VAS_291_OpenLineDetails
 *   Account & dimensions                  | VAS_291_AccountDimensions
 *   Debit / Credit                        | VAS_291_Debit / VAS_291_Credit
 *   Debit line / Credit line              | VAS_291_DebitLine / VAS_291_CreditLine
 *   No debit on this line                 | VAS_291_NoDebit
 *   No credit on this line                | VAS_291_NoCredit
 *   No lines on this journal yet.         | VAS_291_NoLines
 *   Document total                        | VAS_291_DocumentTotal
 *   balanced / out of balance             | VAS_291_TotalBalanced / VAS_291_TotalUnbalanced
 *   Showing                               | VAS_291_Showing
 *   Previous page / Next page / of        | VAS_291_Previous / VAS_291_Next / VAS_291_Of
 *
 *  Approval routing
 *   Approval routing                      | VAS_291_ApprovalRouting
 *   {0} of {1} complete                   | VAS_291_StepsComplete
 *   Prepared                              | VAS_291_Prepared
 *   Completed                             | VAS_291_Completed
 *   awaiting completion                   | VAS_291_AwaitingCompletion
 *   Posted to ledger                      | VAS_291_PostedToLedger
 *   System                                | VAS_291_System
 *   not posted yet                        | VAS_291_NotPostedYet
 * ---------------------------------------------------------------------------
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab sits on a row that has not been saved yet - whether it
    // came from New Record or from Copy Record. The authority is the GRID
    // TABLE's insert flag: GridTab does not expose it, it only holds the table
    // as .gridTable, so asking the tab itself always answers "no". The record id
    // cannot answer it either: a copied row still carries the SOURCE record's
    // key.
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

    VAS.VAS_291_GLJournalRightPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;

        var CLS = "vas_291-";

        /* The GL_Journal_ID the panel is showing OR loading. 0 = nothing. */
        var shownRecordId = 0;

        /* How long refreshPanelData holds before it fetches. On New Record /
           Copy Record the framework can call it BEFORE GridTable raises its
           insert flag, so asking at that instant answers "no". Asking again
           after this pause gets the truth, and it collapses a burst of
           arrow-key row changes into one request. */
        var REFRESH_DELAY_MS = 150;
        /* Raised by every fetch, scheduled fetch and clear. A reply carrying a
           stale token belongs to a journal the panel has already left, so it is
           dropped instead of painting over the newer one. */
        var fetchToken = 0;
        var pendingFetch = null;
        /* A request is on the wire. Together with pendingFetch and shownRecordId
           this is what lets the panel tell "already loading this record" from
           "idle", so the two host entry points cannot each start their own load
           for the same selection. */
        var inFlight = false;

        /* Journal lines page on screen (zero-based) and the pages fetched so far,
           keyed by page index. Page 0 comes with the initial payload; the rest
           are fetched on demand through GetJournalLines. Both reset on every
           record change. The page size is the SERVER's (data.LinesPageSize), so
           the pager and the server can never disagree about where a page starts. */
        var linesPage = 0;
        var linePages = {};
        var DEFAULT_LINES_PER_PAGE = 50;
        /* A page fetch on the wire, keyed by the fetch token it belongs to. */
        var linesFetchToken = 0;

        /* Hidden frames raised by Print voucher. Kept until the next print or
           dispose - removing one while its print dialog is open cancels it. */
        var printFrames = [];

        /* ---------------------------------------------------------------- */
        /*  Icons - inline SVG only; the host shell may not carry an icon font */
        /* ---------------------------------------------------------------- */

        var SVG_ATTR = 'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"';
        var SVG = {
            print: '<svg ' + SVG_ATTR + '><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8" rx="1"/></svg>',
            download: '<svg ' + SVG_ATTR + '><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/><path d="M12 15V3"/></svg>',
            external: '<svg ' + SVG_ATTR + '><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5"/></svg>',
            debit: '<svg ' + SVG_ATTR + '><path d="M17 7 7 17"/><path d="M17 17H7V7"/></svg>',
            credit: '<svg ' + SVG_ATTR + '><path d="M7 17 17 7"/><path d="M7 7h10v10"/></svg>',
            prev: '<svg ' + SVG_ATTR + '><path d="m15 18-6-6 6-6"/></svg>',
            next: '<svg ' + SVG_ATTR + '><path d="m9 18 6-6-6-6"/></svg>',
            check: '<svg ' + SVG_ATTR.replace('stroke-width="2"', 'stroke-width="3"') + '><path d="M20 6 9 17l-5-5"/></svg>',
            cross: '<svg ' + SVG_ATTR.replace('stroke-width="2"', 'stroke-width="3"') + '><path d="M18 6 6 18M6 6l12 12"/></svg>'
        };

        function icon(name) {
            return $('<span class="' + CLS + 'ic"></span>').html(SVG[name] || "");
        }

        /* ---------------------------------------------------------------- */
        /*  Messages                                                        */
        /* ---------------------------------------------------------------- */

        // Prefer the seeded AD_Message; else a readable English default; else the
        // key. VIS.Msg answers an unseeded key with the key BRACKETED, which is
        // never equal to the key, so a bracketed answer counts as "not found".
        function msg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key && !isMissingMsg(m)) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : key;
        }

        function isMissingMsg(text) {
            var t = String(text);
            return t.length > 1 && t.charAt(0) === "[" && t.charAt(t.length - 1) === "]";
        }

        function fmt(text, a, b) {
            return String(text).replace("{0}", a === undefined ? "" : a).replace("{1}", b === undefined ? "" : b);
        }

        function error(text) {
            if (window.VIS && VIS.ADialog && VIS.ADialog.error) VIS.ADialog.error("", "", text);
            else if (window.console) console.log(text);
        }

        /* ---------------------------------------------------------------- */
        /*  Formatting                                                      */
        /* ---------------------------------------------------------------- */

        /* Amounts carry the journal currency's symbol and precision; the
           user's locale decides the grouping and decimal separators. */
        function fmtMoney(value, symbol, precision) {
            var v = (+value) || 0;
            var p = (precision >= 0 && precision <= 10) ? precision : 2;
            var sign = v < 0 ? "-" : "";
            var text;
            try {
                text = Math.abs(v).toLocaleString(window.navigator.language, {
                    minimumFractionDigits: p, maximumFractionDigits: p
                });
            } catch (e) {
                text = Math.abs(v).toFixed(p);
            }
            /* Symbol sits flush against the figure - "$100.00", not "$ 100.00". */
            return sign + (symbol || "") + text;
        }

        function money(value) {
            if (!data) return "";
            return fmtMoney(value, data.CurSymbol || data.CurISO, data.StdPrecision);
        }

        /* Grid cells carry no symbol - the column header and the hero already
           name the currency, and the symbol would eat width in a narrow panel. */
        function amount(value) {
            if (!data) return "";
            return fmtMoney(value, "", data.StdPrecision);
        }

        /* Server dates arrive as ISO yyyy-MM-dd, so they are parsed as a plain
           calendar date; the user's locale decides how it reads. */
        function fmtDate(value) {
            if (!value) return "";
            var s = String(value);
            var parts = s.length >= 10 ? s.substring(0, 10).split("-") : null;
            var d = (parts && parts.length === 3)
                ? new Date(+parts[0], (+parts[1]) - 1, +parts[2])
                : new Date(s);
            if (!d || isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        /* Server timestamps arrive as yyyy-MM-dd HH:mm and are read as UTC, then
           shown in the browser's own zone - the raw string is never displayed
           as if it were local time. */
        function fmtStamp(value) {
            if (!value) return "";
            var m = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})/.exec(String(value));
            if (!m) return fmtDate(value);
            var d = new Date(Date.UTC(+m[1], (+m[2]) - 1, +m[3], +m[4], +m[5]));
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "2-digit" })
                    + ", " + d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
            } catch (e) {
                return d.toString();
            }
        }

        function joinBits(bits) {
            var kept = [];
            for (var i = 0; i < bits.length; i++) {
                if (bits[i] !== null && bits[i] !== undefined && String(bits[i]).length > 0) {
                    kept.push(bits[i]);
                }
            }
            return kept.join(" · ");
        }

        /* ---------------------------------------------------------------- */
        /*  Document state                                                  */
        /* ---------------------------------------------------------------- */

        function isPosted() { return !!(data && data.Posted === "Y"); }
        function isPostError() { return !!(data && data.Posted && data.Posted !== "Y" && data.Posted !== "N"); }
        function isReversedOrVoided() { return !!(data && (data.DocStatus === "RE" || data.DocStatus === "VO")); }
        function isCompletedOrClosed() { return !!(data && (data.DocStatus === "CO" || data.DocStatus === "CL")); }
        /* Lines can still change while the document is neither completed nor
           reversed - that is when an empty lines section is worth showing. */
        function isEditable() { return !!(data && !isCompletedOrClosed() && !isReversedOrVoided()); }
        function isBalanced() { return !!(data && Math.abs((+data.TotalDr || 0) - (+data.TotalCr || 0)) < 0.000001); }

        /* Hero tone follows the headline fact: posted = success, reversed /
           voided = risk, a failed posting = warning, anything still moving = info. */
        function heroTone() {
            if (isReversedOrVoided()) return "crit";
            if (isPostError()) return "warn";
            if (isPosted()) return "ok";
            return "info";
        }

        function statusPillText() {
            if (!data) return "";
            if (isReversedOrVoided()) return data.DocStatusName || data.DocStatus || "";
            if (isPosted()) return data.PostedName || data.DocStatusName || "";
            return data.DocStatusName || data.DocStatus || "";
        }

        /* ---------------------------------------------------------------- */
        /*  Lifecycle                                                       */
        /* ---------------------------------------------------------------- */

        this.init = function () {
            $root = $('<div class="' + CLS + 'root"></div>');
            $body = $('<div class="' + CLS + 'body"></div>');
            $emptyState = $('<div class="' + CLS + 'empty" style="display:none;"></div>');
            $emptyState.text(msg("VAS_291_NoData", "No journal selected"));
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

        /* Delegated once on the root so the handlers survive every re-render.
           Every clickable element here is a real <button>, so it answers the
           keyboard the way a button does without extra key handling. All of
           these stay inside the panel root - nothing is detached to body. */
        function bindEvents() {
            $root.on("click", "[data-action]", function (e) {
                e.preventDefault();
                var $btn = $(this);
                if ($btn.prop("disabled")) return;
                runAction($btn.attr("data-action"));
            });
        }

        function runAction(name) {
            try {
                if (name === "print") printVoucher();
                else if (name === "download") downloadPDF();
                else if (name === "open-lines") openLineDetails();
                else if (name === "lines-prev") pageLines(-1);
                else if (name === "lines-next") pageLines(1);
            } catch (e) { if (window.console) console.log(e); }
        }

        /* ---------------------------------------------------------------- */
        /*  Request lifecycle                                               */
        /* ---------------------------------------------------------------- */

        /* Drops whatever the panel was loading: cancels a fetch still waiting on
           its delay and invalidates the token of one already on the wire, so
           neither can paint over what the caller is about to put on screen. */
        function invalidateFetch() {
            fetchToken++;
            if (pendingFetch) {
                clearTimeout(pendingFetch);
                pendingFetch = null;
            }
            inFlight = false;
        }

        this.abortPendingFetch = invalidateFetch;

        /* True when the panel is already showing, waiting to load, or loading this
           exact record. The host drives the panel from TWO independent entry
           points for one selection - refreshPanelData() and the tab's
           dataStatusChanged event - in no guaranteed order; this test is what
           keeps them from fetching the same journal twice. */
        function isCurrent(recordID) {
            var id = +recordID || 0;
            return id > 0 && id === shownRecordId
                   && (data !== null || inFlight || pendingFetch !== null);
        }

        /* An explicit reload (the platform Refresh button) calls fetchData
           directly and so is never blocked by the guard above. */
        this.scheduleFetch = function (recordID) {
            if (isCurrent(recordID)) return;

            invalidateFetch();
            var token = fetchToken;
            /* Claimed now, not when the timer fires: shownRecordId means "showing
               or loading", and leaving it stale through the wait would let the
               data-status listener fire a second fetch for the same row. */
            shownRecordId = +recordID || 0;
            showBusy(true);
            pendingFetch = setTimeout(function () {
                pendingFetch = null;
                if (token !== fetchToken) return;          // superseded while waiting
                if (isTabInserting($self.curTab)) {        // flag may only be up now
                    $self.record_ID = 0;
                    $self.clear();
                    return;
                }
                $self.fetchData(recordID);
            }, REFRESH_DELAY_MS);
        };

        this.fetchData = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            shownRecordId = +recordID || 0;
            /* Nothing of the previous journal may stay on screen while another
               record is loading. The "no journal" placeholder is NOT raised:
               this is a load, not an empty selection. */
            data = null;
            if ($body) {
                $body.empty();
                $emptyState.hide();
                $body.show();
            }
            showBusy(true);
            inFlight = true;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_291_GLJournalRightPanel/GetJournalOverview",
                type: "GET",
                dataType: "json",
                data: { GL_Journal_ID: recordID },
                success: function (raw) {
                    /* Reply for a journal the panel has already left. Whoever
                       superseded us owns the busy indicator and the in-flight
                       flag now, so this reply must not touch either. */
                    if (token !== fetchToken) return;
                    inFlight = false;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (data && !(+data.GL_Journal_ID > 0)) data = null;   // not accessible / not found
                    linesPage = 0;
                    linePages = {};
                    if (data) linePages[0] = data.Lines || [];
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    if (token !== fetchToken) return;
                    inFlight = false;
                    if (window.console) console.log(err);
                    data = null;
                    render();
                    showBusy(false);
                    error(msg("VAS_291_LoadFailed", "Could not load the journal details."));
                }
            });
        };

        this.clear = function () {
            invalidateFetch();
            data = null;
            shownRecordId = 0;
            linesPage = 0;
            linePages = {};
            render();
            /* A discarded reply never reaches its own showBusy(false), so the
               spinner would otherwise sit on the empty panel for good. */
            showBusy(false);
        };

        /* The framework notifies a tab panel when the selected record changes but
           NOT when the user starts a new one: GridController.dataNew() never
           reaches the tab panel. Listening to the tab's own data-status events
           closes that gap. */
        function onTabDataStatus(e) {
            var inserting = false;
            try {
                inserting = !!(e && typeof e.getIsInserting === "function" && e.getIsInserting());
            } catch (ex) {
                inserting = false;
            }
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
                if (shownRecordId || data) {
                    $self.record_ID = 0;
                    $self.clear();
                }
                return;
            }
            /* Routed through scheduleFetch, not straight to fetchData, so this
               entry point goes through the same "already showing / already
               loading" guard and the same debounce as refreshPanelData. */
            if (rid !== shownRecordId) {
                $self.record_ID = rid;
                $self.scheduleFetch(rid);
            }
        }

        this.tabDataListener = { dataStatusChanged: function (e) { onTabDataStatus(e); } };

        /* The platform Refresh button calls this. Without it the button silently
           does nothing, so it is exposed as an instance method here and as a
           prototype method below. */
        this.refreshWidget = function () {
            if ($self.record_ID > 0) $self.fetchData($self.record_ID);
            else $self.clear();
        };

        /* ---------------------------------------------------------------- */
        /*  Render                                                          */
        /* ---------------------------------------------------------------- */

        function render() {
            if (!$body) return;
            $body.empty();

            if (!data) {
                $body.hide();
                $emptyState.show();
                return;
            }
            $emptyState.hide();
            $body.show();

            /* Composition rule: hero -> action pills -> headered sections. */
            $body.append(renderHero());
            $body.append(renderActions());
            var $lines = renderLines();
            if ($lines) $body.append($lines);
            var $routing = renderRouting();
            if ($routing) $body.append($routing);

            resetScroll();
        }

        /* A different record starts at the top. Staggered because the host may
           still be laying the panel out when the first reset runs. */
        function resetScroll() {
            var delays = [0, 100, 300, 600];
            for (var i = 0; i < delays.length; i++) {
                setTimeout(function () {
                    var el = $root && $root[0];
                    while (el && el !== document.body) {
                        if (el.scrollTop) el.scrollTop = 0;
                        el = el.parentNode;
                    }
                }, delays[i]);
            }
        }

        /* Reusable pieces ------------------------------------------------- */

        function sectionHeader(title, summary, $action) {
            var $h = $('<div class="' + CLS + 'secHead"></div>');
            $h.append($('<span class="' + CLS + 'secTitle"></span>').text(title).attr("title", title));
            var $right = $('<span class="' + CLS + 'secRight"></span>');
            if (summary) $right.append($('<span class="' + CLS + 'secSum"></span>').text(summary).attr("title", summary));
            if ($action) $right.append($action);
            $h.append($right);
            return $h;
        }

        function cell(cls, text, title) {
            return $('<span class="' + CLS + cls + '"></span>').text(text).attr("title", title || text);
        }

        /* 3 · Hero status card + 4 · metric grid -------------------------- */

        function renderHero() {
            var $hero = $('<section class="' + CLS + 'hero ' + CLS + 'tone-' + heroTone() + '"></section>');

            var $top = $('<div class="' + CLS + 'heroTop"></div>');
            var $id = $('<div class="' + CLS + 'heroId"></div>');
            $id.append($('<div class="' + CLS + 'heroTitle"></div>').text(data.DocumentNo || "").attr("title", data.DocumentNo || ""));
            /* Subtitle names the document type and the posting type (Actual /
               Budget / Statistical) - never the free-text description. */
            var subtitle = joinBits([data.DocTypeName, data.PostingTypeName || data.PostingType]);
            if (subtitle) {
                $id.append($('<div class="' + CLS + 'heroSub"></div>').text(subtitle).attr("title", subtitle));
            }
            $top.append($id);
            var pill = statusPillText();
            if (pill) $top.append($('<span class="' + CLS + 'chip ' + CLS + 'onTint"></span>').text(pill));
            $hero.append($top);

            var $emph = $('<div class="' + CLS + 'heroEmph"></div>');
            var total = money(data.TotalDr);
            $emph.append($('<span class="' + CLS + 'heroValue"></span>').text(total).attr("title", total));
            var qualifier = isBalanced()
                ? msg("VAS_291_Balanced", "debits = credits")
                : fmt(msg("VAS_291_OutOfBalance", "out of balance by {0}"), money(Math.abs((+data.TotalDr || 0) - (+data.TotalCr || 0))));
            $emph.append($('<span class="' + CLS + 'heroQual"></span>').text(qualifier));
            $hero.append($emph);

            var $grid = $('<div class="' + CLS + 'metrics"></div>');
            $grid.append(metric(msg("VAS_291_PostingDate", "Posting date"), fmtDate(data.DateAcct)));
            $grid.append(metric(msg("VAS_291_AccountingBook", "Accounting book"), data.AcctSchemaName));
            var curText = data.CurISO ? (data.CurISO + (data.CurSymbol && data.CurSymbol !== data.CurISO ? " (" + data.CurSymbol + ")" : "")) : "";
            /* Currency only - the conversion rate is deliberately not shown. */
            $grid.append(metric(msg("VAS_291_Currency", "Currency"), curText));
            $grid.append(metric(msg("VAS_291_Period", "Period"), joinBits([data.FiscalYear, data.PeriodName])));
            $hero.append($grid);

            return $hero;
        }

        function metric(label, value) {
            var $c = $('<div class="' + CLS + 'metric"></div>');
            $c.append($('<div class="' + CLS + 'metricLabel"></div>').text(label));
            var v = value || "—";
            $c.append($('<div class="' + CLS + 'metricValue"></div>').text(v).attr("title", v));
            return $c;
        }

        /* 8 · Action pill row --------------------------------------------- */

        /* Both pills run off the tab's print process, so they share one
           enablement test: nothing can be printed without it. They are drawn
           disabled rather than hidden, so the user can see the action exists. */
        function hasPrintProcess() {
            var tab = $self.curTab;
            return !!(tab && typeof tab.getAD_Process_ID === "function"
                && +tab.getAD_Process_ID() > 0);
        }

        function renderActions() {
            var $row = $('<section class="' + CLS + 'pills"></section>');
            var canPrint = hasPrintProcess();
            $row.append(pill("print", "print", msg("VAS_291_PrintVoucher", "Print voucher"), canPrint));
            $row.append(pill("download", "download", msg("VAS_291_DownloadPDF", "Download PDF"), canPrint));
            return $row;
        }

        function pill(action, iconName, label, enabled) {
            var $b = $('<button type="button" class="' + CLS + 'pill"></button>')
                .attr("data-action", action)
                .attr("title", label)
                .prop("disabled", !enabled);
            if (!enabled) $b.attr("aria-disabled", "true");
            $b.append(icon(iconName)).append($('<span></span>').text(label));
            return $b;
        }

        /* 11 · Data grid - journal lines ---------------------------------- */

        /* Count over EVERY line, from the server's aggregate - not the length of
           the page on screen. */
        function lineCount() { return (data && +data.LineCount) || 0; }

        function linesPerPage() { return (data && +data.LinesPageSize > 0) ? +data.LinesPageSize : DEFAULT_LINES_PER_PAGE; }

        function linePageCount() { return Math.max(1, Math.ceil(lineCount() / linesPerPage())); }

        /* The rows of the page on screen, or null while that page is still on
           the wire. */
        function currentLines() {
            return linePages.hasOwnProperty(linesPage) ? linePages[linesPage] : null;
        }

        function renderLines() {
            var total = lineCount();
            /* An empty section is only worth drawing while lines can still be
               added; a completed or reversed journal without lines is hidden. */
            if (!total && !isEditable()) return null;

            var $sec = $('<section class="' + CLS + 'sec ' + CLS + 'lines"></section>');
            var $open = $('<button type="button" class="' + CLS + 'secAction"></button>')
                .attr("data-action", "open-lines")
                .attr("title", msg("VAS_291_OpenLineDetails", "Open Line Details"));
            $open.append(icon("external")).append($('<span></span>').text(msg("VAS_291_OpenLineDetails", "Open Line Details")));
            $sec.append(sectionHeader(
                msg("VAS_291_JournalLines", "Journal lines"),
                fmt(msg("VAS_291_LineCount", "{0} lines"), total),
                $open));

            if (!total) {
                $sec.append($('<div class="' + CLS + 'secEmpty"></div>').text(msg("VAS_291_NoLines", "No lines on this journal yet.")));
                return $sec;
            }

            var $grid = $('<div class="' + CLS + 'dg"></div>');

            /* Header row: the leading affordance column carries no label. */
            var $head = $('<div class="' + CLS + 'dgHead"></div>');
            $head.append($('<span aria-hidden="true"></span>'));
            $head.append(cell("dgH", msg("VAS_291_AccountDimensions", "Account & dimensions")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_291_Debit", "Debit")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_291_Credit", "Credit")));
            $grid.append($head);

            var pageCount = linePageCount();
            if (linesPage > pageCount - 1) linesPage = pageCount - 1;
            if (linesPage < 0) linesPage = 0;

            var rows = currentLines();
            if (rows) {
                for (var i = 0; i < rows.length; i++) {
                    $grid.append(lineRow(rows[i], i === rows.length - 1));
                }
            } else {
                /* Page still on the wire: the framework's own busy indicator sits
                   where the rows will go, so the grid keeps its head and total
                   band and the rest of the panel stays usable (same localised
                   loader the VAS_020 widget uses for a page fetch). */
                $grid.append($('<div class="' + CLS + 'dgBusy">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                    '</div>'));
            }

            $grid.append(totalRow());
            $sec.append($grid);

            if (pageCount > 1) $sec.append(pager(linesPage, pageCount));
            return $sec;
        }

        function lineRow(line, isLast) {
            var dr = +line.AmtSourceDr || 0;
            var cr = +line.AmtSourceCr || 0;
            /* A line is a debit when it carries a debit amount; a zero line
               reads as a credit only when it actually has a credit. */
            var isDebit = dr !== 0 || cr === 0;

            var $r = $('<div class="' + CLS + 'dgRow' + (isLast ? " " + CLS + "last" : "") + '"></div>');

            var $ic = $('<span class="' + CLS + 'dgIcon ' + CLS + (isDebit ? "isDebit" : "isCredit") + '"></span>')
                .attr("title", isDebit ? msg("VAS_291_DebitLine", "Debit line") : msg("VAS_291_CreditLine", "Credit line"));
            $ic.append(icon(isDebit ? "debit" : "credit"));
            $r.append($ic);

            var $cell = $('<span class="' + CLS + 'dgCell"></span>');
            var primary = line.AccountName || line.AccountValue || "";
            var primaryTitle = joinBits([line.AccountValue, line.AccountName, line.Description]);
            $cell.append(cell("dgPrimary", primary, primaryTitle));
            /* Account code, then the dimension codes when there are any; a line
               without dimensions shows the code alone - no placeholder. */
            var dims = (line.Dimensions && line.Dimensions.length) ? line.Dimensions.join(" · ") : "";
            var meta = joinBits([line.AccountValue, dims]);
            $cell.append(cell("dgMeta", meta, meta));
            $r.append($cell);

            $r.append(amountCell(dr, amount(dr), msg("VAS_291_NoDebit", "No debit on this line")));
            $r.append(amountCell(cr, amount(cr), msg("VAS_291_NoCredit", "No credit on this line")));
            return $r;
        }

        /* A zero leg reads as an em dash rather than as "0.00", so the eye
           lands on the side that actually moved. */
        function amountCell(value, text, emptyTitle) {
            if (!value) {
                return $('<span class="' + CLS + 'dgAmount ' + CLS + 'isEmpty"></span>').text("—").attr("title", emptyTitle);
            }
            return $('<span class="' + CLS + 'dgAmount"></span>').text(text).attr("title", text);
        }

        function totalRow() {
            var dr = +data.LinesTotalDr || 0;
            var cr = +data.LinesTotalCr || 0;
            var balanced = Math.abs(dr - cr) < 0.000001;
            var $t = $('<div class="' + CLS + 'dgTotal"></div>');
            var label = joinBits([
                msg("VAS_291_DocumentTotal", "Document total"),
                balanced ? msg("VAS_291_TotalBalanced", "balanced") : msg("VAS_291_TotalUnbalanced", "out of balance")
            ]);
            var labelTitle = joinBits([label, fmt(msg("VAS_291_LineCount", "{0} lines"), lineCount())]);
            $t.append($('<span class="' + CLS + 'dgTotalLabel"></span>').text(label).attr("title", labelTitle));
            $t.append($('<span class="' + CLS + 'dgTotalValue"></span>').text(amount(dr)).attr("title", amount(dr)));
            $t.append($('<span class="' + CLS + 'dgTotalValue"></span>').text(amount(cr)).attr("title", amount(cr)));
            return $t;
        }

        /* Canonical footer pager (same shape as the VAS_020 widget pager):
           "Showing a–b of N" on the left, compact  <  n of m  >  on the right. */
        function pager(page, pageCount) {
            var total = lineCount();
            var from = total ? page * linesPerPage() + 1 : 0;
            var to = Math.min(total, (page + 1) * linesPerPage());
            var of = msg("VAS_291_Of", "of");

            var $p = $('<div class="' + CLS + 'pager"></div>');
            var showing = msg("VAS_291_Showing", "Showing") + " " + from + "–" + to + " " + of + " " + total;
            $p.append($('<span class="' + CLS + 'pgInfo"></span>').text(showing).attr("title", showing));

            var $nav = $('<div class="' + CLS + 'pgNav"></div>');
            var $prev = $('<button type="button" class="' + CLS + 'pgBtn"></button>')
                .attr("data-action", "lines-prev")
                .attr("title", msg("VAS_291_Previous", "Previous page"))
                .attr("aria-label", msg("VAS_291_Previous", "Previous page"))
                .prop("disabled", page <= 0);
            $prev.append(icon("prev"));
            var $next = $('<button type="button" class="' + CLS + 'pgBtn"></button>')
                .attr("data-action", "lines-next")
                .attr("title", msg("VAS_291_Next", "Next page"))
                .attr("aria-label", msg("VAS_291_Next", "Next page"))
                .prop("disabled", page >= pageCount - 1);
            $next.append(icon("next"));
            $nav.append($prev);
            $nav.append($('<span class="' + CLS + 'pgLabel"></span>').text((page + 1) + " " + of + " " + pageCount));
            $nav.append($next);
            $p.append($nav);
            return $p;
        }

        /* Only the lines section is repainted, and only in place: the rest of
           the panel stays as it is. On a page change the panel is scrolled so
           the grid's column head and first row sit at the top of the view -
           the user lands on the first row of the new page, not wherever the
           old page's pager happened to be. */
        function repaintLines(scrollToFirstRow) {
            if (!$body) return;
            var $old = $body.children("." + CLS + "lines").first();
            var $new = renderLines();
            if (!$old.length || !$new) return;
            $old.replaceWith($new);
            if (scrollToFirstRow) scrollLinesToTop($new);
        }

        /* Scrolls the panel (the root is the scroller in this host) so the
           lines grid's head - and therefore its first row - is at the top. */
        function scrollLinesToTop($sec) {
            var root = $root && $root[0];
            var head = $sec.find("." + CLS + "dgHead")[0];
            if (!root || !head) return;
            var rootRect = root.getBoundingClientRect();
            var headRect = head.getBoundingClientRect();
            var target = root.scrollTop + (headRect.top - rootRect.top);
            /* A little room above the head so it does not sit hard on the edge. */
            root.scrollTop = Math.max(0, target - root.clientHeight * 0.02);
        }

        /* Moves the pager. A page already fetched is painted at once; any other
           is asked from the server (50 rows per request) and painted when it
           lands - unless the record changed meanwhile, in which case the reply
           belongs to a journal the panel has already left and is dropped. */
        function pageLines(delta) {
            var next = linesPage + delta;
            if (next < 0 || next > linePageCount() - 1) return;
            linesPage = next;
            repaintLines(true);
            if (linePages.hasOwnProperty(next)) return;

            var recordId = +data.GL_Journal_ID;
            var token = ++linesFetchToken;
            var mainToken = fetchToken;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_291_GLJournalRightPanel/GetJournalLines",
                type: "GET",
                dataType: "json",
                data: { GL_Journal_ID: recordId, page: next, pageSize: linesPerPage() },
                success: function (raw) {
                    if (token !== linesFetchToken || mainToken !== fetchToken || !data || +data.GL_Journal_ID !== recordId) return;
                    var page = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    linePages[next] = (page && page.Rows) ? page.Rows : [];
                    if (linesPage === next) repaintLines(true);
                },
                error: function (err) {
                    if (window.console) console.log(err);
                    if (token !== linesFetchToken || mainToken !== fetchToken) return;
                    /* Back to the last page that is actually on hand. */
                    linesPage = Math.max(0, next - delta);
                    repaintLines(false);
                    error(msg("VAS_291_LoadFailed", "Could not load the journal details."));
                }
            });
        }

        /* 9 · Approval routing rail ---------------------------------------- */

        /* Composes the routing from the facts the server reports. Titles are
           localised here; the server only tells what happened.
             1. Prepared            - always done (the record exists)
             2. workflow steps      - one per human workflow activity
             3. Completed           - document status
             4. Posted to ledger    - posting status + moment + batch
           Exactly one step is active: the first that is neither done nor
           blocked. Every step after a blocked one is pending. */
        function buildSteps() {
            var steps = [];

            steps.push({
                title: msg("VAS_291_Prepared", "Prepared"),
                meta: joinBits([data.CreatedByName, fmtStamp(data.Created)]),
                state: "done"
            });

            var wf = data.WorkflowSteps || [];
            for (var i = 0; i < wf.length; i++) {
                var w = wf[i];
                var state = "pending";
                if (w.WFState === "CC") state = "done";
                else if (w.WFState === "OR" || w.WFState === "OS") state = "active";
                else if (w.WFState === "CA" || w.WFState === "CT") state = "blocked";
                steps.push({
                    title: w.NodeName || w.WFStateName || "",
                    meta: joinBits([w.ActorName, fmtStamp(state === "done" ? (w.Updated || w.Created) : w.Created), state === "done" ? "" : w.WFStateName]),
                    state: state
                });
            }

            if (isReversedOrVoided()) {
                steps.push({
                    title: data.DocStatusName || data.DocStatus,
                    meta: joinBits([data.UpdatedByName, fmtStamp(data.Updated)]),
                    state: "blocked"
                });
            } else if (isCompletedOrClosed()) {
                steps.push({
                    title: msg("VAS_291_Completed", "Completed"),
                    meta: joinBits([data.DocStatusName, data.UpdatedByName]),
                    state: "done"
                });
            } else {
                steps.push({
                    title: msg("VAS_291_Completed", "Completed"),
                    meta: joinBits([data.DocStatusName, msg("VAS_291_AwaitingCompletion", "awaiting completion")]),
                    state: "active"
                });
            }

            if (isPosted()) {
                steps.push({
                    title: msg("VAS_291_PostedToLedger", "Posted to ledger"),
                    meta: joinBits([msg("VAS_291_System", "System"), fmtStamp(data.PostedOn), data.BatchDocumentNo]),
                    state: "done"
                });
            } else if (isPostError()) {
                steps.push({
                    title: msg("VAS_291_PostedToLedger", "Posted to ledger"),
                    meta: data.PostedName || data.Posted,
                    state: "blocked"
                });
            } else {
                steps.push({
                    title: msg("VAS_291_PostedToLedger", "Posted to ledger"),
                    meta: data.PostedName || msg("VAS_291_NotPostedYet", "not posted yet"),
                    state: "active"
                });
            }

            /* One active step at most: the first one that is still open. Once a
               step is blocked nothing after it can be reached, so those read as
               pending too. */
            var activeSeen = false, blockedSeen = false;
            for (var s = 0; s < steps.length; s++) {
                if (steps[s].state === "blocked") { blockedSeen = true; continue; }
                if (steps[s].state === "done") continue;
                if (blockedSeen || activeSeen) steps[s].state = "pending";
                else { steps[s].state = "active"; activeSeen = true; }
            }
            return steps;
        }

        /* Drawn as the rail the payment panel (VAS_191) uses - a marker node per
           step, a hairline connector, title + meta in plain text, no tinted
           card. The node carries the state: done = filled green check,
           active = blue ring with a dot, pending = grey ring, blocked = filled
           red cross. */
        function renderRouting() {
            var steps = buildSteps();
            var done = 0;
            for (var i = 0; i < steps.length; i++) if (steps[i].state === "done") done++;

            var $sec = $('<section class="' + CLS + 'sec"></section>');
            $sec.append(sectionHeader(
                msg("VAS_291_ApprovalRouting", "Approval routing"),
                fmt(msg("VAS_291_StepsComplete", "{0} of {1} complete"), done, steps.length),
                null));

            var $rail = $('<div class="' + CLS + 'vsteps"></div>');
            for (var j = 0; j < steps.length; j++) {
                var st = steps[j];
                var $step = $('<div class="' + CLS + 'vstep ' + CLS + 'is-' + st.state + '"></div>');

                var $r = $('<div class="' + CLS + 'rail"></div>');
                var $node = $('<div class="' + CLS + 'node"></div>');
                if (st.state === "done") $node.append(icon("check"));
                else if (st.state === "blocked") $node.append(icon("cross"));
                else if (st.state === "active") $node.append($('<span class="' + CLS + 'nodeDot"></span>'));
                $r.append($node);
                /* The connector is drawn by every step but the last, which has
                   nothing below it to connect to. */
                if (j < steps.length - 1) $r.append($('<div class="' + CLS + 'line"></div>'));
                $step.append($r);

                var $c = $('<div class="' + CLS + 'vc"></div>');
                $c.append($('<div class="' + CLS + 'st"></div>').text(st.title).attr("title", st.title));
                if (st.meta) $c.append($('<div class="' + CLS + 'mt"></div>').text(st.meta).attr("title", st.meta));
                $step.append($c);

                $rail.append($step);
            }
            $sec.append($rail);
            return $sec;
        }

        /* ---------------------------------------------------------------- */
        /*  Actions                                                         */
        /* ---------------------------------------------------------------- */

        /* AD_Process_ID / AD_Table_ID for the print flow, read off the current
           grid tab. Without the tab there is no print process, and both pills
           are drawn disabled. */
        function printContext() {
            var tab = $self.curTab;
            return {
                AD_Process_ID: (tab && typeof tab.getAD_Process_ID === "function") ? tab.getAD_Process_ID() : 0,
                AD_Table_ID: (tab && typeof tab.getAD_Table_ID === "function") ? tab.getAD_Table_ID() : ($self.table_ID || 0),
                RecordID: $self.record_ID
            };
        }

        /* Generates the journal document through the framework print process
           and hands the produced file to the callback. Identical path to the AR
           invoice and payment panels - only the origin name differs. */
        function generatePdf(onFile) {
            if (!$self.record_ID || !$self.curTab) return;

            var pc = printContext();
            if (!pc.AD_Process_ID || !pc.AD_Table_ID) {
                error(msg("VAS_291_ActionFailed", "The action could not be completed."));
                return;
            }

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "JsonData/GeneratePrint/",
                dataType: "json",
                data: {
                    AD_Process_ID: pc.AD_Process_ID,
                    Name: "Print",
                    AD_Table_ID: pc.AD_Table_ID,
                    Record_ID: pc.RecordID,
                    WindowNo: $self.windowNo,
                    filetype: "P",                 // P = PDF
                    actionOrigin: "W",
                    originName: "GLJournal"
                },
                success: function (raw) {
                    showBusy(false);
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!res) return;

                    /* GeneratePrint writes to TempDownload and answers with the file
                       name; anything else is a failure worth naming. */
                    var file = res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path;
                    if (!file) {
                        var reason = (res.ErrorText)
                            || (res.ReportProcessInfo && res.ReportProcessInfo.Summary)
                            || msg("VAS_291_ActionFailed", "The action could not be completed.");
                        error(reason);
                        if (window.console) console.log("GeneratePrint response:", res);
                        return;
                    }
                    onFile(VIS.Application.contextUrl + file);
                },
                error: function (err) {
                    if (window.console) console.log(err);
                    showBusy(false);
                    error(msg("VAS_291_ActionFailed", "The action could not be completed."));
                }
            });
        }

        /* Print voucher: the PDF is loaded into a hidden same-origin frame and
           the browser's print dialog is raised on it. Where the frame cannot
           print (no inline PDF viewer) the file opens in a new tab instead, so
           the user still gets the document. */
        function printVoucher() {
            generatePdf(function (url) {
                clearPrintFrames();
                var frame = document.createElement("iframe");
                frame.setAttribute("aria-hidden", "true");
                frame.setAttribute("title", msg("VAS_291_PrintVoucher", "Print voucher"));
                frame.style.position = "fixed";
                frame.style.width = "0";
                frame.style.height = "0";
                frame.style.border = "0";
                frame.style.visibility = "hidden";
                frame.onload = function () {
                    try {
                        frame.contentWindow.focus();
                        frame.contentWindow.print();
                    } catch (e) {
                        window.open(url, "_blank");
                    }
                };
                printFrames.push(frame);
                document.body.appendChild(frame);
                frame.src = url;
            });
        }

        function clearPrintFrames() {
            for (var i = 0; i < printFrames.length; i++) {
                try { if (printFrames[i].parentNode) printFrames[i].parentNode.removeChild(printFrames[i]); } catch (e) { }
            }
            printFrames = [];
        }

        /* Download PDF: a same-origin link with the download attribute saves
           the file instead of opening it; browsers that ignore the attribute
           open it, which is the next best thing. */
        function downloadPDF() {
            generatePdf(function (url) {
                var a = document.createElement("a");
                a.href = url;
                a.download = (data && data.DocumentNo ? data.DocumentNo : "journal") + ".pdf";
                a.target = "_blank";
                a.rel = "noopener";
                a.style.display = "none";
                document.body.appendChild(a);
                try { a.click(); } catch (e) { window.open(url, "_blank"); }
                document.body.removeChild(a);
            });
        }

        /* The APanel hosting this panel's window. The host never hands it over
           directly, but it registers its GridController as a data-status
           listener on the tab (GridController.initGrid -> gTab.addDataStatusListener),
           and that controller carries the APanel as .aPanel. Found by the tab
           it serves, never by class name, so a renamed bundle cannot break it. */
        function hostPanel() {
            var tab = $self.curTab;
            if (!tab) return null;
            var list = tab.mDataListenerList;
            if (!list || !list.length) return null;
            for (var i = 0; i < list.length; i++) {
                var l = list[i];
                if (l && l.aPanel && l.gTab === tab && typeof l.aPanel.onTabChange === "function") {
                    return l.aPanel;
                }
            }
            return null;
        }

        /* The journal's own line tab in the hosting window: the first tab whose
           table is GL_JournalLine (id from the server, never hard-coded) that
           sits directly under the current tab. Falls back to the first line
           tab of the window when levels cannot be read. */
        function findLineTab(aPanel) {
            var lineTableId = data ? +data.LineTable_ID : 0;
            if (!aPanel || !aPanel.gridWindow || typeof aPanel.gridWindow.getTabs !== "function" || lineTableId <= 0) return null;
            var tabs = aPanel.gridWindow.getTabs() || [];
            var cur = $self.curTab;
            var curLevel = (cur && typeof cur.getTabLevel === "function") ? +cur.getTabLevel() : 0;
            var fallback = null;
            for (var i = 0; i < tabs.length; i++) {
                var t = tabs[i];
                if (!t || typeof t.getAD_Table_ID !== "function" || +t.getAD_Table_ID() !== lineTableId) continue;
                var level = (typeof t.getTabLevel === "function") ? +t.getTabLevel() : curLevel + 1;
                if (level === curLevel + 1) return t;
                if (!fallback) fallback = t;
            }
            return fallback;
        }

        /* Switches the HOSTING window to the journal's line tab - exactly what a
           click on that tab header does (the APanel registers every tab as the
           action windowNo_AD_Tab_ID and routes the header click through
           onTabChange). The current record stays selected, so the line tab
           opens on this journal's lines. No second window instance is ever
           opened. Degrades silently: a click can never throw. */
        function openLineDetails() {
            if (!data || !(+data.GL_Journal_ID > 0)) return;
            try {
                var aPanel = hostPanel();
                var lineTab = aPanel ? findLineTab(aPanel) : null;
                if (!lineTab || typeof lineTab.getAD_Tab_ID !== "function") {
                    if (window.console) console.log("VAS_291: line tab not found in the hosting window");
                    return;
                }
                aPanel.onTabChange($self.windowNo + "_" + lineTab.getAD_Tab_ID());
            } catch (e) { if (window.console) console.log(e); }
        }

        this.clearPrintFrames = clearPrintFrames;

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_291_GLJournalRightPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
        /* Watch the tab itself so New Record / Copy Record (neither of which
           reliably calls refreshPanelData) still empties the panel. */
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update the tab panel for the selected record. */
    VAS.VAS_291_GLJournalRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        /* The insert check is what makes New Record / Copy Record behave: the id
           handed in for an unsaved row can still be the previously selected (or
           copied-from) journal's, so the tab's own insert state decides. */
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        /* Held rather than fetched outright: the insert flag is not always up yet
           when we get here, so scheduleFetch asks once more before loading. */
        this.scheduleFetch(recordID);
    };

    /* The platform Refresh button - exposed on the prototype as well as on the
       instance, since the host may reach either. */
    VAS.VAS_291_GLJournalRightPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) this.fetchData(this.record_ID);
        else this.clear();
    };

    /* Set width as per window width */
    VAS.VAS_291_GLJournalRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_291_GLJournalRightPanel.prototype.dispose = function () {
        /* Kill any held fetch first - its timer would otherwise fire against a
           panel whose curTab has just been nulled out below. */
        if (typeof this.abortPendingFetch === "function") {
            try { this.abortPendingFetch(); } catch (e) { }
        }
        if (typeof this.clearPrintFrames === "function") {
            try { this.clearPrintFrames(); } catch (e) { }
        }
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
