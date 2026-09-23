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
 *                       details              no., SWIFT/BIC, organisation, default
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
 *                                           this account still selected.
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
 *   SWIFT / BIC                                 | VAS_293_SwiftCode
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
            /* One glyph per configuration table, so a row is recognisable before
               its label is read. */
            lines: '<svg ' + SVG_ATTR + '><path d="M8 6h13"/><path d="M8 12h13"/><path d="M8 18h13"/><path d="M3 6h.01"/><path d="M3 12h.01"/><path d="M3 18h.01"/></svg>',
            doc: '<svg ' + SVG_ATTR + '><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v5h6"/></svg>',
            processor: '<svg ' + SVG_ATTR + '><rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20"/></svg>',
            loader: '<svg ' + SVG_ATTR + '><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/><path d="M12 15V3"/></svg>',
            statementClass: '<svg ' + SVG_ATTR + '><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2Z"/></svg>',
            accounting: '<svg ' + SVG_ATTR + '><rect x="4" y="2" width="16" height="20" rx="2"/><path d="M8 6h8"/><path d="M8 11h3"/><path d="M13 11h3"/><path d="M8 16h3"/><path d="M13 16h3"/></svg>',
            config: '<svg ' + SVG_ATTR + '><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z"/></svg>',
            chevron: '<svg ' + SVG_ATTR + '><path d="m9 18 6-6-6-6"/></svg>'
        };

        function icon(name) {
            return $('<span class="' + CLS + 'ic"></span>').html(SVG[name] || "");
        }

        /* The configuration tables, in the order the panel lists them, with the
           glyph each row wears. A table the window does not expose as an active
           tab never reaches here - the server only sends rows it found. */
        function configIcon(tableName) {
            if (tableName === TBL_ACCOUNT_LINE) return "lines";
            if (tableName === TBL_ACCOUNT_DOC) return "doc";
            if (tableName === TBL_PAYMENT_PROCESSOR) return "processor";
            if (tableName === TBL_STATEMENT_LOADER) return "loader";
            if (tableName === TBL_STATEMENT_CLASS) return "statementClass";
            if (tableName === TBL_DEFAULT_ACCOUNTING) return "accounting";
            return "config";
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

        function configuredCount() {
            var list = (data && data.LinkedConfigs) || [];
            var n = 0;
            for (var i = 0; i < list.length; i++) {
                if (list[i] && list[i].IsConfigured) n++;
            }
            return n;
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
            /* Nothing of the previous record may stay on screen while another
               record is loading. The "no account" placeholder is NOT raised: this
               is a load, not an empty selection. */
            data = null;
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
            $card.grid.append(metric(msg("VAS_293_SwiftCode", "SWIFT / BIC"), data.SwiftCode, { mono: true }));
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
            var list = (data.LinkedConfigs) || [];
            $sec.append(sectionHeader(msg("VAS_293_LinkedConfiguration", "Linked configuration"),
                list.length ? fmt(msg("VAS_293_ConfiguredCount", "{0} of {1} configured"), configuredCount(), list.length) : ""));

            /* A window with no active configuration tab is a real state, and it
               gets its own line - a data section is never left blank. */
            if (!list.length) {
                $sec.append($('<div class="' + CLS + 'secEmpty"></div>')
                    .text(msg("VAS_293_NoLinkedTabs", "No linked configuration tabs are active.")));
                return $sec;
            }

            var $list = $('<div class="' + CLS + 'entities"></div>');
            for (var i = 0; i < list.length; i++) {
                $list.append(configRow(list[i], i === list.length - 1));
            }
            $sec.append($list);
            return $sec;
        }

        /* One Entity List row: type tile, identity + status pill + detail, then
           the record count and the navigation chevron. The whole row is a button
           - clicking it switches the hosting window to that tab. */
        function configRow(cfg, isLast) {
            var label = msg("VAS_293_OpenTab", "Open this tab");
            var $r = $('<button type="button" class="' + CLS + 'entity' + (isLast ? " " + CLS + "last" : "") + '"></button>')
                .attr("data-action", "open-tab")
                .attr("data-tab", cfg.AD_Tab_ID)
                .attr("title", label);

            /* One tone for the whole section (entity type = configuration); row
               status rides on the pill, not on the tile. */
            var $tile = $('<span class="' + CLS + 'tile"></span>');
            $tile.append(icon(configIcon(cfg.TableName)));
            $r.append($tile);

            var $id = $('<span class="' + CLS + 'entityMain"></span>');
            var $titleRow = $('<span class="' + CLS + 'entityTitle"></span>');
            $titleRow.append($('<span class="' + CLS + 'entityName"></span>').text(cfg.TabName || "").attr("title", cfg.TabName || ""));
            $titleRow.append(pill(cfg.IsConfigured ? msg("VAS_293_Configured", "Configured")
                                                   : msg("VAS_293_NotConfigured", "Not configured"),
                                  cfg.IsConfigured ? "success" : "neutral"));
            $id.append($titleRow);

            var detail = configDetail(cfg);
            $id.append($('<span class="' + CLS + 'entityMeta"></span>').text(detail).attr("title", detail));
            $r.append($id);

            var $trail = $('<span class="' + CLS + 'entityTrail"></span>');
            $trail.append($('<span class="' + CLS + 'entityValue"></span>').text(ltrToken(+cfg.RecordCount || 0)));
            $trail.append(icon("chevron").addClass(CLS + "chev"));
            $r.append($trail);

            return $r;
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

        /* ---------------------------------------------------------------- */
        /*  Actions                                                         */
        /* ---------------------------------------------------------------- */

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
           when we get here, so scheduleFetch asks once more before loading. */
        this.scheduleFetch(recordID);
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
