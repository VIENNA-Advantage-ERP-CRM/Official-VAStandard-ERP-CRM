/************************************************************
 * Module Name    : VAS
 * Purpose        : Banking Journal overview right panel for the selected
 *                  C_BankStatement record (the Banking Journal / Bank
 *                  Statement window). Read-only.
 *
 *                  Composition follows the approved reference
 *                  (banking-journal-right-panel-no-harness.html), section for
 *                  section, drawn with the design.md right-panel primitives
 *                  (windows-and-panels.md > Right Panel Body):
 *                    hero status card (document, status pill, net bank
 *                    movement, posting / balance / matching chips)
 *                    -> Exceptions (compact list, only when one exists)
 *                    -> Bank account (metric grid, detail-card wrap)
 *                    -> Balance summary (metric grid, detail-card wrap)
 *                    -> Journal details (metric grid, two values per row)
 *                    -> Journal lines (data grid, paged on the server)
 *                    -> Accounting impact (metric grid + account data grid)
 *                    -> Audit (step rail: Drafted -> In progress -> Approved
 *                       -> Completed -> Posted, each with actor + moment).
 *                  The reference's demo harness and dev-note metas
 *                  ("C_BankAccount.CurrentBalance") are deliberately NOT drawn.
 *
 *                  Journal lines page on the SERVER, 20 rows per request
 *                  (data.LinesPageSize): the initial payload carries page 0
 *                  plus the count and totals over every line, and the compact
 *                  footer pager fetches further pages through GetJournalLines,
 *                  caching each page until the record changes. The panel body
 *                  owns scrolling and no section grows its own scrollbar.
 *
 *                  The Audit rail is composed HERE from the facts the server
 *                  reports (creator, workflow activities with their node
 *                  action / document action, document status, approval flag,
 *                  posting moment + user), so every step title is localised
 *                  through VIS.Msg. Exactly one step is ever "active": the
 *                  first one that is neither done nor blocked. Drawn as the
 *                  rail VAS_191 / VAS_291 use - a state-toned marker node, a
 *                  hairline connector and plain title + meta text.
 *
 *                  The panel chrome (shell width, collapse strip, header, close
 *                  button, panel switcher) belongs to the VIS tab-panel host,
 *                  not to this file - every existing VAS right panel hands
 *                  that to the host, and this one does the same. The root this
 *                  file returns IS the panel body.
 *
 *                  Data comes from VAS_292_BankingJournalRightPanel/
 *                  GetJournalOverview in one read: header + bank account +
 *                  bank-account currency, one aggregate over the lines, the
 *                  first few lines, the Fact_Acct impact (only when posted)
 *                  and the audit names. Every amount is drawn in the BANK
 *                  ACCOUNT currency (symbol / precision from the payload); a
 *                  line that carries another currency keeps its own symbol.
 *                  Balance checks come from the server as booleans decided on
 *                  the decimal values - nothing is compared as text here.
 *
 *                  Exceptions are data-driven and the section is hidden when
 *                  none exists: statement difference outside the currency
 *                  tolerance, unmatched active lines, completed / closed but
 *                  not posted. Optional fields and sections without meaningful
 *                  data are hidden rather than shown empty.
 *
 *                  Navigation - each action runs only when the framework
 *                  capability is available, and is drawn disabled otherwise:
 *                    View account    - zooms to the Bank Account window on the
 *                                      statement's C_BankAccount_ID through
 *                                      VAS.ZoomUtil (window resolved by NAME,
 *                                      never a hard-coded id).
 *                    View all lines  - switches the HOSTING window to the
 *                                      statement's own line tab (found by its
 *                                      table, C_BankStatementLine), exactly as
 *                                      a click on that tab header does; never
 *                                      opens a second window instance.
 *                    View accounting - opens the framework account viewer
 *                                      (VIS.AcctViewer) on this record, the
 *                                      same dialog the window's Posted button
 *                                      raises; enabled only when posted.
 *                  None of them creates a transaction.
 *
 *                  All on-screen strings resolve through VIS.Msg.getMsg with an
 *                  English fallback, so an unseeded AD_Message key never renders
 *                  as a raw key. The account number is masked here to its last
 *                  four characters.
 * Class Used     : VAS.VAS_292_BankingJournalRightPanel
 * Chronological development:
 *   VAI145   2026-09-21  Created.
 *   VAI145   2026-09-21  Journal details as a two-column metric grid; lines
 *                        paged on the server at 20 per request; Audit redrawn
 *                        as the Drafted / In progress / Approved / Completed /
 *                        Posted rail; "Bank account currency" meta dropped.
 *
 * -- Labels / Message Keys ---------------------------------------------------
 *  Panel
 *   No banking journal selected                 | VAS_292_NoData
 *   Could not load the banking journal details. | VAS_292_LoadFailed
 *
 *  Hero card
 *   Net bank movement                     | VAS_292_NetBankMovement
 *   {0} journal lines                     | VAS_292_LineCount
 *   Posted / Unposted                     | VAS_292_Posted / VAS_292_Unposted
 *   Statement balanced                    | VAS_292_StatementBalanced
 *   Difference exists                     | VAS_292_DifferenceExists
 *   All lines matched                     | VAS_292_AllLinesMatched
 *   {0} unmatched                         | VAS_292_UnmatchedCount
 *
 *  Exceptions
 *   Exceptions                            | VAS_292_Exceptions
 *   {0} open                              | VAS_292_OpenCount
 *   Review                                | VAS_292_Review
 *   Statement difference                  | VAS_292_StatementDifference
 *   Statement difference requires review  | VAS_292_StatementDifferenceMeta
 *   Unmatched journal lines               | VAS_292_UnmatchedLines
 *   {0} journal line(s) are not matched   | VAS_292_UnmatchedLinesMeta
 *   Completed but not posted              | VAS_292_CompletedNotPosted
 *   Completed Banking Journal is not posted | VAS_292_CompletedNotPostedMeta
 *
 *  Bank account
 *   Bank account                          | VAS_292_BankAccount
 *   View account                          | VAS_292_ViewAccount
 *   Bank                                  | VAS_292_Bank
 *   Account                               | VAS_292_Account
 *   Currency                              | VAS_292_Currency
 *   Current balance                       | VAS_292_CurrentBalance
 *   Unmatched balance                     | VAS_292_UnmatchedBalance
 *   Account type                          | VAS_292_AccountType
 *
 *  Balance summary
 *   Balance summary                       | VAS_292_BalanceSummary
 *   {0} lines                             | VAS_292_LinesShort
 *   Beginning balance                     | VAS_292_BeginningBalance
 *   Before journal activity               | VAS_292_BeforeActivity
 *   Ending balance                        | VAS_292_EndingBalance
 *   Statement ending balance              | VAS_292_StatementEnding
 *   Inflow                                | VAS_292_Inflow
 *   Positive statement lines              | VAS_292_InflowMeta
 *   Outflow                               | VAS_292_Outflow
 *   Negative statement lines              | VAS_292_OutflowMeta
 *   Charges                               | VAS_292_Charges
 *   Interest {0}                          | VAS_292_InterestMeta
 *   Difference                            | VAS_292_Difference
 *   Balanced                              | VAS_292_Balanced
 *   Requires review                       | VAS_292_RequiresReview
 *
 *  Journal details
 *   Journal details                       | VAS_292_JournalDetails
 *   Name                                  | VAS_292_Name
 *   Statement date                        | VAS_292_StatementDate
 *   Document status                       | VAS_292_DocumentStatus
 *   Processed                             | VAS_292_Processed
 *   Approved                              | VAS_292_Approved
 *   Journal type                          | VAS_292_JournalType
 *   Manual                                | VAS_292_Manual
 *   Imported / system                     | VAS_292_ImportedSystem
 *   Description                           | VAS_292_Description
 *   Yes / No                              | VAS_292_Yes / VAS_292_No
 *
 *  Journal lines
 *   Journal lines                         | VAS_292_JournalLines
 *   {0} matched · {1} unmatched           | VAS_292_MatchedSummary
 *   View all lines                        | VAS_292_ViewAllLines
 *   Line                                  | VAS_292_Line
 *   Amount                                | VAS_292_Amount
 *   Match                                 | VAS_292_Match
 *   Matched / Unmatched                   | VAS_292_Matched / VAS_292_Unmatched
 *   Inflow line / Outflow line            | VAS_292_InflowLine / VAS_292_OutflowLine
 *   Zero amount line                      | VAS_292_ZeroLine
 *   No lines on this journal yet.         | VAS_292_NoLines
 *   Showing                               | VAS_292_Showing
 *   Previous page / Next page / of        | VAS_292_Previous / VAS_292_Next / VAS_292_Of
 *   Statement line date                   | VAS_292_StatementLineDate
 *   Effective date                        | VAS_292_EffectiveDate
 *   Account date                          | VAS_292_AccountDate
 *   Reference no.                         | VAS_292_ReferenceNo
 *   Payment                               | VAS_292_Payment
 *   Business partner                      | VAS_292_BusinessPartner
 *   Charge                                | VAS_292_Charge
 *
 *  Accounting impact
 *   Accounting impact                     | VAS_292_AccountingImpact
 *   View accounting                       | VAS_292_ViewAccounting
 *   Total debit / Total credit            | VAS_292_TotalDebit / VAS_292_TotalCredit
 *   {0} accounting entries                | VAS_292_EntryCount
 *   Posting imbalance                     | VAS_292_PostingImbalance
 *   Posting status                        | VAS_292_PostingStatus
 *   Debit / Credit                        | VAS_292_Debit / VAS_292_Credit
 *   Net debit / Net credit                | VAS_292_NetDebit / VAS_292_NetCredit
 *   No accounting entries posted yet.     | VAS_292_NoEntries
 *
 *  Audit (rail)
 *   Audit                                 | VAS_292_Audit
 *   {0} of {1} complete                   | VAS_292_StepsComplete
 *   Drafted                               | VAS_292_Drafted
 *   In progress                           | VAS_292_InProgress
 *   Approved                              | VAS_292_Approved
 *   awaiting approval                     | VAS_292_AwaitingApproval
 *   Completed                             | VAS_292_Completed
 *   awaiting completion                   | VAS_292_AwaitingCompletion
 *   Posted                                | VAS_292_Posted
 *   not posted yet                        | VAS_292_NotPostedYet
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

    VAS.VAS_292_BankingJournalRightPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;

        var CLS = "vas_292-";

        /* Bank Account window, resolved by NAME through VAS.ZoomUtil (ids differ
           per environment). 0 until resolved; "View account" is drawn disabled
           until then. */
        var ZOOM_ACCOUNT_COLUMN = "C_BankAccount_ID";
        var ZOOM_ACCOUNT_WINDOW_NEW = "VAS_BankAccount";
        var ZOOM_ACCOUNT_WINDOW_OLD = "Bank Account";
        var accountWindowId = 0;
        var disposed = false;

        /* The C_BankStatement_ID the panel is showing OR loading. 0 = nothing. */
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

        /* Journal lines page on screen (zero-based) and the pages fetched so far,
           keyed by page index. Page 0 comes with the initial payload; the rest
           are fetched on demand through GetJournalLines. Both reset on every
           record change. The page size is the SERVER's (data.LinesPageSize), so
           the pager and the server can never disagree about where a page starts. */
        var linesPage = 0;
        var linePages = {};
        var DEFAULT_LINES_PER_PAGE = 20;
        /* A page fetch on the wire, keyed by the fetch token it belongs to. */
        var linesFetchToken = 0;

        /* ---------------------------------------------------------------- */
        /*  Icons - inline SVG only; the host shell may not carry an icon font */
        /* ---------------------------------------------------------------- */

        var SVG_ATTR = 'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"';
        var SVG = {
            external: '<svg ' + SVG_ATTR + '><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5"/></svg>',
            list: '<svg ' + SVG_ATTR + '><path d="M8 6h13"/><path d="M8 12h13"/><path d="M8 18h13"/><path d="M3 6h.01"/><path d="M3 12h.01"/><path d="M3 18h.01"/></svg>',
            book: '<svg ' + SVG_ATTR + '><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></svg>',
            inflow: '<svg ' + SVG_ATTR + '><path d="M17 7 7 17"/><path d="M17 17H7V7"/></svg>',
            outflow: '<svg ' + SVG_ATTR + '><path d="M7 17 17 7"/><path d="M7 7h10v10"/></svg>',
            neutral: '<svg ' + SVG_ATTR + '><path d="M5 12h14"/></svg>',
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

        /* Amounts carry the bank account currency's symbol and precision; the
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

        /* Bank account currency - header balances and the line summary. */
        function money(value) {
            if (!data) return "";
            return fmtMoney(value, data.CurSymbol || data.CurISO, data.StdPrecision);
        }

        /* Magnitude with an explicit direction glyph in front, for figures whose
           sign is the message (net movement, line amounts). */
        function signedMoney(value, symbol, precision) {
            var v = (+value) || 0;
            var prefix = v < 0 ? "− " : (v > 0 ? "+ " : "");
            return prefix + fmtMoney(Math.abs(v), symbol, precision);
        }

        /* A line keeps its own currency when it carries one that differs from
           the bank account's; otherwise the grid cell carries no symbol - the
           section summary and the hero already name the currency, and the
           symbol would eat width in a narrow panel. */
        function lineAmount(line) {
            var own = line && +line.C_Currency_ID > 0 && +line.C_Currency_ID !== +data.C_Currency_ID;
            return own
                ? signedMoney(line.StmtAmt, line.CurSymbol || line.CurISO, line.StdPrecision)
                : signedMoney(line.StmtAmt, "", data.StdPrecision);
        }

        /* Grid cells of the account breakdown carry no symbol either. */
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
                return d.toLocaleDateString(window.navigator.language, { year: "numeric", month: "short", day: "2-digit" })
                    + " · " + d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
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

        /* Account number is masked HERE, never in SQL: four characters or fewer
           are shown as they are, anything longer keeps only its last four. */
        function maskAccountNo(value) {
            var s = (value === null || value === undefined) ? "" : String(value).replace(/\s+/g, "");
            if (!s) return "";
            if (s.length <= 4) return s;
            return "•••• " + s.substring(s.length - 4);
        }

        function yesNo(flag) {
            return flag ? msg("VAS_292_Yes", "Yes") : msg("VAS_292_No", "No");
        }

        /* ---------------------------------------------------------------- */
        /*  Document state                                                  */
        /* ---------------------------------------------------------------- */

        function isPosted() { return !!(data && data.Posted === "Y"); }
        function isReversedOrVoided() { return !!(data && (data.DocStatus === "RE" || data.DocStatus === "VO")); }
        function isCompletedOrClosed() { return !!(data && (data.DocStatus === "CO" || data.DocStatus === "CL")); }
        /* Lines can still change while the document is neither completed nor
           reversed - that is when an empty lines section is worth showing. */
        function isEditable() { return !!(data && !isCompletedOrClosed() && !isReversedOrVoided()); }
        /* Both decided on the server, on the decimal values. */
        function isBalanced() { return !!(data && data.IsBalanced); }
        function isPostingBalanced() { return !!(data && data.IsPostingBalanced); }
        function unmatchedCount() { return (data && +data.UnmatchedCount) || 0; }
        function lineCount() { return (data && +data.LineCount) || 0; }
        function hasFacts() { return !!(data && +data.EntryCount > 0); }

        /* Hero tone follows the headline fact: reversed / voided = risk, an open
           difference or a completed-but-unposted journal = warning, posted =
           success, anything still moving = info. */
        function heroTone() {
            if (isReversedOrVoided()) return "crit";
            if (!isBalanced()) return "warn";
            if (isCompletedOrClosed() && !isPosted()) return "warn";
            if (isPosted()) return "ok";
            return "info";
        }

        /* ---------------------------------------------------------------- */
        /*  Lifecycle                                                       */
        /* ---------------------------------------------------------------- */

        this.init = function () {
            $root = $('<div class="' + CLS + 'root"></div>');
            $body = $('<div class="' + CLS + 'body"></div>');
            $emptyState = $('<div class="' + CLS + 'empty" style="display:none;"></div>');
            $emptyState.text(msg("VAS_292_NoData", "No banking journal selected"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            bindEvents();
            resolveAccountWindow();
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
                if (name === "view-account") viewAccount();
                else if (name === "view-lines") viewAllLines();
                else if (name === "view-accounting") viewAccounting();
                else if (name === "lines-prev") pageLines(-1);
                else if (name === "lines-next") pageLines(1);
            } catch (e) { if (window.console) console.log(e); }
        }

        /* Resolve the Bank Account window ONCE, up front, by name. "View account"
           is drawn disabled until it resolves - a link that navigates nowhere is
           worse than a disabled one. This races the first data load, so whichever
           finishes second repaints the section header. */
        function resolveAccountWindow() {
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.getWindowId !== "function") return;
            VAS.ZoomUtil.getWindowId(ZOOM_ACCOUNT_WINDOW_NEW, ZOOM_ACCOUNT_WINDOW_OLD)
                .then(function (id) {
                    if (disposed) return;
                    accountWindowId = Number(id) || 0;
                    if (accountWindowId > 0 && data) repaintSection("bank", renderBankAccount);
                });
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
                url: VIS.Application.contextUrl + "VAS/VAS_292_BankingJournalRightPanel/GetJournalOverview",
                type: "GET",
                dataType: "json",
                data: { C_BankStatement_ID: recordID },
                success: function (raw) {
                    /* Reply for a record the panel has already left. Whoever
                       superseded us owns the busy indicator and the in-flight
                       flag now, so this reply must not touch either. */
                    if (token !== fetchToken) return;
                    inFlight = false;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (data && !(+data.C_BankStatement_ID > 0)) data = null;   // not accessible / not found
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
                    error(msg("VAS_292_LoadFailed", "Could not load the banking journal details."));
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

            /* Composition rule: hero -> headered sections, in the reference's
               order. A section that has nothing meaningful to say returns null
               and is not drawn. */
            $body.append(renderHero());
            appendSection(renderExceptions());
            appendSection(renderBankAccount());
            appendSection(renderBalances());
            appendSection(renderDetails());
            appendSection(renderLines());
            appendSection(renderAccounting());
            appendSection(renderAudit());

            resetScroll();
        }

        function appendSection($sec) {
            if ($sec) $body.append($sec);
        }

        /* Repaints ONE section in place (keyed by its data-sec name); the rest
           of the panel stays as it is. */
        function repaintSection(key, renderer) {
            if (!$body || !data) return;
            var $old = $body.children('[data-sec="' + key + '"]').first();
            var $new = renderer();
            if (!$old.length || !$new) return;
            $old.replaceWith($new);
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

        function section(key) {
            return $('<section class="' + CLS + 'sec"></section>').attr("data-sec", key);
        }

        function sectionHeader(title, summary, $action) {
            var $h = $('<div class="' + CLS + 'secHead"></div>');
            $h.append($('<span class="' + CLS + 'secTitle"></span>').text(title).attr("title", title));
            var $right = $('<span class="' + CLS + 'secRight"></span>');
            if (summary) $right.append($('<span class="' + CLS + 'secSum"></span>').text(summary).attr("title", summary));
            if ($action) $right.append($action);
            $h.append($right);
            return $h;
        }

        /* Section Header - action variant. Drawn disabled (not hidden) when the
           framework capability behind it is not available. */
        function sectionAction(action, iconName, label, enabled) {
            var $b = $('<button type="button" class="' + CLS + 'secAction"></button>')
                .attr("data-action", action)
                .attr("title", label)
                .prop("disabled", !enabled);
            if (!enabled) $b.attr("aria-disabled", "true");
            $b.append(icon(iconName)).append($('<span></span>').text(label));
            return $b;
        }

        function cell(cls, text, title) {
            return $('<span class="' + CLS + cls + '"></span>').text(text).attr("title", title || text);
        }

        function chip(text, tone) {
            return $('<span class="' + CLS + 'chip ' + CLS + 'tone-' + tone + '"></span>').text(text).attr("title", text);
        }

        /* Metric Grid cell (Detail Card variant): label, value, optional meta,
           optional semantic tone on the value. */
        function metric(label, value, meta, tone) {
            var $c = $('<div class="' + CLS + 'metric"></div>');
            $c.append($('<div class="' + CLS + 'metricLabel"></div>').text(label));
            var v = (value === null || value === undefined || value === "") ? "—" : value;
            $c.append($('<div class="' + CLS + 'metricValue' + (tone ? " " + CLS + "tx-" + tone : "") + '"></div>').text(v).attr("title", v));
            if (meta) $c.append($('<div class="' + CLS + 'metricMeta"></div>').text(meta).attr("title", meta));
            return $c;
        }

        function detailCard() {
            var $card = $('<div class="' + CLS + 'card"></div>');
            var $grid = $('<div class="' + CLS + 'metrics"></div>');
            $card.append($grid);
            $card.grid = $grid;
            return $card;
        }

        /* Compact List row: primary + optional meta on the left; a status pill
           OR a trailing value on the right. */
        function row(label, value, meta, pill) {
            var $r = $('<div class="' + CLS + 'row"></div>');
            var $left = $('<div class="' + CLS + 'rowLeft"></div>');
            $left.append($('<div class="' + CLS + 'rowLabel"></div>').text(label).attr("title", label));
            if (meta) $left.append($('<div class="' + CLS + 'rowMeta"></div>').text(meta).attr("title", meta));
            $r.append($left);
            if (pill) {
                $r.append($('<span class="' + CLS + 'pill ' + CLS + 'tone-' + pill.tone + '"></span>').text(pill.label));
            } else {
                var v = (value === null || value === undefined || value === "") ? "—" : value;
                $r.append($('<div class="' + CLS + 'rowValue"></div>').text(v).attr("title", v));
            }
            return $r;
        }

        /* 3 · Hero status card ------------------------------------------- */

        function renderHero() {
            var $hero = $('<section class="' + CLS + 'hero ' + CLS + 'tone-' + heroTone() + '"></section>').attr("data-sec", "hero");

            var $top = $('<div class="' + CLS + 'heroTop"></div>');
            var $id = $('<div class="' + CLS + 'heroId"></div>');
            $id.append($('<div class="' + CLS + 'heroTitle"></div>').text(data.DocumentNo || "").attr("title", data.DocumentNo || ""));
            /* Subtitle: statement date · bank account currency · organisation. */
            var subtitle = joinBits([fmtDate(data.StatementDate), data.CurISO, data.OrgName]);
            if (subtitle) {
                $id.append($('<div class="' + CLS + 'heroSub"></div>').text(subtitle).attr("title", subtitle));
            }
            $top.append($id);
            var status = data.DocStatusName || data.DocStatus || "";
            if (status) $top.append($('<span class="' + CLS + 'chip ' + CLS + 'onTint"></span>').text(status).attr("title", status));
            $hero.append($top);

            /* Emphasis row: net bank movement (ending - beginning) with its
               direction, and the line count as the qualifier. */
            var $emph = $('<div class="' + CLS + 'heroEmph"></div>');
            var $val = $('<div class="' + CLS + 'heroValueWrap"></div>');
            $val.append($('<div class="' + CLS + 'heroLabel"></div>').text(msg("VAS_292_NetBankMovement", "Net bank movement")));
            var movement = signedMoney(data.NetMovement, data.CurSymbol || data.CurISO, data.StdPrecision);
            var moveTone = (+data.NetMovement || 0) < 0 ? "risk" : "success";
            $val.append($('<div class="' + CLS + 'heroValue ' + CLS + 'tx-' + moveTone + '"></div>').text(movement).attr("title", movement));
            $emph.append($val);
            $emph.append($('<div class="' + CLS + 'heroQual"></div>').text(fmt(msg("VAS_292_LineCount", "{0} journal lines"), lineCount())));
            $hero.append($emph);

            /* Posting / balance / matching chips - each a data fact. */
            var $chips = $('<div class="' + CLS + 'chips"></div>');
            $chips.append(chip(isPosted() ? msg("VAS_292_Posted", "Posted") : msg("VAS_292_Unposted", "Unposted"), isPosted() ? "success" : "warning"));
            $chips.append(chip(isBalanced() ? msg("VAS_292_StatementBalanced", "Statement balanced") : msg("VAS_292_DifferenceExists", "Difference exists"), isBalanced() ? "success" : "risk"));
            var un = unmatchedCount();
            $chips.append(chip(un === 0 ? msg("VAS_292_AllLinesMatched", "All lines matched") : fmt(msg("VAS_292_UnmatchedCount", "{0} unmatched"), un), un === 0 ? "success" : "warning"));
            $hero.append($chips);

            return $hero;
        }

        /* Exceptions - compact list, only when at least one exists ---------- */

        function renderExceptions() {
            var items = [];
            if (!isBalanced()) {
                items.push(row(msg("VAS_292_StatementDifference", "Statement difference"), "",
                    joinBits([money(data.StatementDifference), msg("VAS_292_StatementDifferenceMeta", "Statement difference requires review")]),
                    { label: msg("VAS_292_Review", "Review"), tone: "risk" }));
            }
            var un = unmatchedCount();
            if (un > 0) {
                items.push(row(msg("VAS_292_UnmatchedLines", "Unmatched journal lines"), "",
                    fmt(msg("VAS_292_UnmatchedLinesMeta", "{0} journal line(s) are not matched"), un),
                    { label: msg("VAS_292_Review", "Review"), tone: "warning" }));
            }
            if (isCompletedOrClosed() && !isPosted()) {
                items.push(row(msg("VAS_292_CompletedNotPosted", "Completed but not posted"), "",
                    msg("VAS_292_CompletedNotPostedMeta", "Completed Banking Journal is not posted"),
                    { label: msg("VAS_292_Review", "Review"), tone: "warning" }));
            }
            if (!items.length) return null;

            var $sec = section("exceptions");
            $sec.append(sectionHeader(msg("VAS_292_Exceptions", "Exceptions"), fmt(msg("VAS_292_OpenCount", "{0} open"), items.length), null));
            var $list = $('<div class="' + CLS + 'list"></div>');
            for (var i = 0; i < items.length; i++) $list.append(items[i]);
            $sec.append($list);
            return $sec;
        }

        /* Bank account - metric grid, detail-card wrap, 6 cells ------------- */

        function canViewAccount() {
            return !!(data && +data.C_BankAccount_ID > 0 && accountWindowId > 0
                && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === "function");
        }

        function renderBankAccount() {
            var $sec = section("bank");
            $sec.append(sectionHeader(msg("VAS_292_BankAccount", "Bank account"), "",
                sectionAction("view-account", "external", msg("VAS_292_ViewAccount", "View account"), canViewAccount())));

            var $card = detailCard();
            $card.grid.append(metric(msg("VAS_292_Bank", "Bank"), data.BankName, joinBits([data.SwiftCode, data.RoutingNo])));
            $card.grid.append(metric(msg("VAS_292_Account", "Account"), data.BankAccountName || maskAccountNo(data.AccountNo),
                data.BankAccountName ? maskAccountNo(data.AccountNo) : ""));
            var curText = data.CurISO ? (data.CurISO + (data.CurSymbol && data.CurSymbol !== data.CurISO ? " (" + data.CurSymbol + ")" : "")) : "";
            $card.grid.append(metric(msg("VAS_292_Currency", "Currency"), curText, ""));
            $card.grid.append(metric(msg("VAS_292_CurrentBalance", "Current balance"), money(data.CurrentBalance), ""));
            var unmatchedBal = +data.UnMatchedBalance || 0;
            $card.grid.append(metric(msg("VAS_292_UnmatchedBalance", "Unmatched balance"), money(unmatchedBal), "", unmatchedBal ? "warning" : ""));
            $card.grid.append(metric(msg("VAS_292_AccountType", "Account type"), data.BankAccountTypeName || data.BankAccountType, ""));
            $sec.append($card);
            return $sec;
        }

        /* Balance summary - metric grid, detail-card wrap, 6 cells ---------- */

        function renderBalances() {
            var $sec = section("balances");
            $sec.append(sectionHeader(msg("VAS_292_BalanceSummary", "Balance summary"),
                joinBits([data.CurISO, fmt(msg("VAS_292_LinesShort", "{0} lines"), lineCount())]), null));

            var $card = detailCard();
            $card.grid.append(metric(msg("VAS_292_BeginningBalance", "Beginning balance"), money(data.BeginningBalance), msg("VAS_292_BeforeActivity", "Before journal activity")));
            $card.grid.append(metric(msg("VAS_292_EndingBalance", "Ending balance"), money(data.EndingBalance), msg("VAS_292_StatementEnding", "Statement ending balance")));
            $card.grid.append(metric(msg("VAS_292_Inflow", "Inflow"), money(data.InflowAmount), msg("VAS_292_InflowMeta", "Positive statement lines"), "success"));
            $card.grid.append(metric(msg("VAS_292_Outflow", "Outflow"), money(data.OutflowAmount), msg("VAS_292_OutflowMeta", "Negative statement lines"), "risk"));
            /* Interest rides as the charges cell's meta so the grid keeps an even
               cell count; it is left out when there is none. */
            var interest = (+data.InterestAmount || 0) !== 0 ? fmt(msg("VAS_292_InterestMeta", "Interest {0}"), money(data.InterestAmount)) : "";
            $card.grid.append(metric(msg("VAS_292_Charges", "Charges"), money(data.ChargeAmount), interest));
            $card.grid.append(metric(msg("VAS_292_Difference", "Difference"), money(data.StatementDifference),
                isBalanced() ? msg("VAS_292_Balanced", "Balanced") : msg("VAS_292_RequiresReview", "Requires review"),
                isBalanced() ? "" : "risk"));
            $sec.append($card);
            return $sec;
        }

        /* Journal details - metric grid, two values per row ----------------- */

        function renderDetails() {
            var $sec = section("details");
            $sec.append(sectionHeader(msg("VAS_292_JournalDetails", "Journal details"), "", null));

            /* Six cells, two per row: Name | Statement date, Document status |
               Processed, Approved | Journal type. The description, being free
               text, takes a full-width cell of its own beneath and is hidden
               when blank. */
            var $card = detailCard();
            $card.grid.append(metric(msg("VAS_292_Name", "Name"), data.Name, ""));
            $card.grid.append(metric(msg("VAS_292_StatementDate", "Statement date"), fmtDate(data.StatementDate), ""));
            $card.grid.append(metric(msg("VAS_292_DocumentStatus", "Document status"), data.DocStatusName || data.DocStatus, ""));
            $card.grid.append(metric(msg("VAS_292_Processed", "Processed"), yesNo(data.Processed), ""));
            $card.grid.append(metric(msg("VAS_292_Approved", "Approved"), yesNo(data.IsApproved), ""));
            $card.grid.append(metric(msg("VAS_292_JournalType", "Journal type"),
                data.IsManual ? msg("VAS_292_Manual", "Manual") : msg("VAS_292_ImportedSystem", "Imported / system"), ""));
            if (data.Description) {
                var $desc = $('<div class="' + CLS + 'metric ' + CLS + 'metricWide"></div>');
                $desc.append($('<div class="' + CLS + 'metricLabel"></div>').text(msg("VAS_292_Description", "Description")));
                $desc.append($('<div class="' + CLS + 'metricText"></div>').text(data.Description).attr("title", data.Description));
                $card.grid.append($desc);
            }
            $sec.append($card);
            return $sec;
        }

        /* Journal lines - data grid, paged on the server -------------------- */

        function canViewLines() {
            return !!(data && +data.C_BankStatement_ID > 0 && hostPanel() !== null);
        }

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

            var $sec = section("lines");
            $sec.append(sectionHeader(
                msg("VAS_292_JournalLines", "Journal lines"),
                total ? fmt(msg("VAS_292_MatchedSummary", "{0} matched · {1} unmatched"), +data.MatchedCount || 0, unmatchedCount()) : "",
                sectionAction("view-lines", "list", msg("VAS_292_ViewAllLines", "View all lines"), canViewLines())));

            if (!total) {
                $sec.append($('<div class="' + CLS + 'secEmpty"></div>').text(msg("VAS_292_NoLines", "No lines on this journal yet.")));
                return $sec;
            }

            var $grid = $('<div class="' + CLS + 'dg ' + CLS + 'dgLines"></div>');

            /* Header row: the leading affordance column carries no label. */
            var $head = $('<div class="' + CLS + 'dgHead"></div>');
            $head.append($('<span aria-hidden="true"></span>'));
            $head.append(cell("dgH", msg("VAS_292_Line", "Line")));
            $head.append(cell("dgH", msg("VAS_292_Description", "Description")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_292_Amount", "Amount")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_292_Match", "Match")));
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
                   where the rows will go, so the grid keeps its head and the rest
                   of the panel stays usable. */
                $grid.append($('<div class="' + CLS + 'dgBusy">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                    '</div>'));
            }
            $sec.append($grid);

            if (pageCount > 1) $sec.append(pager(linesPage, pageCount));
            return $sec;
        }

        /* Canonical footer pager (same shape as the VAS_020 widget pager):
           "Showing a–b of N" on the left, compact  <  n of m  >  on the right. */
        function pager(page, pageCount) {
            var total = lineCount();
            var from = total ? page * linesPerPage() + 1 : 0;
            var to = Math.min(total, (page + 1) * linesPerPage());
            var of = msg("VAS_292_Of", "of");

            var $p = $('<div class="' + CLS + 'pager"></div>');
            var showing = msg("VAS_292_Showing", "Showing") + " " + from + "–" + to + " " + of + " " + total;
            $p.append($('<span class="' + CLS + 'pgInfo"></span>').text(showing).attr("title", showing));

            var $nav = $('<div class="' + CLS + 'pgNav"></div>');
            var $prev = $('<button type="button" class="' + CLS + 'pgBtn"></button>')
                .attr("data-action", "lines-prev")
                .attr("title", msg("VAS_292_Previous", "Previous page"))
                .attr("aria-label", msg("VAS_292_Previous", "Previous page"))
                .prop("disabled", page <= 0);
            $prev.append(icon("prev"));
            var $next = $('<button type="button" class="' + CLS + 'pgBtn"></button>')
                .attr("data-action", "lines-next")
                .attr("title", msg("VAS_292_Next", "Next page"))
                .attr("aria-label", msg("VAS_292_Next", "Next page"))
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
           the grid's column head and first row sit at the top of the view. */
        function repaintLines(scrollToFirstRow) {
            if (!$body) return;
            var $old = $body.children('[data-sec="lines"]').first();
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
           is asked from the server (20 rows per request) and painted when it
           lands - unless the record changed meanwhile, in which case the reply
           belongs to a record the panel has already left and is dropped. */
        function pageLines(delta) {
            var next = linesPage + delta;
            if (next < 0 || next > linePageCount() - 1) return;
            linesPage = next;
            repaintLines(true);
            if (linePages.hasOwnProperty(next)) return;

            var recordId = +data.C_BankStatement_ID;
            var token = ++linesFetchToken;
            var mainToken = fetchToken;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_292_BankingJournalRightPanel/GetJournalLines",
                type: "GET",
                dataType: "json",
                data: { C_BankStatement_ID: recordId, page: next, pageSize: linesPerPage() },
                success: function (raw) {
                    if (token !== linesFetchToken || mainToken !== fetchToken || !data || +data.C_BankStatement_ID !== recordId) return;
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
                    error(msg("VAS_292_LoadFailed", "Could not load the banking journal details."));
                }
            });
        }

        function lineRow(line, isLast) {
            var amt = +line.StmtAmt || 0;
            var dir = amt > 0 ? "inflow" : (amt < 0 ? "outflow" : "neutral");
            var $r = $('<div class="' + CLS + 'dgRow' + (isLast ? " " + CLS + "last" : "") + '"></div>');

            /* Leading affordance: the line's direction, row-specific. */
            var $ic = $('<span class="' + CLS + 'dgIcon ' + CLS + 'is-' + dir + '"></span>')
                .attr("title", dir === "inflow" ? msg("VAS_292_InflowLine", "Inflow line")
                    : (dir === "outflow" ? msg("VAS_292_OutflowLine", "Outflow line") : msg("VAS_292_ZeroLine", "Zero amount line")));
            $ic.append(icon(dir));
            $r.append($ic);

            $r.append(cell("dgCell " + CLS + "muted", String(line.Line || "")));

            /* Description; the tooltip carries the line's detail. */
            var detail = lineTooltip(line);
            $r.append(cell("dgCell " + CLS + "dgPrimary", line.Description || "—", detail));

            var text = lineAmount(line);
            $r.append(cell("dgCell " + CLS + "dgAmount " + CLS + "right " + CLS + "tx-" + (dir === "inflow" ? "success" : (dir === "outflow" ? "risk" : "muted")), text));

            var matchText = line.IsMatched ? msg("VAS_292_Matched", "Matched") : msg("VAS_292_Unmatched", "Unmatched");
            $r.append(cell("dgCell " + CLS + "dgMatch " + CLS + "right " + CLS + "tx-" + (line.IsMatched ? "success" : "warning"), matchText));
            return $r;
        }

        /* Everything the row cannot show inline, one fact per line. */
        function lineTooltip(line) {
            var bits = [];
            if (line.Description) bits.push(line.Description);
            if (line.StatementLineDate) bits.push(msg("VAS_292_StatementLineDate", "Statement line date") + ": " + fmtDate(line.StatementLineDate));
            if (line.ValutaDate) bits.push(msg("VAS_292_EffectiveDate", "Effective date") + ": " + fmtDate(line.ValutaDate));
            if (line.DateAcct) bits.push(msg("VAS_292_AccountDate", "Account date") + ": " + fmtDate(line.DateAcct));
            if (line.ReferenceNo) bits.push(msg("VAS_292_ReferenceNo", "Reference no.") + ": " + line.ReferenceNo);
            if (line.PaymentDocumentNo) bits.push(msg("VAS_292_Payment", "Payment") + ": " + line.PaymentDocumentNo);
            if (line.BPartnerName) bits.push(msg("VAS_292_BusinessPartner", "Business partner") + ": " + line.BPartnerName);
            if (line.ChargeName) bits.push(msg("VAS_292_Charge", "Charge") + ": " + line.ChargeName);
            return bits.join("\n");
        }

        /* Accounting impact - metric grid + account data grid --------------- */

        function canViewAccounting() {
            return !!(isPosted() && window.VIS && typeof VIS.AcctViewer === "function"
                && $self.curTab && typeof $self.curTab.getAD_Window_ID === "function");
        }

        function renderAccounting() {
            /* A journal that is neither posted nor completed has no accounting
               impact to report - the section is not applicable, so it is hidden. */
            if (!isPosted() && !isCompletedOrClosed()) return null;

            var postedText = isPosted() ? msg("VAS_292_Posted", "Posted") : msg("VAS_292_Unposted", "Unposted");
            var $sec = section("accounting");
            $sec.append(sectionHeader(msg("VAS_292_AccountingImpact", "Accounting impact"), postedText,
                sectionAction("view-accounting", "book", msg("VAS_292_ViewAccounting", "View accounting"), canViewAccounting())));

            if (!hasFacts()) {
                $sec.append($('<div class="' + CLS + 'secEmpty"></div>').text(msg("VAS_292_NoEntries", "No accounting entries posted yet.")));
                return $sec;
            }

            var entries = fmt(msg("VAS_292_EntryCount", "{0} accounting entries"), +data.EntryCount || 0);
            var $card = detailCard();
            $card.grid.append(metric(msg("VAS_292_TotalDebit", "Total debit"), money(data.TotalDebit), entries));
            $card.grid.append(metric(msg("VAS_292_TotalCredit", "Total credit"), money(data.TotalCredit), entries));
            $card.grid.append(metric(msg("VAS_292_Difference", "Difference"), money((+data.TotalDebit || 0) - (+data.TotalCredit || 0)),
                isPostingBalanced() ? msg("VAS_292_Balanced", "Balanced") : msg("VAS_292_PostingImbalance", "Posting imbalance"),
                isPostingBalanced() ? "" : "risk"));
            $card.grid.append(metric(msg("VAS_292_PostingStatus", "Posting status"), postedText, ""));
            $sec.append($card);

            var accounts = data.Accounts || [];
            if (!accounts.length) return $sec;   // never an empty account grid

            var $grid = $('<div class="' + CLS + 'dg ' + CLS + 'dgAccounts"></div>');
            var $head = $('<div class="' + CLS + 'dgHead"></div>');
            $head.append($('<span aria-hidden="true"></span>'));
            $head.append(cell("dgH", msg("VAS_292_Account", "Account")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_292_Debit", "Debit")));
            $head.append(cell("dgH " + CLS + "right", msg("VAS_292_Credit", "Credit")));
            $grid.append($head);
            for (var i = 0; i < accounts.length; i++) {
                $grid.append(accountRow(accounts[i], i === accounts.length - 1));
            }
            $sec.append($grid);
            return $sec;
        }

        function accountRow(acct, isLast) {
            var dr = +acct.DebitAmount || 0;
            var cr = +acct.CreditAmount || 0;
            /* The row's net side decides its glyph: debit-heavy reads as an
               inflow arrow, credit-heavy as an outflow arrow, a wash as neutral. */
            var side = dr > cr ? "inflow" : (cr > dr ? "outflow" : "neutral");
            var $r = $('<div class="' + CLS + 'dgRow' + (isLast ? " " + CLS + "last" : "") + '"></div>');

            var $ic = $('<span class="' + CLS + 'dgIcon ' + CLS + 'is-' + side + '"></span>')
                .attr("title", side === "inflow" ? msg("VAS_292_NetDebit", "Net debit") : (side === "outflow" ? msg("VAS_292_NetCredit", "Net credit") : ""));
            $ic.append(icon(side));
            $r.append($ic);

            var label = joinBits([acct.AccountValue, acct.AccountName]);
            $r.append(cell("dgCell " + CLS + "dgPrimary", label));
            $r.append(amountCell(dr));
            $r.append(amountCell(cr));
            return $r;
        }

        /* A zero leg reads as an em dash rather than as "0.00", so the eye
           lands on the side that actually moved. */
        function amountCell(value) {
            if (!value) {
                return $('<span class="' + CLS + 'dgCell ' + CLS + 'dgAmount ' + CLS + 'right ' + CLS + 'isEmpty"></span>').text("—");
            }
            var text = amount(value);
            return $('<span class="' + CLS + 'dgCell ' + CLS + 'dgAmount ' + CLS + 'right"></span>').text(text).attr("title", text);
        }

        /* Audit - step rail --------------------------------------------------- */

        /* The workflow activity that ran a given document action (PR = prepare,
           CO = complete), or null. Closed activities (WFState CC) win over open
           ones; the latest closed one is the fact of record. */
        function findDocActivity(docAction) {
            var wf = (data && data.WorkflowSteps) || [];
            var found = null;
            for (var i = 0; i < wf.length; i++) {
                var w = wf[i];
                if (w.NodeAction !== "D" || w.DocAction !== docAction) continue;
                if (w.WFState === "CC") found = w;
                else if (!found) found = w;
            }
            return found;
        }

        /* The last human (user-choice / window / form) activity - the approval
           step a person acted on, or null when the flow has none. */
        function findApprovalActivity() {
            var wf = (data && data.WorkflowSteps) || [];
            var found = null;
            for (var i = 0; i < wf.length; i++) {
                var w = wf[i];
                if (w.NodeAction === "C" || w.NodeAction === "W" || w.NodeAction === "X") found = w;
            }
            return found;
        }

        function wfStepState(w) {
            if (!w) return "pending";
            if (w.WFState === "CC") return "done";
            if (w.WFState === "OR" || w.WFState === "OS" || w.WFState === "ON") return "active";
            if (w.WFState === "CA" || w.WFState === "CT") return "blocked";
            return "pending";
        }

        /* Composes the trail from the facts the server reports. Titles are
           localised here; the server only tells what happened.
             1. Drafted      - always done (the record exists): creator + moment
             2. In progress  - the Prepare document action, or the status itself
             3. Approved     - the human approval activity or the IsApproved flag;
                               left out when the flow has neither
             4. Completed    - the Complete document action, or the status itself;
                               a reversed / voided journal reads as blocked here
             5. Posted       - Fact_Acct moment + user
           Exactly one step is active: the first that is neither done nor
           blocked. Every step after a blocked one is pending. */
        function buildAuditSteps() {
            var steps = [];

            steps.push({
                title: msg("VAS_292_Drafted", "Drafted"),
                meta: joinBits([data.CreatedByName, fmtStamp(data.Created)]),
                state: "done"
            });

            var prep = findDocActivity("PR");
            var moved = data.DocStatus === "IP" || isCompletedOrClosed();
            if (prep && prep.WFState === "CC") {
                steps.push({ title: msg("VAS_292_InProgress", "In progress"), meta: joinBits([prep.ActorName, fmtStamp(prep.Updated)]), state: "done" });
            } else if (moved) {
                steps.push({ title: msg("VAS_292_InProgress", "In progress"), meta: data.DocStatus === "IP" ? joinBits([data.UpdatedByName, fmtStamp(data.Updated)]) : "", state: "done" });
            } else {
                steps.push({ title: msg("VAS_292_InProgress", "In progress"), meta: data.DocStatusName || "", state: isReversedOrVoided() ? "pending" : "active" });
            }

            var appr = findApprovalActivity();
            if (appr) {
                var st = wfStepState(appr);
                steps.push({
                    title: msg("VAS_292_Approved", "Approved"),
                    meta: joinBits([appr.NodeName, appr.ActorName, fmtStamp(st === "done" ? (appr.Updated || appr.Created) : appr.Created), st === "done" ? "" : appr.WFStateName]),
                    state: st
                });
            } else if (data.IsApproved) {
                steps.push({ title: msg("VAS_292_Approved", "Approved"), meta: joinBits([data.UpdatedByName, fmtStamp(data.Updated)]), state: "done" });
            } else if (!isCompletedOrClosed() && !isReversedOrVoided() && !isPosted()) {
                /* Still open and not yet approved: the step is ahead of the user. */
                steps.push({ title: msg("VAS_292_Approved", "Approved"), meta: msg("VAS_292_AwaitingApproval", "awaiting approval"), state: "active" });
            }
            /* A completed journal that never carried an approval simply has no
               Approved step - nothing is invented for it. */

            var comp = findDocActivity("CO");
            if (isReversedOrVoided()) {
                steps.push({ title: data.DocStatusName || data.DocStatus, meta: joinBits([data.UpdatedByName, fmtStamp(data.Updated)]), state: "blocked" });
            } else if (isCompletedOrClosed()) {
                var actor = (comp && comp.WFState === "CC") ? comp.ActorName : data.UpdatedByName;
                var when = (comp && comp.WFState === "CC") ? comp.Updated : data.Updated;
                steps.push({ title: msg("VAS_292_Completed", "Completed"), meta: joinBits([actor, fmtStamp(when)]), state: "done" });
            } else {
                steps.push({ title: msg("VAS_292_Completed", "Completed"), meta: msg("VAS_292_AwaitingCompletion", "awaiting completion"), state: "active" });
            }

            if (isPosted()) {
                steps.push({ title: msg("VAS_292_Posted", "Posted"), meta: joinBits([data.PostedByName, fmtStamp(data.PostedOn)]), state: "done" });
            } else {
                steps.push({ title: msg("VAS_292_Posted", "Posted"), meta: msg("VAS_292_NotPostedYet", "not posted yet"), state: "active" });
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

        /* Drawn as the rail the payment / GL journal panels use - a marker node
           per step, a hairline connector, title + meta in plain text, no tinted
           card. The node carries the state: done = filled green check,
           active = blue ring with a dot, pending = grey ring, blocked = filled
           red cross. */
        function renderAudit() {
            var steps = buildAuditSteps();
            var done = 0;
            for (var i = 0; i < steps.length; i++) if (steps[i].state === "done") done++;

            var $sec = section("audit");
            $sec.append(sectionHeader(msg("VAS_292_Audit", "Audit"),
                fmt(msg("VAS_292_StepsComplete", "{0} of {1} complete"), done, steps.length), null));

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

        /* View account: the Bank Account window positioned on the statement's
           account. The id was resolved by name up front; a miss leaves the
           action disabled, so this never zooms nowhere. */
        function viewAccount() {
            if (!canViewAccount()) return;
            VAS.ZoomUtil.zoomToRecord(ZOOM_ACCOUNT_COLUMN, +data.C_BankAccount_ID, accountWindowId,
                ZOOM_ACCOUNT_WINDOW_NEW, ZOOM_ACCOUNT_WINDOW_OLD);
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

        /* The statement's own line tab in the hosting window: the first tab
           whose table is C_BankStatementLine (id from the server, never
           hard-coded) that sits directly under the current tab. Falls back to
           the first line tab of the window when levels cannot be read. */
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

        /* View all lines: switches the HOSTING window to the statement's line
           tab - exactly what a click on that tab header does (the APanel
           registers every tab as the action windowNo_AD_Tab_ID and routes the
           header click through onTabChange). The current record stays
           selected, so the line tab opens on this statement's lines. No second
           window instance is ever opened. Degrades silently: a click can never
           throw. */
        function viewAllLines() {
            if (!data || !(+data.C_BankStatement_ID > 0)) return;
            try {
                var aPanel = hostPanel();
                var lineTab = aPanel ? findLineTab(aPanel) : null;
                if (!lineTab || typeof lineTab.getAD_Tab_ID !== "function") {
                    if (window.console) console.log("VAS_292: line tab not found in the hosting window");
                    return;
                }
                aPanel.onTabChange($self.windowNo + "_" + lineTab.getAD_Tab_ID());
            } catch (e) { if (window.console) console.log(e); }
        }

        /* View accounting: the framework account viewer on this record - the
           same dialog the window's Posted button raises for a posted document
           (VIS.AcctViewer(client, table, record, windowNo, window).showDialog()).
           Nothing is posted or re-posted from here. */
        function viewAccounting() {
            if (!canViewAccounting()) return;
            var tableId = ($self.curTab && typeof $self.curTab.getAD_Table_ID === "function")
                ? +$self.curTab.getAD_Table_ID() : (+data.Table_ID || $self.table_ID);
            var recordId = +data.C_BankStatement_ID;
            if (!(tableId > 0) || !(recordId > 0)) return;
            var viewer = new VIS.AcctViewer(VIS.context.getAD_Client_ID(), tableId, recordId,
                $self.windowNo, $self.curTab.getAD_Window_ID());
            if (viewer && typeof viewer.showDialog === "function") viewer.showDialog();
        }

        this.markDisposed = function () { disposed = true; };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_292_BankingJournalRightPanel.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_292_BankingJournalRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        /* The insert check is what makes New Record / Copy Record behave: the id
           handed in for an unsaved row can still be the previously selected (or
           copied-from) statement's, so the tab's own insert state decides. */
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
    VAS.VAS_292_BankingJournalRightPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) this.fetchData(this.record_ID);
        else this.clear();
    };

    /* Set width as per window width */
    VAS.VAS_292_BankingJournalRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_292_BankingJournalRightPanel.prototype.dispose = function () {
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
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
