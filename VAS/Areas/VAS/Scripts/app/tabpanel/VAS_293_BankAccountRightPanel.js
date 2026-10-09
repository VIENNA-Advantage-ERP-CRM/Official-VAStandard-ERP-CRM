/************************************************************
 * Module Name    : VAS
 * Purpose        : Bank Account right panel (VAS.VAS_293_BankAccountRightPanel).
 *                  A read-only overview of the C_BankAccount record selected in
 *                  the hosting Bank window:
 *
 *                    1. Panel header      - eyebrow, the bank as the headline
 *                                           identity, the account name / type as
 *                                           its subtitle, and the active state as
 *                                           a dotted status badge.
 *                    2. Bank & account    - account no., currency, IBAN, routing
 *                       details              no., Swift code, organisation, default
 *                                           account, and the bank ADDRESS LAST.
 *                    3. Account position  - the CURRENT BALANCE as the panel's
 *                                           headline figure with the currency
 *                                           beside it, then opening / unmatched
 *                                           balance and the credit limit as stat
 *                                           cards.
 *                    4. Statement &       - latest account line, its ending
 *                       reconciliation      balance, the unmatched balance and the
 *                                           reconciliation state.
 *                    5. Linked            - one row per ACTIVE child tab of the
 *                       configuration       hosting window, with its record count
 *                                           and a short detail. Clicking a row
 *                                           switches the window to that tab with
 *                                           this account still selected - EXCEPT
 *                                           for Bank Account Document and Statement
 *                                           Class, which expand a detail table in
 *                                           place instead of navigating.
 *
 *                  Inline detail (rows 5a):
 *                    - only C_BankAccountDoc and VA012_BankStatementClass expand;
 *                      the other four rows keep their tab navigation untouched;
 *                    - a row with no active record does not expand at all - it
 *                      stays in its "Not configured" state;
 *                    - tables start hidden and are never auto-opened;
 *                    - the rows are fetched on the FIRST open and cached for the
 *                      account, so closing and reopening costs no request, and
 *                      opening one never reloads the panel;
 *                    - the row is the disclosure control: aria-expanded /
 *                      aria-controls, an accented card while open, and the SAME
 *                      chevron every other row wears, turned to point down;
 *                    - the detail reads active AND inactive rows, because the
 *                      tables carry a Status column;
 *                    - the NAME cell of each detail row is a link that switches
 *                      this window to the record's own tab and selects its row
 *                      - no window or tab id is named anywhere in this panel.
 *
 *                  Row order is the PANEL's, set by the model's DISPLAY_ORDER:
 *                  Account Line, Bank Account Document, Statement Class, Payment
 *                  Processor, Statement Loader, Default Accounting - NOT the
 *                  window's AD_Tab.SeqNo.
 *
 *                  Payment Processor and Statement Loader are dropped entirely
 *                  when they hold nothing: an account without either is the
 *                  ordinary case, not an omission worth a line. The other four
 *                  always show, because "Not configured" IS news for them. The
 *                  section summary counts the rows actually drawn, so it can
 *                  never be read against a different number than is on screen.
 *
 *                  The panel is a tab panel: the framework loads it by the
 *                  AD_Tab class name, so it is NOT imported by VASjs.js on its
 *                  own account - it is bundled there with the other numbered
 *                  panels so the file ships, and instantiated by name.
 *
 *                  Nothing is hard-coded per environment. The hosting window is
 *                  read from the panel's own tab (curTab.getAD_Window_ID()), the
 *                  configuration rows and their AD_Tab_IDs come from that
 *                  window's metadata, and the row click is the framework's own
 *                  tab change - the same call a click on the tab header makes.
 *
 *                  Design contract (design.md > windows-and-panels.md):
 *                    - the panel root IS the scrolling body in this host, and
 *                      carries the canonical anchor clamp(16px, 1vw, 18px);
 *                    - composition is identity header -> headered sections, four
 *                      in total;
 *                    - primitives used: Section Header, Metric Grid (Detail Card
 *                      wrap), Stat Grid, Entity List.
 *                  The host owns the panel CHROME (title, close, collapse strip);
 *                  this file draws the body only. The header block at the top of
 *                  the body is CONTENT - the record's identity, not a second
 *                  window bar: it carries no close, collapse or resize affordance.
 *
 *                  RTL: every amount and every digit range is emitted as a
 *                  bidi-isolated left-to-right token, so a leading minus stays in
 *                  front of the figure in Arabic instead of being pushed to the
 *                  far end. The chevron mirrors, and the scroller is stepped in
 *                  from the panel edge so its scrollbar clears the host's resize
 *                  handle (see the stylesheet).
 *
 * Chronological development:
 *   VAI145   2026-09-22  Created.
 *   VAI145   2026-09-23  Header, Bank & account details, Account position and
 *                        Statement & reconciliation redesigned to the approved
 *                        reference: the tinted hero card is replaced by a flat
 *                        identity header, the current balance moves down to
 *                        Account position as its emphasis figure, the details
 *                        grid gains monospaced identifiers and ends on the bank
 *                        address, and the statement facts read as a dashed
 *                        label / value list.
 *   VAI145   2026-09-23  Linked configuration: Bank Account Document and Statement
 *                        Class now expand an inline detail table instead of
 *                        navigating, loaded on demand and cached per account.
 *   VAI145   2026-09-23  Bank Account Document detail gains the process name;
 *                        Statement Class keeps VA012_BankStatementClassName and
 *                        drops File required.
 *   VAI145   2026-09-23  Expanded rows redrawn to the approved reference: the row
 *                        and its table share one bordered card, Next no. and End
 *                        check no. merge into a single Cheque series column, the
 *                        block's own title strip is gone (the row above it already
 *                        names it and carries the count), and the caret sits in a
 *                        bordered button.
 *   VAI145   2026-09-23  Linked configuration redrawn for EVERY row: each row is
 *                        its own card, the status pill gives way to a check / dash
 *                        state tile, title and summary flow inline, and the count
 *                        sits in a badge. Bank Account Document detail adds the
 *                        cheque series (StartChkNumber - EndChkNumber) with the
 *                        current number and the priority as cell sub-lines;
 *                        Statement Class detail adds the VA012_StatementClass
 *                        master name beside the row's own.
 *   VAI145   2026-09-23  Expandable rows wear the same trailing chevron as every
 *                        other row - the bordered caret button is gone, and the
 *                        open state is carried by the card's accent and the
 *                        chevron's rotation. Cheque series left-aligns with the
 *                        rest of the table; no column is right-aligned any more.
 *   VAI145   2026-09-23  Detail name cells (Document, Statement class) became
 *                        links that zoom to their own record, and the linked
 *                        configuration list moved to a fixed panel order.
 *   VAI145   2026-09-23  Payment Processor and Statement Loader are hidden when
 *                        they hold no record; the section summary counts only the
 *                        rows drawn. Detail links are underlined at rest.
 *   VAI145   2026-09-25  Detail links fixed: VIS.AEnv.zoom did nothing for child
 *                        tables (AD_Table.AD_Window_ID is empty for them). They
 *                        now switch the current window to the record's own tab
 *                        and select its row - no new window is opened.
 *   VAI145   2026-09-25  Returning to the Bank Account tab re-reads the panel
 *                        (refreshPanelData now reloads a record already on
 *                        screen), so rows added on the Bank Account Document /
 *                        Statement Class tabs show up. Expanded detail tables
 *                        stay open across that reload and are re-read too.
 *
 * -- Labels / Message Keys ---------------------------------------------------
 *  Panel
 *   No bank account selected                    | VAS_293_NoData
 *   Could not load the bank account details.    | VAS_293_LoadFailed
 *
 *  Header
 *   Bank account                                | VAS_293_Eyebrow
 *   Active                                      | VAS_293_Active
 *   Inactive                                    | VAS_293_Inactive
 *
 *  Bank & account details
 *   Bank & account details                      | VAS_293_BankAccountDetails
 *   Account no.                                 | VAS_293_AccountNo
 *   Currency                                    | VAS_293_Currency
 *   IBAN                                        | VAS_293_IBAN
 *   Routing no.                                 | VAS_293_RoutingNo
 *   Swift code                                  | VAS_293_SwiftCode
 *   Organization                                | VAS_293_Organization
 *   Default account                             | VAS_293_DefaultAccount
 *   Bank address                                | VAS_293_BankAddress
 *   Yes                                         | VAS_293_Yes
 *   No                                          | VAS_293_No
 *
 *  Account position
 *   Account position                            | VAS_293_AccountPosition
 *   Current balance                             | VAS_293_CurrentBalance
 *   Opening balance                             | VAS_293_OpeningBalance
 *   Unmatched balance                           | VAS_293_UnmatchedBalance
 *   Credit limit                                | VAS_293_CreditLimit
 *   Not defined in this environment             | VAS_293_NotDefined
 *
 *  Statement & reconciliation
 *   Statement & reconciliation                  | VAS_293_StatementRecon
 *   Last statement                              | VAS_293_LastStatement
 *   Statement balance                           | VAS_293_StatementBalance
 *   Reconciliation status                       | VAS_293_ReconciliationStatus
 *   {0} account lines                           | VAS_293_AccountLineCount
 *   Reconciled                                  | VAS_293_Reconciled
 *   Needs review                                | VAS_293_NeedsReview
 *   No statement                                | VAS_293_NoStatement
 *
 *  Linked configuration
 *   Linked configuration                        | VAS_293_LinkedConfiguration
 *   {0} of {1} configured                       | VAS_293_ConfiguredCount
 *   Configured                                  | VAS_293_Configured
 *   Not configured                              | VAS_293_NotConfigured
 *   Ending balance {0}                          | VAS_293_EndingBalanceMeta
 *   Last run {0}                                | VAS_293_LastRun
 *   Never run                                   | VAS_293_NeverRun
 *   Open this tab                               | VAS_293_OpenTab
 *   No linked configuration tabs are active.    | VAS_293_NoLinkedTabs
 *
 *  Inline configuration detail
 *   Show details                                | VAS_293_ShowDetail
 *   Hide details                                | VAS_293_HideDetail
 *   Loading...                                  | VAS_293_Loading
 *   Could not load the configuration details.   | VAS_293_DetailLoadFailed
 *   Open this record    (link tooltip)          | VAS_293_OpenRecord
 *   Status                                      | VAS_293_ColStatus
 *   Active / Inactive  (shared with the header) | VAS_293_Active / _Inactive
 *    Bank account document details
 *     Document                                  | VAS_293_ColDocument
 *     Payment method                            | VAS_293_ColPaymentMethod
 *     Cheque series                             | VAS_293_ColChequeSeries
 *     Process                                   | VAS_293_ColProcess
 *     Priority {0}        (cell sub-line)       | VAS_293_PriorityMeta
 *     Current {0}         (cell sub-line)       | VAS_293_CurrentNoMeta
 *     No Bank Account Document configuration…   | VAS_293_NoDocDetail
 *    Statement class details
 *     Statement class                           | VAS_293_ColStatementClass
 *     Class                                     | VAS_293_ColClass
 *     No Statement Class configuration found.   | VAS_293_NoClassDetail
 * ---------------------------------------------------------------------------
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Physical tables behind the six configuration tabs. Kept in step with the
       constants of VAS_293_BankAccountRightPanelModel - the server sends the
       table name, and the panel keys its icon and its one special case off it. */
    var TBL_ACCOUNT_LINE = "C_BankAccountLine";
    var TBL_ACCOUNT_DOC = "C_BankAccountDoc";
    var TBL_PAYMENT_PROCESSOR = "C_PaymentProcessor";
    var TBL_STATEMENT_LOADER = "C_BankStatementLoader";
    var TBL_STATEMENT_CLASS = "VA012_BankStatementClass";
    var TBL_DEFAULT_ACCOUNTING = "FRPT_BankAccount_Acct";

    /* Reconciliation state codes as the model sends them - never display text. */
    var RECON_RECONCILED = "RECONCILED";
    var RECON_REVIEW = "REVIEW";
    var RECON_NO_STATEMENT = "NOSTMT";

    // True when the tab sits on a row that has not been saved yet - whether it
    // came from New Record or from Copy Record. The authority is the GRID
    // TABLE's insert flag: GridTab does not expose it, it only holds the table
    // as .gridTable, so asking the tab itself always answers "no". The record id
    // cannot answer it either: a copied row still carries the SOURCE record's
    // key.
    function isTabInserting(curTab) {
        if (!curTab) return false;
        try {
            var gt = curTab.gridTable;
            if (gt) {
                if (typeof gt.getIsInserting === "function" && gt.getIsInserting()) return true;
                if (gt.mInserting === true) return true;
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

    VAS.VAS_293_BankAccountRightPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;

        /* Rows of the inline detail tables, keyed by table name, for the account
           currently on screen. Opening a row fills its entry; closing and reopening
           it costs nothing more. Emptied whenever the panel changes record, so a
           table can never show the previous account's configuration. */
        var detailCache = {};

        var CLS = "vas_293-";

        var disposed = false;

        /* The C_BankAccount_ID the panel is showing OR loading. 0 = nothing. */
        var shownRecordId = 0;

        /* How long refreshPanelData holds before it fetches. On New Record /
           Copy Record the framework can call it BEFORE GridTable raises its
           insert flag, so asking at that instant answers "no". Asking again
           after this pause gets the truth, and it collapses a burst of
           arrow-key row changes into one request. */
        var REFRESH_DELAY_MS = 150;
        /* Raised by every fetch, scheduled fetch and clear. A reply carrying a
           stale token belongs to a record the panel has already left, so it is
           dropped instead of painting over the newer one. */
        var fetchToken = 0;
        var pendingFetch = null;
        /* A request is on the wire. Together with pendingFetch and shownRecordId
           this is what lets the panel tell "already loading this record" from
           "idle", so the two host entry points cannot each start their own load
           for the same selection. */
        var inFlight = false;

        /* ---------------------------------------------------------------- */
        /*  Icons - inline SVG only; the host shell may not carry an icon font */
        /* ---------------------------------------------------------------- */

        var SVG_ATTR = 'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"';
        var SVG = {
            chevron: '<svg ' + SVG_ATTR + '><path d="m9 18 6-6-6-6"/></svg>',
            /* The two states a configuration row can be in. The TILE carries them,
               so the row needs no status pill beside its title. */
            check: '<svg ' + SVG_ATTR + '><path d="M20 6 9 17l-5-5"/></svg>',
            dash: '<svg ' + SVG_ATTR + '><path d="M5 12h14"/></svg>'
        };

        function icon(name) {
            return $('<span class="' + CLS + 'ic"></span>').html(SVG[name] || "");
        }

        /* ---------------------------------------------------------------- */
        /*  Messages                                                        */
        /* ---------------------------------------------------------------- */

        // Prefer the seeded AD_Message; else a readable English default; else the
        // key. VIS.Msg answers an unseeded key with the key BRACKETED, which is
        // what isMissingMsg looks for - an unseeded panel still reads as English
        // rather than as a wall of identifiers.
        function msg(key, fallback) {
            try {
                if (window.VIS && VIS.Msg && typeof VIS.Msg.getMsg === "function") {
                    var text = VIS.Msg.getMsg(key);
                    if (text && text !== key && !isMissingMsg(text)) return text;
                }
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

        /* An amount is ONE left-to-right token: "-CHF 2,497.08". Its sign and its
           separators are bidi-neutral characters, so in an Arabic (RTL) panel the
           bidi algorithm resolves them to the paragraph direction and parks them
           at the far end - the figure renders "CHF 2,497.08-", with the sign
           reading as a trailing mark. LRI … PDI lays the token out left-to-right
           and isolates it from whatever sits around it.

           Done here rather than with `direction: ltr` in the stylesheet because an
           amount is not always alone in its element: it is composed into
           translated sentences ("Ending balance {0}", "Last run {0}") where
           pinning the whole element to LTR would mis-lay the Arabic words with
           it. The marks are invisible and inert in an LTR UI. */
        var LRI = String.fromCharCode(0x2066);  /* LEFT-TO-RIGHT ISOLATE */
        var PDI = String.fromCharCode(0x2069);  /* POP DIRECTIONAL ISOLATE */

        function ltrToken(text) {
            var s = (text === null || text === undefined) ? "" : String(text);
            return s.length ? LRI + s + PDI : "";
        }

        /* The bare token, for the callers that put something in front of it and
           have to isolate the result as a whole rather than twice. */
        function rawMoney(value, symbol, precision) {
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

        /* Amounts carry the ACCOUNT currency's symbol and precision - never a
           hard-coded symbol; the user's locale decides grouping and separators. */
        function money(value) {
            if (!data) return "";
            return ltrToken(rawMoney(value, data.CurSymbol || data.CurISO, data.StdPrecision));
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
                return ltrToken(d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                }));
            } catch (e) {
                return ltrToken(d.toDateString());
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

        function yesNo(flag) {
            return flag ? msg("VAS_293_Yes", "Yes") : msg("VAS_293_No", "No");
        }

        /* Display rule: a missing text reads as an em dash, never as "null",
           "undefined" or a misleading zero. */
        function orDash(text) {
            return (text === null || text === undefined || String(text).length === 0) ? "—" : text;
        }

        /* ---------------------------------------------------------------- */
        /*  Record state                                                    */
        /* ---------------------------------------------------------------- */

        function reconState() {
            return (data && data.ReconciliationState) ? data.ReconciliationState : RECON_NO_STATEMENT;
        }

        function reconLabel() {
            var s = reconState();
            if (s === RECON_RECONCILED) return msg("VAS_293_Reconciled", "Reconciled");
            if (s === RECON_REVIEW) return msg("VAS_293_NeedsReview", "Needs review");
            return msg("VAS_293_NoStatement", "No statement");
        }

        function reconTone() {
            var s = reconState();
            if (s === RECON_RECONCILED) return "success";
            if (s === RECON_REVIEW) return "warning";
            return "neutral";
        }

        function configuredCount(list) {
            var n = 0;
            for (var i = 0; i < list.length; i++) {
                if (list[i] && list[i].IsConfigured) n++;
            }
            return n;
        }

        /* The two configuration rows that are only worth a line when they hold
           something. An account with no payment processor and no statement loader
           is the ordinary case rather than an omission, so an empty row for each
           is noise - whereas "Not configured" IS news for the other four, which
           every account is expected to have. The server still reports all six; it
           is the panel that decides what is worth showing. */
        var HIDE_WHEN_EMPTY = [TBL_PAYMENT_PROCESSOR, TBL_STATEMENT_LOADER];

        function isRowVisible(cfg) {
            if (!cfg) return false;
            for (var i = 0; i < HIDE_WHEN_EMPTY.length; i++) {
                if (cfg.TableName === HIDE_WHEN_EMPTY[i]) return (+cfg.RecordCount || 0) > 0;
            }
            return true;
        }

        /* The rows the section actually draws. The section summary counts from this
           SAME list, so "3 of 4 configured" can never be read against a different
           number of rows than the user can see. */
        function visibleConfigs() {
            var list = (data && data.LinkedConfigs) || [];
            var kept = [];
            for (var i = 0; i < list.length; i++) {
                if (isRowVisible(list[i])) kept.push(list[i]);
            }
            return kept;
        }

        /* ---------------------------------------------------------------- */
        /*  Lifecycle                                                       */
        /* ---------------------------------------------------------------- */

        this.init = function () {
            $root = $('<div class="' + CLS + 'root"></div>');
            $body = $('<div class="' + CLS + 'body"></div>');
            $emptyState = $('<div class="' + CLS + 'empty" style="display:none;"></div>');
            $emptyState.text(msg("VAS_293_NoData", "No bank account selected"));
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
           keyboard the way a button does without extra key handling. Nothing is
           ever detached to body, so $root context always holds. */
        function bindEvents() {
            $root.on("click", "[data-action]", function (e) {
                e.preventDefault();
                var $btn = $(this);
                if ($btn.prop("disabled")) return;
                runAction($btn.attr("data-action"), $btn);
            });
        }

        function runAction(name, $btn) {
            try {
                if (name === "open-tab") openLinkedTab($btn.attr("data-tab"));
                else if (name === "toggle-detail") toggleDetail($btn);
                else if (name === "zoom") zoomToRecord($btn.attr("data-zoom-table"), $btn.attr("data-zoom-id"));
            } catch (e) { if (window.console) console.log(e); }
        }

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
           keeps them from fetching the same record twice. */
        function isCurrent(recordID) {
            var id = +recordID || 0;
            return id > 0 && id === shownRecordId
                   && (data !== null || inFlight || pendingFetch !== null);
        }

        /* True only while this record is WAITING or LOADING - not once it is on
           screen. */
        function isLoading(recordID) {
            var id = +recordID || 0;
            return id > 0 && id === shownRecordId && (inFlight || pendingFetch !== null);
        }

        /* An explicit reload (the platform Refresh button) calls fetchData
           directly and so is never blocked by the guard above.

           `reload` = the framework asked for this record again (refreshPanelData).
           It re-reads a record that is already on screen - coming back from the
           Bank Account Document or Statement Class tab after adding a row there
           re-selects the SAME account, and the panel must show the new count.
           Only a load already on its way for that record is left alone, which
           still collapses the data-status event and refreshPanelData of one row
           click into a single request (the event fires first and schedules). */
        this.scheduleFetch = function (recordID, reload) {
            if (reload ? isLoading(recordID) : isCurrent(recordID)) return;

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
            /* Re-reading the account already on screen: the detail tables the
               user had open are opened again once it has loaded - re-read too,
               the cache is emptied below - rather than collapsing under them. */
            var reopen = (data && +data.C_BankAccount_ID === (+recordID || 0)) ? openDetailTables() : [];

            invalidateFetch();
            var token = fetchToken;
            shownRecordId = +recordID || 0;
            /* Nothing of the previous record may stay on screen while another
               record is loading. The "no account" placeholder is NOT raised: this
               is a load, not an empty selection. */
            data = null;
            detailCache = {};
            if ($body) {
                $body.empty();
                $emptyState.hide();
                $body.show();
            }
            showBusy(true);
            inFlight = true;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_293_BankAccountRightPanel/GetAccountOverview",
                type: "GET",
                dataType: "json",
                data: {
                    C_BankAccount_ID: recordID,
                    /* The hosting window decides which configuration tabs exist.
                       Read from the tab itself, never hard-coded - window ids
                       differ per environment. */
                    AD_Window_ID: hostWindowId()
                },
                success: function (raw) {
                    /* Reply for a record the panel has already left. Whoever
                       superseded us owns the busy indicator and the in-flight
                       flag now, so this reply must not touch either. */
                    if (token !== fetchToken) return;
                    inFlight = false;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (data && !(+data.C_BankAccount_ID > 0)) data = null;   // not accessible / not found
                    render();
                    reopenDetailTables(reopen);
                    showBusy(false);
                },
                error: function (err) {
                    if (token !== fetchToken) return;
                    inFlight = false;
                    if (window.console) console.log(err);
                    data = null;
                    render();
                    showBusy(false);
                    error(msg("VAS_293_LoadFailed", "Could not load the bank account details."));
                }
            });
        };

        this.clear = function () {
            invalidateFetch();
            data = null;
            detailCache = {};
            shownRecordId = 0;
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

            /* Composition rule: identity header -> headered sections, in the
               reference's order. A section with nothing meaningful to say returns
               null and is not drawn. */
            $body.append(renderHeader());
            appendSection(renderDetails());
            appendSection(renderPosition());
            appendSection(renderStatement());
            appendSection(renderLinkedConfig());

            resetScroll();
        }

        function appendSection($sec) {
            if ($sec) $body.append($sec);
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

        /* ---------------------------------------------------------------- */
        /*  Primitives                                                      */
        /* ---------------------------------------------------------------- */

        function section(key) {
            return $('<section class="' + CLS + 'sec"></section>').attr("data-sec", key);
        }

        function sectionHeader(title, summary) {
            var $h = $('<div class="' + CLS + 'secHead"></div>');
            $h.append($('<span class="' + CLS + 'secTitle"></span>').text(title).attr("title", title));
            var $right = $('<span class="' + CLS + 'secRight"></span>');
            if (summary) $right.append($('<span class="' + CLS + 'secSum"></span>').text(summary).attr("title", summary));
            $h.append($right);
            return $h;
        }

        function pill(text, tone) {
            return $('<span class="' + CLS + 'pill ' + CLS + 'tone-' + tone + '"></span>').text(text).attr("title", text);
        }

        /* Status badge: a tinted lozenge carrying a leading state dot, drawn from
           currentColor in the stylesheet so dot and text can never disagree. */
        function badge(text, tone) {
            return $('<span class="' + CLS + 'badge ' + CLS + 'tone-' + tone + '"></span>').text(text).attr("title", text);
        }

        /* Metric Grid cell (Detail Card variant). opts:
             wide - span both columns (long single strings: IBAN, address)
             mono - a machine identifier, set monospaced and isolated LTR
             meta - a qualifier line under the value */
        function metric(label, value, opts) {
            opts = opts || {};
            var $c = $('<div class="' + CLS + 'metric' + (opts.wide ? " " + CLS + "metricWide" : "") + '"></div>');
            $c.append($('<div class="' + CLS + 'metricLabel"></div>').text(label));
            /* A machine identifier is one left-to-right token: its digits and its
               separators must not be re-ordered by an Arabic paragraph. */
            var v = orDash(opts.mono ? ltrToken(value) : value);
            var $v = $('<div class="' + CLS + 'metricValue' + (opts.mono ? " " + CLS + "mono" : "") + '"></div>')
                .text(v).attr("title", v);
            $c.append($v);
            if (opts.meta) {
                $c.append($('<div class="' + CLS + 'metricMeta"></div>').text(opts.meta).attr("title", opts.meta));
            }
            return $c;
        }

        function detailCard() {
            var $card = $('<div class="' + CLS + 'card"></div>');
            var $grid = $('<div class="' + CLS + 'metrics"></div>');
            $card.append($grid);
            $card.grid = $grid;
            return $card;
        }

        /* Stat Grid card: an independently readable figure with its label and an
           optional qualifier. `wide` spans both columns. */
        function stat(label, value, meta, wide) {
            var $c = $('<div class="' + CLS + 'stat' + (wide ? " " + CLS + "statWide" : "") + '"></div>');
            $c.append($('<div class="' + CLS + 'statLabel"></div>').text(label).attr("title", label));
            var v = orDash(value);
            $c.append($('<div class="' + CLS + 'statValue"></div>').text(v).attr("title", v));
            if (meta) {
                $c.append($('<div class="' + CLS + 'statMeta"></div>').text(meta).attr("title", meta));
            }
            return $c;
        }

        /* Reconciliation row: a muted label on the left, the fact on the right -
           either a plain value or a status pill. Rows are separated by a dashed
           hairline, so the list reads as a specification rather than as a table. */
        function reconRow(label, value, tone) {
            var $r = $('<div class="' + CLS + 'reconRow"></div>');
            $r.append($('<span class="' + CLS + 'reconLabel"></span>').text(label).attr("title", label));
            if (tone) {
                $r.append(pill(value, tone));
            } else {
                var v = orDash(value);
                $r.append($('<span class="' + CLS + 'reconValue"></span>').text(v).attr("title", v));
            }
            return $r;
        }

        /* 1 · Panel header - the record's identity ------------------------- */

        /* CONTENT, not chrome: the host draws the window bar with its title,
           close and collapse affordances; this block names the record inside the
           body and carries no affordance of its own. */
        function renderHeader() {
            var $head = $('<header class="' + CLS + 'head"></header>').attr("data-sec", "header");

            $head.append($('<div class="' + CLS + 'eyebrow"></div>')
                .text(msg("VAS_293_Eyebrow", "Bank account")));

            var $row = $('<div class="' + CLS + 'headRow"></div>');
            var $id = $('<div class="' + CLS + 'headId"></div>');
            /* The BANK is the headline identity; the account is its subtitle. */
            var title = data.BankName || "";
            $id.append($('<h2 class="' + CLS + 'headTitle"></h2>').text(title).attr("title", title));
            /* Subtitle: account name (or its number when unnamed) · account type. */
            var subtitle = joinBits([data.BankAccountName || data.AccountNo, data.BankAccountTypeName || data.BankAccountType]);
            if (subtitle) {
                $id.append($('<div class="' + CLS + 'headSub"></div>').text(subtitle).attr("title", subtitle));
            }
            $row.append($id);

            /* The active state is the one fact that belongs beside the name: an
               inactive account changes how every figure below it should be read. */
            var state = data.IsActive ? msg("VAS_293_Active", "Active") : msg("VAS_293_Inactive", "Inactive");
            $row.append(badge(state, data.IsActive ? "success" : "risk"));
            $head.append($row);

            return $head;
        }

        /* 2 · Bank & account details - metric grid, detail-card wrap -------- */

        function renderDetails() {
            var $sec = section("details");
            /* No section summary: the organisation is a cell of the grid below,
               and the same fact twice in one section is noise. */
            $sec.append(sectionHeader(msg("VAS_293_BankAccountDetails", "Bank & account details")));

            var $card = detailCard();
            /* Full account number and IBAN, by explicit request - not masked.
               The four machine identifiers are monospaced so their digit groups
               line up and a transposed character is visible.
               ADDRESS LAST, by explicit request: the identifiers that name the
               account come first, the bank's postal address closes the card. */
            $card.grid.append(metric(msg("VAS_293_AccountNo", "Account no."), data.AccountNo, { mono: true }));
            $card.grid.append(metric(msg("VAS_293_Currency", "Currency"), currencyText()));
            $card.grid.append(metric(msg("VAS_293_IBAN", "IBAN"), data.IBAN, { wide: true, mono: true }));
            $card.grid.append(metric(msg("VAS_293_RoutingNo", "Routing no."), data.RoutingNo, { mono: true }));
            $card.grid.append(metric(msg("VAS_293_SwiftCode", "Swift code"), data.SwiftCode, { mono: true }));
            $card.grid.append(metric(msg("VAS_293_Organization", "Organization"), data.OrganizationName));
            $card.grid.append(metric(msg("VAS_293_DefaultAccount", "Default account"), yesNo(data.IsDefault)));
            $card.grid.append(metric(msg("VAS_293_BankAddress", "Bank address"), data.BankAddress, { wide: true }));
            $sec.append($card);
            return $sec;
        }

        /* "CHF ($)" when the symbol differs from the code, else just the code. */
        function currencyText() {
            if (!data.CurISO) return "";
            var sym = data.CurSymbol;
            return (sym && sym !== data.CurISO) ? (data.CurISO + " (" + sym + ")") : data.CurISO;
        }

        /* 3 · Account position - emphasis figure + stat grid ---------------- */

        function renderPosition() {
            var $sec = section("position");
            /* No section summary: the currency rides beside the figure it
               qualifies, where it is actually read. */
            $sec.append(sectionHeader(msg("VAS_293_AccountPosition", "Account position")));

            /* Emphasis row: the current balance - the panel's headline figure -
               with the currency code as its qualifier. */
            var $emph = $('<div class="' + CLS + 'balance"></div>');
            var $main = $('<div class="' + CLS + 'balanceMain"></div>');
            $main.append($('<div class="' + CLS + 'balanceLabel"></div>')
                .text(msg("VAS_293_CurrentBalance", "Current balance")));
            var balance = money(data.CurrentBalance);
            var balanceTone = (+data.CurrentBalance || 0) < 0 ? "risk" : "success";
            $main.append($('<div class="' + CLS + 'balanceValue ' + CLS + 'tx-' + balanceTone + '"></div>')
                .text(balance).attr("title", balance));
            $emph.append($main);
            if (data.CurISO) {
                $emph.append($('<span class="' + CLS + 'curPill"></span>').text(data.CurISO).attr("title", data.CurISO));
            }
            $sec.append($emph);

            /* Three stat cards. The catalogue's Stat Grid takes an even card
               count, so the third spans both columns rather than leaving a hole
               beside itself - there is no fourth position fact to invent. */
            var $stats = $('<div class="' + CLS + 'stats"></div>');
            /* The opening balance is only shown where the environment defines the
               column; a blank reads honestly, a zero would not. */
            $stats.append(stat(msg("VAS_293_OpeningBalance", "Opening balance"),
                data.HasOpenBalance ? money(data.OpenBalance) : "",
                data.HasOpenBalance ? "" : msg("VAS_293_NotDefined", "Not defined in this environment")));
            $stats.append(stat(msg("VAS_293_UnmatchedBalance", "Unmatched balance"), money(data.UnMatchedBalance)));
            $stats.append(stat(msg("VAS_293_CreditLimit", "Credit limit"), money(data.CreditLimit), "", true));
            $sec.append($stats);
            return $sec;
        }

        /* 4 · Statement & reconciliation - dashed label / value list -------- */

        function renderStatement() {
            var $sec = section("statement");
            $sec.append(sectionHeader(msg("VAS_293_StatementRecon", "Statement & reconciliation"),
                fmt(msg("VAS_293_AccountLineCount", "{0} account lines"), +data.AccountLineCount || 0)));

            var $list = $('<div class="' + CLS + 'recon"></div>');
            /* No account line at all: the two statement facts read as em dashes
               rather than as a zero balance on a date that does not exist. */
            $list.append(reconRow(msg("VAS_293_LastStatement", "Last statement"),
                data.HasLatestLine ? fmtDate(data.LatestStatementDate) : ""));
            $list.append(reconRow(msg("VAS_293_StatementBalance", "Statement balance"),
                data.HasLatestLine ? money(data.LatestEndingBalance) : ""));
            $list.append(reconRow(msg("VAS_293_UnmatchedBalance", "Unmatched balance"), money(data.UnMatchedBalance)));
            $list.append(reconRow(msg("VAS_293_ReconciliationStatus", "Reconciliation status"),
                reconLabel(), reconTone()));
            $sec.append($list);
            return $sec;
        }

        /* 5 · Linked configuration - entity list ---------------------------- */

        function renderLinkedConfig() {
            var $sec = section("linked");
            var list = visibleConfigs();
            $sec.append(sectionHeader(msg("VAS_293_LinkedConfiguration", "Linked configuration"),
                list.length ? fmt(msg("VAS_293_ConfiguredCount", "{0} of {1} configured"), configuredCount(list), list.length) : ""));

            /* A window with no active configuration tab is a real state, and it
               gets its own line - a data section is never left blank. */
            if (!list.length) {
                $sec.append($('<div class="' + CLS + 'secEmpty"></div>')
                    .text(msg("VAS_293_NoLinkedTabs", "No linked configuration tabs are active.")));
                return $sec;
            }

            /* Every row is its own card, expandable or not - an expandable one just
               also holds its table. The list itself stays flat and only spaces the
               cards, which is the "outer containers never carry borders, per-item
               cards do" rule. */
            var $list = $('<div class="' + CLS + 'entities"></div>');
            for (var i = 0; i < list.length; i++) {
                var cfg = list[i];
                var $card = $('<div class="' + CLS + 'entityCard"></div>').append(configRow(cfg));
                if (isExpandable(cfg)) {
                    $card.append(detailHost(cfg));
                }
                $list.append($card);
            }
            $sec.append($list);
            return $sec;
        }

        /* True when this row opens a detail table in place instead of switching the
           window to its tab. Only the two tables the specification names, and only
           when there is something to show: a row with no active record stays in its
           "Not configured" state and is NOT expandable. */
        function isExpandable(cfg) {
            if (!cfg) return false;
            if (cfg.TableName !== TBL_ACCOUNT_DOC && cfg.TableName !== TBL_STATEMENT_CLASS) return false;
            return (+cfg.RecordCount || 0) > 0;
        }

        /* DOM id of a row's detail container. Unique per window AND per tab, so two
           instances of the panel on screen cannot point their aria-controls at each
           other's table. */
        function detailId(cfg) {
            return CLS + "detail_" + ($self.windowNo || 0) + "_" + (+cfg.AD_Tab_ID || 0);
        }

        /* One Entity List row: type tile, identity + status pill + detail, then the
           record count and the chevron. The whole row is a button. Clicking it
           switches the hosting window to that tab - except on the two expandable
           rows, where it opens the detail table in place. */
        function configRow(cfg) {
            var expandable = isExpandable(cfg);
            var label = expandable ? msg("VAS_293_ShowDetail", "Show details")
                                   : msg("VAS_293_OpenTab", "Open this tab");
            var $r = $('<button type="button" class="' + CLS + 'entity"></button>')
                .attr("data-action", expandable ? "toggle-detail" : "open-tab")
                .attr("data-tab", cfg.AD_Tab_ID)
                .attr("data-table", cfg.TableName || "")
                .attr("title", label);

            if (expandable) {
                /* The row IS the disclosure control, so it says so: screen readers
                   announce the state and the table it governs. */
                $r.attr("aria-expanded", "false").attr("aria-controls", detailId(cfg));
            }

            /* The TILE carries the row's state - a green check when the tab holds
               something for this account, a grey dash when it does not. It replaces
               the status pill beside the title: one signal, read before any word
               is. The state is still spelled out for assistive technology, which
               cannot see a tint. */
            var state = cfg.IsConfigured ? msg("VAS_293_Configured", "Configured")
                                         : msg("VAS_293_NotConfigured", "Not configured");
            var $tile = $('<span class="' + CLS + 'tile ' + CLS
                          + (cfg.IsConfigured ? "tileOk" : "tileOff") + '"></span>')
                .attr("title", state).attr("aria-label", state);
            $tile.append(icon(cfg.IsConfigured ? "check" : "dash"));
            $r.append($tile);

            /* Title and summary FLOW: the summary follows the title on the same
               line while it fits and wraps beneath it when it does not, so a short
               detail ("MT940 Statement") costs no second line. */
            var $id = $('<span class="' + CLS + 'entityMain"></span>');
            $id.append($('<span class="' + CLS + 'entityName"></span>')
                .text(cfg.TabName || "").attr("title", cfg.TabName || ""));
            var detail = configDetail(cfg);
            $id.append($('<span class="' + CLS + 'entityMeta"></span>').text(detail).attr("title", detail));
            $r.append($id);

            var $trail = $('<span class="' + CLS + 'entityTrail"></span>');
            $trail.append($('<span class="' + CLS + 'countBadge"></span>')
                .text(ltrToken(+cfg.RecordCount || 0)));
            /* Every row wears the SAME chevron, expandable or not. On a row that
               opens in place it turns to point down at the table it just revealed;
               that rotation is the only thing that sets it apart. */
            $trail.append(icon("chevron").addClass(CLS + "chev"));
            $r.append($trail);

            return $r;
        }

        /* The empty, hidden container a row's detail table is rendered into. It is
           built with the list so the toggle has somewhere to draw; its rows are not
           fetched until the user opens it. */
        function detailHost(cfg) {
            return $('<div class="' + CLS + 'detail"></div>')
                .attr("id", detailId(cfg))
                .attr("data-table", cfg.TableName || "")
                .attr("hidden", "hidden");
        }

        /* The row's secondary line. Account Line is composed here rather than on
           the server because it mixes a date and an amount, and the client owns
           both formats; every other table's detail arrives ready as text. */
        function configDetail(cfg) {
            if (!cfg.IsConfigured) {
                return msg("VAS_293_NotConfigured", "Not configured");
            }

            if (cfg.TableName === TBL_ACCOUNT_LINE) {
                if (!data.HasLatestLine) return msg("VAS_293_NoStatement", "No statement");
                return joinBits([
                    fmtDate(data.LatestStatementDate),
                    fmt(msg("VAS_293_EndingBalanceMeta", "Ending balance {0}"), money(data.LatestEndingBalance))
                ]);
            }

            if (cfg.TableName === TBL_STATEMENT_LOADER) {
                var run = cfg.DetailDate
                    ? fmt(msg("VAS_293_LastRun", "Last run {0}"), fmtDate(cfg.DetailDate))
                    : msg("VAS_293_NeverRun", "Never run");
                return joinBits([cfg.Detail, run]);
            }

            /* Default Accounting and the rest: whatever the server composed, and
               "Configured" when it had nothing more specific to say. */
            return cfg.Detail || msg("VAS_293_Configured", "Configured");
        }

        /* 5a · Inline detail ------------------------------------------------ */

        /* Everything that differs between the two expandable rows, in one place:
           the endpoint, the empty-state wording, and the columns. A column is
           {label, cell}: `cell` turns one payload row into the jQuery content of
           one td, so the column list is the ONLY place that knows the shape of a
           row.

           The block carries no title of its own: it opens directly under the row
           that names it, inside the same card, and the record count is already on
           that row. A heading would only repeat both. */
        function detailSpec(tableName) {
            if (tableName === TBL_ACCOUNT_DOC) {
                return {
                    action: "GetBankAccountDocDetail",
                    /* Column widths live in the stylesheet, keyed off this class -
                       a fixed table needs them, and CSS is where they belong. */
                    cls: CLS + "tblDoc",
                    empty: msg("VAS_293_NoDocDetail", "No Bank Account Document configuration found."),
                    columns: [
                        {
                            /* The document NAME links to its own record. Priority
                               rides UNDER it rather than taking a column of its
                               own: five columns is already what a right panel
                               affords, and a one-or-two digit figure does not earn
                               a sixth. */
                            label: msg("VAS_293_ColDocument", "Document"),
                            cell: function (row) {
                                return cellText(row.Name,
                                    row.Priority
                                        ? fmt(msg("VAS_293_PriorityMeta", "Priority {0}"), ltrToken(row.Priority))
                                        : "",
                                    { tableName: TBL_ACCOUNT_DOC, recordId: row.C_BankAccountDoc_ID });
                            }
                        },
                        {
                            label: msg("VAS_293_ColPaymentMethod", "Payment method"),
                            cell: function (row) { return cellText(row.PaymentMethod); }
                        },
                        {
                            /* The cheque book as ONE column: the SERIES it spans on
                               top, the number it is standing on underneath. Sequence
                               numbers are identifiers, not quantities - monospaced,
                               never grouped, LEFT-aligned like every other column
                               (they are not figures to total down the column), and
                               each is a single left-to-right token so RTL cannot
                               reverse a range's ends. */
                            label: msg("VAS_293_ColChequeSeries", "Cheque series"),
                            cell: function (row) {
                                return cellText(chequeSeries(row), row.CurrentNext
                                    ? fmt(msg("VAS_293_CurrentNoMeta", "Current {0}"), ltrToken(row.CurrentNext))
                                    : "").addClass(CLS + "mono");
                            }
                        },
                        {
                            /* The PROCESS by name - the panel never shows an
                               AD_Process_ID, and the server already translated it. */
                            label: msg("VAS_293_ColProcess", "Process"),
                            cell: function (row) { return cellText(row.ProcessName); }
                        },
                        {
                            label: msg("VAS_293_ColStatus", "Status"),
                            cell: function (row) { return statusPill(row.IsActive); }
                        }
                    ]
                };
            }

            return {
                action: "GetStatementClassDetail",
                cls: CLS + "tblClass",
                empty: msg("VAS_293_NoClassDetail", "No Statement Class configuration found."),
                columns: [
                    {
                        /* Two different names: the configuration row's own - which
                           links to its record - and the VA012_StatementClass master
                           it points at. They answer different questions, so each
                           gets its own column. */
                        label: msg("VAS_293_ColStatementClass", "Statement class"),
                        cell: function (row) {
                            return cellText(row.ClassName, "",
                                { tableName: TBL_STATEMENT_CLASS,
                                  recordId: row.VA012_BankStatementClass_ID });
                        }
                    },
                    {
                        label: msg("VAS_293_ColClass", "Class"),
                        cell: function (row) { return cellText(row.MasterClassName); }
                    },
                    {
                        label: msg("VAS_293_ColStatus", "Status"),
                        cell: function (row) { return statusPill(row.IsActive); }
                    }
                ]
            };
        }

        /* "1 – 50" from the two ends of the cheque book. Either end may be absent,
           in which case the one that is known stands alone rather than reading as
           an open-ended range. */
        function chequeSeries(row) {
            var from = row.StartChkNumber || "";
            var to = row.EndChkNumber || "";
            if (from.length && to.length) return ltrToken(from + " – " + to);
            return ltrToken(from.length ? from : to);
        }

        /* A truncating cell that carries its full value as a tooltip - a right panel
           is never wide enough to promise a column will fit. An optional `meta` adds
           a smaller second line, which is how a cell carries a second fact without
           the table growing a column it has no room for.

           `zoom` = {tableName, recordId} turns the value into a link that opens that
           record in its own window. It degrades to plain text whenever the zoom
           cannot be performed - a dead link is worse than no link. */
        function cellText(value, meta, zoom) {
            var v = orDash(value);
            var $c = $('<span class="' + CLS + 'cellText"></span>');
            var linkable = zoom && canZoom(zoom.tableName) && (+zoom.recordId > 0)
                           && v !== "—";
            var $main = linkable
                ? $('<button type="button" class="' + CLS + 'cellMain ' + CLS + 'link"></button>')
                    .attr("data-action", "zoom")
                    .attr("data-zoom-table", zoom.tableName)
                    .attr("data-zoom-id", zoom.recordId)
                : $('<span class="' + CLS + 'cellMain"></span>');
            $main.text(v).attr("title", linkable
                ? (v + " — " + msg("VAS_293_OpenRecord", "Open this record"))
                : v);
            $c.append($main);
            if (meta) {
                $c.append($('<span class="' + CLS + 'cellMeta"></span>').text(meta).attr("title", meta));
            }
            return $c;
        }

        /* The zoom stays in the hosting window: it switches to the record's own
           tab and selects the row (see zoomToRecord). So it only needs that tab
           to exist in this window. */
        function canZoom(tableName) {
            return configTabId(tableName) > 0;
        }

        /* The AD_Tab_ID of the hosting window's tab over this table, as the
           server sent it with the configuration rows - never a constant. */
        function configTabId(tableName) {
            var list = (data && data.LinkedConfigs) || [];
            for (var i = 0; i < list.length; i++) {
                if (list[i] && list[i].TableName === tableName) return +list[i].AD_Tab_ID || 0;
            }
            return 0;
        }

        function statusPill(isActive) {
            return pill(isActive ? msg("VAS_293_Active", "Active") : msg("VAS_293_Inactive", "Inactive"),
                        isActive ? "success" : "neutral");
        }

        /* Paints a loaded detail payload into its container: the table, or the
           block's empty state. */
        function renderDetail($host, tableName, payload) {
            var spec = detailSpec(tableName);
            var rows = (payload && payload.Rows) || [];

            $host.empty();

            if (!rows.length) {
                /* Two different silences: the read came back with nothing, or it
                   could not run at all. The user is told which. */
                var empty = (payload && payload.Ok)
                    ? spec.empty
                    : msg("VAS_293_DetailLoadFailed", "Could not load the configuration details.");
                $host.append($('<div class="' + CLS + 'detailEmpty"></div>').text(empty));
                return;
            }

            $host.append(detailTable(spec, rows));
        }

        /* A nested Data Grid, one row per record. Every column reads from the same
           edge - nothing here is a figure to be totalled down a column, so nothing
           is right-aligned. */
        function detailTable(spec, rows) {
            var $table = $('<table class="' + CLS + 'table ' + spec.cls + '"></table>');
            var $headRow = $('<tr></tr>');
            var i;
            for (i = 0; i < spec.columns.length; i++) {
                var col = spec.columns[i];
                $headRow.append($('<th scope="col"></th>').text(col.label).attr("title", col.label));
            }
            $table.append($('<thead></thead>').append($headRow));

            var $tbody = $('<tbody></tbody>');
            for (i = 0; i < rows.length; i++) {
                var $tr = $('<tr></tr>');
                for (var c = 0; c < spec.columns.length; c++) {
                    $tr.append($('<td></td>').append(spec.columns[c].cell(rows[i])));
                }
                $tbody.append($tr);
            }
            $table.append($tbody);
            return $table;
        }

        /* ---------------------------------------------------------------- */
        /*  Actions                                                         */
        /* ---------------------------------------------------------------- */

        /* Opens or closes one row's detail table. Nothing here reloads the panel:
           the rows are fetched once per account and kept, so a row that is opened,
           closed and opened again costs no second request. */
        function toggleDetail($btn) {
            var tableName = $btn.attr("data-table");
            var $host = $root.find("#" + cssEscape(detailIdOf($btn)));
            if (!$host.length) return;

            var open = $btn.attr("aria-expanded") === "true";
            if (open) {
                closeDetail($btn, $host);
                return;
            }

            $btn.attr("aria-expanded", "true").addClass(CLS + "open")
                .attr("title", msg("VAS_293_HideDetail", "Hide details"));
            /* The CARD takes the open state too - its border is what groups the
               row with the table below it. Set here rather than with :has(), which
               this application's browser baseline cannot be relied on to support. */
            $btn.closest("." + CLS + "entityCard").addClass(CLS + "open");
            $host.removeAttr("hidden");

            if (detailCache.hasOwnProperty(tableName)) {
                renderDetail($host, tableName, detailCache[tableName]);
                return;
            }
            loadDetail($btn, $host, tableName);
        }

        /* Table names of the detail rows currently expanded. */
        function openDetailTables() {
            var names = [];
            if (!$root) return names;
            $root.find('[data-action="toggle-detail"][aria-expanded="true"]').each(function () {
                var name = $(this).attr("data-table");
                if (name) names.push(name);
            });
            return names;
        }

        /* Expands those rows again after a re-render. A row that is no longer
           expandable (its last record was removed) is simply skipped. */
        function reopenDetailTables(names) {
            if (!$root || !names || !names.length) return;
            for (var i = 0; i < names.length; i++) {
                var $btn = $root.find('[data-action="toggle-detail"]').filter(function () {
                    return $(this).attr("data-table") === names[i];
                }).first();
                if ($btn.length && $btn.attr("aria-expanded") !== "true") toggleDetail($btn);
            }
        }

        function closeDetail($btn, $host) {
            $btn.attr("aria-expanded", "false").removeClass(CLS + "open")
                .attr("title", msg("VAS_293_ShowDetail", "Show details"));
            $btn.closest("." + CLS + "entityCard").removeClass(CLS + "open");
            $host.attr("hidden", "hidden");
        }

        /* The id the row's aria-controls names. Read back off the button rather than
           recomposed, so the row and its container can never drift apart. */
        function detailIdOf($btn) {
            return $btn.attr("aria-controls") || "";
        }

        /* The ids here are ours (prefix + digits), so this only has to survive being
           handed to a selector - CSS.escape where the browser has it, and a plain
           passthrough otherwise. */
        function cssEscape(id) {
            try {
                if (window.CSS && typeof window.CSS.escape === "function") return window.CSS.escape(id);
            } catch (e) { }
            return id;
        }

        /* Fetches one detail table. The account id is captured at request time and
           re-checked on the reply: the user can move to another bank account while a
           detail is in flight, and that reply must not paint over the new one. */
        function loadDetail($btn, $host, tableName) {
            var spec = detailSpec(tableName);
            var token = fetchToken;
            var recordID = shownRecordId;

            $host.empty().append($('<div class="' + CLS + 'detailEmpty"></div>')
                .text(msg("VAS_293_Loading", "Loading...")));

            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_293_BankAccountRightPanel/" + spec.action,
                type: "GET",
                dataType: "json",
                data: { C_BankAccount_ID: recordID },
                success: function (raw) {
                    if (token !== fetchToken || recordID !== shownRecordId) return;
                    var payload;
                    try {
                        payload = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    } catch (e) {
                        payload = null;
                    }
                    if (!payload) payload = { Ok: false, Rows: [] };
                    /* Only a read that actually RAN is worth keeping. A server-side
                       failure is cached nowhere, so the next open tries again. */
                    if (payload.Ok) detailCache[tableName] = payload;
                    renderDetail($host, tableName, payload);
                },
                error: function (err) {
                    if (token !== fetchToken || recordID !== shownRecordId) return;
                    if (window.console) console.log(err);
                    /* NOT cached: a failed read is worth retrying on the next open. */
                    renderDetail($host, tableName, { Ok: false, Rows: [] });
                }
            });
        }

        /* The hosting window, read from the panel's own tab. Window ids differ
           per environment, so this is the only acceptable source. */
        function hostWindowId() {
            try {
                var tab = $self.curTab;
                if (tab && typeof tab.getAD_Window_ID === "function") {
                    return +tab.getAD_Window_ID() || 0;
                }
            } catch (e) { }
            return +$self.AD_Window_ID || 0;
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

        /* How long zoomToRecord waits for the target tab to load its rows, and
           how often it looks. The tab change re-queries asynchronously and gives
           no callback, so the row can only be selected once it has arrived. */
        var ZOOM_POLL_MS = 100;
        var ZOOM_TIMEOUT_MS = 6000;

        /* Opens ONE record of a detail table IN THIS WINDOW: switches the hosting
           window to that record's tab - the same tab change a row click makes,
           so the bank account stays the parent - then selects the record's row.

           VIS.AEnv.zoom is not used: it opens a NEW window, and only for tables
           with their own AD_Table.AD_Window_ID, which child tables such as
           C_BankAccountDoc and VA012_BankStatementClass do not have.

           The row is selected through the tab's GridController.navigate, the
           same call a click on the grid row makes, so the grid, the card view
           and the tab panels all follow. The query has not finished when the
           tab change returns, so the row is looked for until it appears; a
           re-query landing after the first selection moves the current row
           back, which is why the record must read as current on two checks in
           a row before the wait ends. A record that never shows up (e.g. not
           on the tab's first page) leaves the user on the tab, unselected.
           Degrades silently: a click can never throw. */
        function zoomToRecord(tableName, recordId) {
            var record = +recordId || 0;
            var tabId = configTabId(tableName);
            if (record <= 0 || tabId <= 0) return;

            var aPanel = hostPanel();
            if (!aPanel) {
                if (window.console) console.log("VAS_293: hosting window not found for tab " + tabId);
                return;
            }
            try {
                aPanel.onTabChange($self.windowNo + "_" + tabId);
            } catch (e) {
                if (window.console) console.log(e);
                return;
            }

            var waited = 0;
            var settled = 0;
            (function poll() {
                try {
                    var tab = aPanel.curTab;
                    var gc = aPanel.curGC;
                    if (tab && gc && typeof gc.navigate === "function"
                        && +tab.getAD_Tab_ID() === tabId) {
                        if (+tab.getRecord_ID() === record) {
                            if (++settled >= 2) return;        // selected and stayed
                        } else {
                            settled = 0;
                            var count = +tab.getRowCount() || 0;
                            for (var i = 0; i < count; i++) {
                                if (+tab.getKeyID(i) === record) {
                                    gc.navigate(i);
                                    break;
                                }
                            }
                        }
                    }
                } catch (e) { if (window.console) console.log(e); }

                waited += ZOOM_POLL_MS;
                if (waited < ZOOM_TIMEOUT_MS) setTimeout(poll, ZOOM_POLL_MS);
            })();
        }

        /* Switches the HOSTING window to the configuration tab the row names -
           exactly what a click on that tab header does (the APanel registers every
           tab as the action windowNo_AD_Tab_ID and routes the header click through
           onTabChange). The bank account stays selected, so the child tab opens
           filtered to this account, and no second window is ever opened. The tab
           id came from the window's own metadata, never from a constant.
           Degrades silently: a click can never throw. */
        function openLinkedTab(tabId) {
            var id = +tabId || 0;
            if (id <= 0) return;
            try {
                var aPanel = hostPanel();
                if (!aPanel) {
                    if (window.console) console.log("VAS_293: hosting window not found for tab " + id);
                    return;
                }
                aPanel.onTabChange($self.windowNo + "_" + id);
            } catch (e) { if (window.console) console.log(e); }
        }

        this.markDisposed = function () { disposed = true; };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_293_BankAccountRightPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        /* Cached so the fetch still resolves the window on a build whose tab does
           not expose the getter at call time. */
        if (curTab && typeof curTab.getAD_Window_ID === "function") {
            this.AD_Window_ID = curTab.getAD_Window_ID();
        }
        this.init();
        /* Watch the tab itself so New Record / Copy Record (neither of which
           reliably calls refreshPanelData) still empties the panel. */
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update the tab panel for the selected record. */
    VAS.VAS_293_BankAccountRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        /* The insert check is what makes New Record / Copy Record behave: the id
           handed in for an unsaved row can still be the previously selected (or
           copied-from) account's, so the tab's own insert state decides. */
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        /* Held rather than fetched outright: the insert flag is not always up yet
           when we get here, so scheduleFetch asks once more before loading.
           `true` = re-read even when this record is already on screen, so the
           panel is current again after a trip to one of its child tabs. */
        this.scheduleFetch(recordID, true);
    };

    /* The platform Refresh button - exposed on the prototype as well as on the
       instance, since the host may reach either. */
    VAS.VAS_293_BankAccountRightPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) this.fetchData(this.record_ID);
        else this.clear();
    };

    /* Set width as per window width */
    VAS.VAS_293_BankAccountRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_293_BankAccountRightPanel.prototype.dispose = function () {
        /* Kill any held fetch first - its timer would otherwise fire against a
           panel whose curTab has just been nulled out below. */
        if (typeof this.abortPendingFetch === "function") {
            try { this.abortPendingFetch(); } catch (e) { }
        }
        if (typeof this.markDisposed === "function") {
            try { this.markDisposed(); } catch (e) { }
        }
        if (this.curTab && typeof this.curTab.removeDataStatusListener === "function") {
            try { this.curTab.removeDataStatusListener(this.tabDataListener); } catch (e) { }
        }
        this.tabDataListener = null;
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
