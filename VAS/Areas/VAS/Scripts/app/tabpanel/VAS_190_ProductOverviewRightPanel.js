/************************************************************
 * Module Name    : VAS
 * Purpose        : Product Overview right tab panel. Renders a read-only
 *                  contextual summary of the selected M_Product record: an
 *                  identity / lifecycle hero, the attribute-set controls, tax
 *                  classification, stock position and stock by locator, UOM
 *                  conversions, price lists, BOMs (own + where-used), the
 *                  configured quality parameters, vendors, the latest sales and
 *                  purchase orders, recent physical movements, the effective
 *                  posting accounts and a unified activity timeline with
 *                  inline-expanding mail. Data is fetched from
 *                  VAS_190_ProductOverviewRightPanel/GetProductOverview.
 *
 *                  Sections are declared in a registry — key, visibility
 *                  condition, renderer — and drawn by iterating it, each behind
 *                  its own guard. A section whose condition is false, or that
 *                  has nothing in it, is not drawn at all: there are no empty
 *                  shells and no "no data" rows. Activity is the one exception
 *                  and reports "0 events".
 *
 *                  The panel chrome (440px shell, collapse strip, 56px header,
 *                  close button, panel switcher) belongs to the VIS tab-panel
 *                  host, not to this file — a tab panel styles its body and
 *                  lets the framework own the frame.
 *
 *                  All on-screen strings resolve through VIS.Msg.getMsg with an
 *                  English fallback, so an unseeded AD_Message key never renders
 *                  as a raw key.
 * Chronological development:
 *   VAI163   2026-08-10  Created.
 *   VAI163   2026-08-10  - The hero renders the real product image. The server
 *                          resolves it to an absolute URL, a data: URI or an
 *                          application-relative path; only the last needs the
 *                          context prefix, which resolveImageSrc adds. A file
 *                          that has gone missing falls back to the placeholder
 *                          instead of a broken-image glyph.
 *                        - Recent transactions show the document TYPE name in
 *                          front of the number, and the whole row opens that
 *                          document through the shared zoom path — by click and
 *                          by keyboard, since the row is a button.
 *   VAI163   2026-08-10  Activity gained chat comments (CM_Chat / CM_ChatEntry)
 *                        as their own "chat" entry type. The comment itself is
 *                        the row's headline, with the whole text on the tooltip.
 *   VAI163   2026-08-11  Stock details and Recent transactions name the unit
 *                        beside the figure (qtyText, the helper the Stock
 *                        summary already used) instead of printing a bare
 *                        number. Both columns are the product's BASE uom —
 *                        M_Storage.QtyOnHand and M_Transaction.MovementQty —
 *                        and a quantity whose unit the reader has to infer from
 *                        another section is a quantity they can misread.
 *   VAI163   2026-08-11  Activity pages at 15 rows rather than 6, matching every
 *                        other tab panel's feed; at 6 a product with any history
 *                        was mostly pager clicks.
 *   VAI163   2026-08-18  - PAGING WORKS in the three sections that own their row
 *                          container. Stock details, Recent transactions and
 *                          Activity each moved the first page out of the
 *                          paginator's host into their grid / timeline and then
 *                          DELETED that host, so every later repaint drew into a
 *                          node no longer in the document: the pager advanced,
 *                          the rows on screen never changed. paginate() is now
 *                          given the container to paint into and clears only the
 *                          rows it painted, leaving the grid header and the
 *                          timeline rail where they are.
 *                        - A UOM conversion row states which of the product's
 *                          DEFAULT units it is — purchase, sales or consumable
 *                          (M_Product.VAS_PurchaseUOM_ID / VAS_SalesUOM_ID /
 *                          VAS_ConsumableUOM_ID). The columns were read under
 *                          invented names (C_UOM_Purchase_ID / C_UOM_Sales_ID)
 *                          that exist in no schema here, so the flag was never
 *                          raised and every row read "Product-specific conversion"
 *                          and nothing else. UOM rows also sit in the compact list
 *                          the rest of the panel uses, five to a page.
 *                        - Pricing names the UNIT and the ATTRIBUTE SET each price
 *                          belongs to. A price list holds one price per unit and
 *                          per attribute set instance, and with neither stated its
 *                          rows read as the same list repeated with different
 *                          figures. The section's count is of price LISTS, not of
 *                          rows.
 *                        - Accounting reports what the product's own accounting
 *                          tab holds: every account set on M_Product_Acct, under
 *                          whichever of the client's schemas carries them. It used
 *                          to read four columns under the primary schema only and
 *                          fall back to the product CATEGORY's accounts, so a
 *                          product with nothing on its tab showed its category's
 *                          numbers as if they were its own, and accounts that were
 *                          set went unreported.
 *   VAI163   2026-08-18  - Attributes says what each control actually IMPOSES.
 *                          Lot, serial and expiry read Mandatory or Optional
 *                          instead of "On"; lot and serial name the control
 *                          record the set points at; expiry states the shelf
 *                          life from the PRODUCT's guarantee days, falling back
 *                          to the set's. The section header carries the set's
 *                          mandatory type beside its name, and an instance
 *                          attribute states its value TYPE — with the "instance
 *                          attribute" note only on attributes whose own box is
 *                          ticked, where it used to sit on every one of them.
 *                        - Attributes and Pricing page at five rows, as UOM
 *                          conversions do: both sit above the sections a reader
 *                          scrolls for.
 *                        - Stock & availability counts OPEN orders under Reserved
 *                          and On order — "completed sales orders" described the
 *                          document's status, not whether anything is still to
 *                          move — and On order carries a count at all, which it
 *                          never did. Where the figure comes from a single order,
 *                          that order's document and due dates are named.
 *   VAI163   2026-08-18  - Activity is VAS_092's feed, to the row: a bordered
 *                          list of tag chip | headline + sub-lines | timestamp
 *                          and author, with a field edit headlining on the FIELD
 *                          and stating the move beneath it (was X → now Y, the
 *                          old value struck through, an em dash for a cleared
 *                          one), a mail naming every address it went to, and its
 *                          body folded under its own row. The timeline of cards
 *                          on a rail is gone — no other overview panel used it,
 *                          and it spent most of a 440px panel on the rail and
 *                          the card borders.
 *                        - An order row's quantity is the ENTERED one, in the
 *                          unit named beside it. It was the base-unit figure
 *                          under the line's own unit name — 120 pieces reported
 *                          as 120 cartons — and both sections did it.
 *                        - An order row says how much of it has actually moved:
 *                          Due, Short received / delivered or Fully received /
 *                          delivered, with what is still open beside the
 *                          quantity.
 *                        - Recent transactions name WHERE each movement happened
 *                          (warehouse, locator, attributes) under the document.
 *                          One document posts a row per line, so two movements on
 *                          one receipt were the same row printed twice. The
 *                          header counts what is on screen AND what it was drawn
 *                          from ("showing 20 of 57"), and the money column is the
 *                          COST the movement was booked at, not the price agreed
 *                          on the order behind it.
 *                        - A transaction row opens the screen its document
 *                          actually lives on — receipt, delivery, customer or
 *                          vendor return, physical inventory, internal use,
 *                          material transfer — named by the server from the
 *                          document behind the movement's LINE. The table alone
 *                          could not answer it, so every M_InOut movement opened
 *                          the same window whichever direction it was.
 *                        - Supplier information leads with the vendor the product
 *                          was LAST bought from and marks the rest as
 *                          alternatives, states the purchase itself (date,
 *                          document, price) from the orders rather than from the
 *                          stored PriceLastPO fields a tenant may not maintain,
 *                          and no longer trails the vendor's own catalogue number
 *                          where the price belongs.
 *                        - Quality parameters and Supplier information page at
 *                          five rows.
 *   VAI163   2026-08-19  - Quality leads with the LATEST CHECK on the product —
 *                          the confirmation it was raised on, and every
 *                          parameter with what was expected against what was
 *                          found — above the plan's statement of what is
 *                          checked. The configured rows no longer state a min
 *                          and a max: those are a numeric tolerance nearly every
 *                          parameter leaves unset, so the line read
 *                          "Min 0.00 · Max 0.00" against tests that have no
 *                          numeric range. Both descriptions are shown, the test
 *                          parameter's and the plan's, since one does not answer
 *                          for the other.
 *                        - Pricing lists EVERY version of a price list the
 *                          product is priced on and says which one is in force.
 *                          It showed one version per list, so a product priced
 *                          on several versions of one list had all but one of
 *                          them missing with nothing saying so.
 *                        - A where-used BOM row states the ATTRIBUTE SET its
 *                          detail line is specified for and when that line was
 *                          added; an own BOM row states when it was created. The
 *                          section pages at five, newest first.
 *                        - Recent transactions carry the product's whole
 *                          movement history rather than the latest twenty, and a
 *                          movement with no document to name — a production or
 *                          assembly issue — reads under its movement type
 *                          instead of as a dash.
 *                        - Activity pages at five, like every other section
 *                          here, and the server de-duplicates the feed: an entry
 *                          reaching it from two sources, or two records saying
 *                          the same thing in the same second, showed up on the
 *                          panel twice.
 *                        - The image box fits the picture INSIDE it (contain)
 *                          rather than cropping it to a square, and a product
 *                          with no picture gets a 3-D box for a placeholder.
 *   VAI163   2026-08-19  - An order row carries ONE chip and it is the
 *                          fulfilment one — Due, Short received / delivered,
 *                          Fully received / delivered. The document's own status
 *                          moved to the detail line, where a drafted or voided
 *                          order still reads as one; two chips on a 440px row
 *                          left the fulfilment one fighting for the space.
 *                        - An order with something still to move states WHEN it
 *                          is due. The outstanding QUANTITY that used to sit
 *                          beside the ordered one is gone — the chip already says
 *                          the order is short, and two quantities on one line
 *                          invited being read as the same figure.
 *   VAI163   2026-08-21  Activity: a Task or Appointment row now says how many
 *                        e-mails were sent against it, and opens on click onto
 *                        each one - who it went to, its subject, when it went
 *                        and who sent it, then the message itself. The body is
 *                        shown ONLY once the row is opened.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab is sitting on a row that has not been saved yet —
    // whether it came from New Record or from Copy Record.
    //
    // The authority is the GRID TABLE's insert flag: VIS.GridTable.dataNew()
    // raises it for both actions and clears it again on save, refresh or undo.
    // GridTab does NOT expose that method — it only holds the table as
    // .gridTable — so asking the tab itself always answers "no".
    //
    // The record id cannot answer this on its own: a copied row carries the
    // SOURCE record's field values, its key included, so the id handed to the
    // panel is the product that was copied FROM.
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

    VAS.VAS_190_ProductOverviewRightPanel = function () {
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

        // The M_Product_ID the panel is showing OR loading. 0 = nothing.
        var shownRecordId = 0;

        // How long refreshPanelData holds before it actually fetches. On New
        // Record / Copy Record the framework can call refreshPanelData BEFORE
        // GridTable raises its insert flag, so asking at that instant answers
        // "no" and the panel would load the record just left. Asking again after
        // this pause gets the truth, and it collapses a burst of arrow-key row
        // changes into one request.
        var REFRESH_DELAY_MS = 150;
        // Raised by every fetch, every scheduled fetch and every clear. A reply
        // carrying a stale token belongs to a product the panel has already
        // moved off, so it is dropped instead of painting over the newer one.
        var fetchToken = 0;
        var pendingFetch = null;

        // Per-section page state, keyed by section key. Paging one section never
        // touches another, and a product change resets every one of them.
        var pages = {};
        var ROWS_PER_PAGE = 10;
        // UOM conversions page at FIVE, not ten: they sit high in the panel and a
        // product with many units pushed everything below them off the screen.
        var UOM_ROWS_PER_PAGE = 5;
        // Attributes and price lists page at five for the same reason: both sit
        // above the sections a reader scrolls for, and a set with a dozen
        // attributes — or a list carrying a price per unit and per attribute set
        // — pushed all of them off the panel.
        var ATTR_ROWS_PER_PAGE = 5;
        var PRICE_ROWS_PER_PAGE = 5;
        // Quality parameters and vendors page at five for the same reason: a plan
        // with a dozen parameters, or a product with a dozen vendors, is a wall of
        // rows between the reader and everything below it.
        var QUALITY_ROWS_PER_PAGE = 5;
        var SUPPLIER_ROWS_PER_PAGE = 5;
        // BOMs page at five as well. The section carries the product's own BOMs
        // AND every BOM detail line that consumes it, newest first, which on a
        // component used across a catalogue is a very long list.
        var BOM_ROWS_PER_PAGE = 5;
        // Activity pages at five, like every other section on this panel. It was
        // 15, and an activity feed that runs fifteen rows deep pushes the whole
        // of the panel above it out of reach on the way back up.
        var ACTIVITY_PER_PAGE = 5;

        // ----------------------------------------------------------------- //
        //  Messages                                                          //
        // ----------------------------------------------------------------- //

        // Prefer the seeded AD_Message; else a readable English default; else the
        // key. VIS.Msg answers an unseeded key with the key BRACKETED and
        // upper-cased, which is never equal to the key — so a bracketed answer is
        // treated as "not found" and the fallback below is reachable.
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

        // ----------------------------------------------------------------- //
        //  Lifecycle                                                         //
        // ----------------------------------------------------------------- //

        this.init = function () {
            $root = $('<div class="vas_190-root"></div>');
            $body = $('<div class="vas_190-body"></div>');
            $emptyState = $('<div class="vas_190-empty" style="display:none;"></div>');
            $emptyState.text(msg("VAS_190_NoData", "No product selected"));
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

        // The candidate window names a row carries, if any.
        function openWindowNames($el) {
            var raw = $el.attr("data-open-windows");
            return raw ? String(raw).split(",") : null;
        }

        // Delegated once on the root so it survives every re-render: a document
        // row opens the record it points at, a mail card toggles its body.
        function bindEvents() {
            $root.on("click", "[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"),
                           $(this).attr("data-open-id"),
                           $(this).attr("data-open-sotrx") === "Y",
                           openWindowNames($(this)));
            });
            // Those rows are buttons, so they have to answer the keyboard the
            // way a button does.
            $root.on("keydown", "[data-open-table]", function (e) {
                if (e.which !== 13 && e.which !== 32) return;
                e.preventDefault();
                openRecord($(this).attr("data-open-table"),
                           $(this).attr("data-open-id"),
                           $(this).attr("data-open-sotrx") === "Y",
                           openWindowNames($(this)));
            });

            $root.on("click", ".vas_190-actRow.vas_190-is-openable", function () {
                toggleMail($(this));
            });
            // Mail expansion is keyboard-operable: Enter and Space both toggle.
            $root.on("keydown", ".vas_190-actRow.vas_190-is-openable", function (e) {
                if (e.which === 13 || e.which === 32) {
                    e.preventDefault();
                    toggleMail($(this));
                }
            });
        }

        // The message body is the row's own next sibling, the way VAS_092 folds
        // it: the row states the state, the panel beneath it holds the mail.
        function toggleMail($row) {
            // Two kinds of row open: a mail onto its own message, a task or
            // appointment onto the e-mails sent against it. The row says which,
            // so the hint names the right thing.
            var isAppt = ($row.attr("data-openkind") === "appt");
            var nowOpen = !$row.hasClass("vas_190-is-open");
            var hint = nowOpen
                ? (isAppt ? msg("VAS_190_HideMails", "Hide e-mails")
                          : msg("VAS_190_HideMail", "Hide full mail"))
                : (isAppt ? msg("VAS_190_ShowMails", "Show e-mails")
                          : msg("VAS_190_ShowMail", "Show full mail"));
            $row.toggleClass("vas_190-is-open", nowOpen)
                .attr("aria-expanded", nowOpen ? "true" : "false")
                .attr("title", hint);
            $row.next(".vas_190-actBody").toggle(nowOpen);
        }

        // Opens a record's window filtered to that row through the platform's
        // zoom API. Never a full-page navigation — that crashes the host from
        // inside a panel. Degrades silently so a click can never throw.
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
                    "VAS_190_ProductOverviewRightPanel/GetWindowId", windowName);
                id = parseInt(id, 10);
                if (isNaN(id) || id <= 0) { windowIdByName[windowName] = -1; return 0; }
                windowIdByName[windowName] = id;
                return id;
            } catch (e) {
                windowIdByName[windowName] = -1;
                return 0;
            }
        }

        // Windows a row may ask for BY NAME, because its table's zoom target opens
        // the wrong screen. C_BPartner is the case that matters here: its zoom
        // target is the customer master, so a click on a SUPPLIER opened the
        // customer window with a vendor's id in it.
        //
        // Several names are tried in order because the dictionary's own naming is
        // the tenant's, not ours — the first that resolves wins, and when none
        // does the click falls back to the zoom target exactly as before. Nothing
        // is hard-failed on a name we cannot confirm.
        var VENDOR_WINDOW_NAMES = ["VAS_Vendor", "VAS_VendorMaster", "VAS_BusinessPartnerVendor"];

        function resolveFirstWindowId(names) {
            if (!names) return 0;
            for (var i = 0; i < names.length; i++) {
                var id = resolveWindowIdByName(names[i]);
                if (id > 0) return id;
            }
            return 0;
        }

        // Opens a record's window filtered to that row through the platform's
        // zoom API. Never a full-page navigation — that crashes the host from
        // inside a panel. Degrades silently so a click can never throw.
        function openRecord(tableName, recordId, isSOTrx, windowNames) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                // A window named on the ROW wins: it is the only thing that can
                // tell two records of the same table apart, which is exactly the
                // customer-versus-vendor case.
                var windowId = resolveFirstWindowId(windowNames);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th argument picks the sales vs purchase window for a
                    // dual-purpose table like C_Order.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                }
            } catch (e) { console.log(e); }
        }

        // ----------------------------------------------------------------- //
        //  Request lifecycle                                                 //
        // ----------------------------------------------------------------- //

        // Drops whatever the panel was loading: cancels a fetch still waiting on
        // its delay and invalidates the token of one already on the wire, so
        // neither can paint over what the caller is about to put on screen.
        function invalidateFetch() {
            fetchToken++;
            if (pendingFetch) {
                clearTimeout(pendingFetch);
                pendingFetch = null;
            }
        }

        this.abortPendingFetch = invalidateFetch;

        this.scheduleFetch = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            // Claimed now, not when the timer fires: shownRecordId means "showing
            // or loading", and leaving it stale through the wait would let the
            // data-status listener fire a second fetch for the same row.
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
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_190_ProductOverviewRightPanel/GetProductOverview",
                type: "GET",
                dataType: "json",
                data: { M_Product_ID: recordID },
                success: function (raw) {
                    // Reply for a product the panel has already left. Whoever
                    // superseded us owns the busy indicator now.
                    if (token !== fetchToken) return;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    pages = {};                 // every section back to page 1
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    if (token !== fetchToken) return;
                    console.log(err);
                    showBusy(false);
                }
            });
        };

        this.clear = function () {
            invalidateFetch();
            data = null;
            shownRecordId = 0;
            pages = {};
            render();
            // A discarded reply never reaches its own showBusy(false), so the
            // spinner would otherwise sit on the empty panel for good.
            showBusy(false);
        };

        // The framework notifies a tab panel when the selected record changes
        // but NOT when the user starts a new one: GridController.dataNew() never
        // reaches the tab panel. Listening to the tab's own data-status events
        // closes that gap.
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
            if (rid !== shownRecordId) {
                $self.record_ID = rid;
                $self.fetchData(rid);
            }
        }

        this.tabDataListener = { dataStatusChanged: function (e) { onTabDataStatus(e); } };

        // The platform Refresh button calls this. Without it the button silently
        // does nothing, so it is exposed as an instance method here and as a
        // prototype method below.
        this.refreshWidget = function () {
            if ($self.record_ID > 0) {
                $self.fetchData($self.record_ID);
            } else {
                $self.clear();
            }
        };

        // ----------------------------------------------------------------- //
        //  Section registry                                                  //
        // ----------------------------------------------------------------- //

        function isItem() { return !!(data && data.Product && data.Product.ProductType === "I"); }
        function any(list) { return !!(list && list.length); }

        // key | when to draw it | what draws it. Rendering iterates this in
        // order; nothing else decides which sections exist or where they sit.
        var SECTIONS = [
            { key: "summary",     condition: function () { return !!data.Product; },        render: renderSummary },
            { key: "attributes",  condition: function () { return any(data.Attributes); },  render: renderAttributes },
            { key: "tax",         condition: function () { return !!data.Tax; },            render: renderTax },
            { key: "stock",       condition: function () { return isItem() && !!data.StockSummary; }, render: renderStockSummary },
            { key: "stockrows",   condition: function () { return isItem() && any(data.StockDetails); }, render: renderStockDetails },
            { key: "uom",         condition: function () { return any(data.UomConversions); }, render: renderUomConversions },
            { key: "pricing",     condition: function () { return any(data.Pricing); },     render: renderPricing },
            { key: "bom",         condition: function () { return isItem() && any(data.Manufacturing); }, render: renderManufacturing },
            { key: "quality",     condition: function () { return isItem() && hasQuality(); }, render: renderQuality },
            { key: "suppliers",   condition: function () { return any(data.Suppliers); },   render: renderSuppliers },
            { key: "so",          condition: function () { return any(data.SalesOrders); }, render: renderSalesOrders },
            { key: "po",          condition: function () { return any(data.PurchaseOrders); }, render: renderPurchaseOrders },
            { key: "tx",          condition: function () { return isItem() && any(data.Transactions); }, render: renderTransactions },
            { key: "accounting",  condition: function () { return !!(data.Accounting && any(data.Accounting.Rows)); }, render: renderAccounting },
            // Activity is the one section that renders empty — it reports the
            // absence of events rather than hiding the fact that there are none.
            { key: "activity",    condition: function () { return true; },                  render: renderActivity }
        ];

        function render() {
            if (!$body) return;    // the host can hand us a record before init()

            $body.empty();

            if (!data || !data.Product || !data.Product.M_Product_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Each section is drawn behind its own guard: one that throws costs
            // only itself, never the sections below it.
            for (var i = 0; i < SECTIONS.length; i++) {
                var sec = SECTIONS[i];
                try {
                    if (sec.condition()) sec.render();
                } catch (e) {
                    try { console.log("VAS_190 section '" + sec.key + "' failed to render:", e); } catch (e2) { }
                }
            }

            // A different product starts at the top of the panel.
            try { $body[0].scrollTop = 0; } catch (e3) { }
        }

        // ----------------------------------------------------------------- //
        //  Primitives                                                        //
        // ----------------------------------------------------------------- //

        // A headered section: title left, optional muted summary right. Returns
        // the section element so the caller can append its content.
        function section(title, summary) {
            var $sec = $('<section class="vas_190-sec"></section>');
            var $head = $('<div class="vas_190-secHead"></div>');
            $head.append($('<span class="vas_190-secTitle"></span>').text(title));
            if (summary) {
                $head.append($('<span class="vas_190-secSum"></span>').text(summary).attr("title", summary));
            }
            $sec.append($head);
            $body.append($sec);
            return $sec;
        }

        function chip(text, tone) {
            return $('<span class="vas_190-chip"></span>')
                .addClass("vas_190-tone-" + (tone || "neutral"))
                .text(text);
        }

        // A labelled metric cell: label, value, optional caption. Everything
        // clips to one line, so the untruncated text goes on the cell's tooltip.
        // A caption that carries several facts rather than one phrase — the open
        // orders behind a figure, with that order's dates — asks to WRAP instead:
        // clipped, all but the first fact would only exist on the tooltip.
        function metricCell(label, value, meta, wrapMeta) {
            var $c = $('<div class="vas_190-mcell"></div>');
            $c.append($('<div class="vas_190-mLabel"></div>').text(label));
            $c.append($('<div class="vas_190-mVal"></div>').text(value).attr("title", value));
            if (meta) {
                var $m = $('<div class="vas_190-mMeta"></div>').text(meta).attr("title", meta);
                if (wrapMeta) $m.addClass("vas_190-mMetaWrap");
                $c.append($m);
            }
            return $c;
        }

        // A compact-list row: primary + meta on the left, optional chip and
        // trailing value on the right.
        function listRow(opts) {
            var $row = $('<div class="vas_190-clRow"></div>');

            var $lhs = $('<div class="vas_190-clLhs"></div>');
            var $p = $('<div class="vas_190-clP"></div>');
            $p.append($('<span></span>').text(opts.primary));
            if (opts.primarySoft) {
                $p.append($('<span class="vas_190-soft"></span>').text(" · " + opts.primarySoft));
            }
            $p.attr("title", opts.primary + (opts.primarySoft ? " · " + opts.primarySoft : ""));
            $lhs.append($p);
            if (opts.meta) {
                $lhs.append($('<div class="vas_190-clM"></div>').text(opts.meta).attr("title", opts.meta));
            }
            $row.append($lhs);

            var $rhs = $('<div class="vas_190-clRhs"></div>');
            // One chip or several: an order row states both its document status
            // and how much of it has actually moved, and neither answers for the
            // other.
            if (opts.chip) $rhs.append(chip(opts.chip.text, opts.chip.tone));
            if (opts.chips) {
                for (var c = 0; c < opts.chips.length; c++) {
                    if (opts.chips[c]) $rhs.append(chip(opts.chips[c].text, opts.chips[c].tone));
                }
            }
            if (opts.value) {
                var $v = $('<span class="vas_190-clVal"></span>').text(opts.value);
                if (opts.valueSub) {
                    $v.append($('<span class="vas_190-clSub"></span>').text(opts.valueSub));
                }
                $rhs.append($v);
            }
            $row.append($rhs);

            if (opts.openTable && opts.openId > 0) {
                // A whole row that navigates is a button, not a styled div.
                $row.attr("role", "button").attr("tabindex", "0")
                    .addClass("vas_190-clickable")
                    .attr("data-open-table", opts.openTable)
                    .attr("data-open-id", opts.openId);
                if (opts.openSOTrx) $row.attr("data-open-sotrx", "Y");
                // Candidate window names, tried in order before the zoom target.
                if (opts.openWindows) {
                    $row.attr("data-open-windows", opts.openWindows.join(","));
                }
            }
            return $row;
        }

        // Paginates a list of rows into a section: draws one page and, when there
        // is more than one, a pager beneath it. Page state lives in `pages` under
        // the section key, so each section pages independently.
        //
        // `$host` is the container the rows themselves go in — the section's grid,
        // its compact list or its timeline, whichever owns the row markup. The
        // paginator used to supply its own host, which the caller then emptied
        // into the grid and DELETED; every repaint after the first drew into that
        // deleted node, so the pager moved through the pages while the rows on
        // screen never changed.
        //
        // Only the rows this pager painted are cleared on a repaint, never the
        // whole container: a grid's header and a timeline's rail are siblings of
        // those rows and must survive it.
        function paginate($sec, key, rows, perPage, buildRow, $host) {
            var $pager = $('<div class="vas_190-pager"></div>');
            var painted = [];

            function paint() {
                var pageCount = Math.max(1, Math.ceil(rows.length / perPage));
                var page = pages[key] || 0;
                if (page >= pageCount) page = pageCount - 1;
                if (page < 0) page = 0;
                pages[key] = page;

                var start = page * perPage;
                var end = Math.min(rows.length, start + perPage);

                for (var d = 0; d < painted.length; d++) painted[d].remove();
                painted = [];
                for (var i = start; i < end; i++) {
                    var $row = buildRow(rows[i], i);
                    $host.append($row);
                    painted.push($row);
                }

                $pager.detach().empty();
                if (pageCount > 1) {
                    $pager.append(pagerButton("prev", page <= 0, function () {
                        pages[key] = page - 1; paint();
                    }));
                    $pager.append($('<span class="vas_190-pgText"></span>').text(
                        (page + 1) + " " + msg("VAS_190_Of", "of") + " " + pageCount));
                    $pager.append(pagerButton("next", page >= pageCount - 1, function () {
                        pages[key] = page + 1; paint();
                    }));
                    $sec.append($pager);
                }
            }
            paint();
        }

        function pagerButton(dir, disabled, handler) {
            var $b = $('<button type="button" class="vas_190-pgBtn"></button>')
                .attr("aria-label", dir === "prev"
                    ? msg("VAS_190_Previous", "Previous page")
                    : msg("VAS_190_Next", "Next page"));
            $b.append(svgIcon(dir === "prev" ? "chevLeft" : "chevRight"));
            if (disabled) $b.prop("disabled", true);
            else $b.on("click", handler);
            return $b;
        }

        // A data grid: a fixed leading icon column then the named columns. `cols`
        // carries {label, align} per column; the icon column has no header.
        function dataGrid(modifier, cols) {
            var $g = $('<div class="vas_190-grid"></div>').addClass("vas_190-" + modifier);
            var $head = $('<div class="vas_190-gHead"></div>');
            $head.append($('<span></span>'));
            for (var i = 0; i < cols.length; i++) {
                var $c = $('<span></span>').text(cols[i].label);
                if (cols[i].align === "r") $c.addClass("vas_190-num");
                $head.append($c);
            }
            $g.append($head);
            return $g;
        }

        function gridCell(text, align, bold) {
            var $c = $('<span></span>').text(text).attr("title", text);
            if (align === "r") $c.addClass("vas_190-num");
            if (bold) $c.addClass("vas_190-gId");
            return $c;
        }

        // ----------------------------------------------------------------- //
        //  1. Product summary (hero)                                         //
        // ----------------------------------------------------------------- //

        var STATUS_META = {
            "ACTIVE":       { tone: "info", key: "VAS_190_Active",       text: "Active",       hero: "" },
            "INACTIVE":     { tone: "crit", key: "VAS_190_Inactive",     text: "Inactive",     hero: "vas_190-tone-risk" },
            "DISCONTINUED": { tone: "warn", key: "VAS_190_Discontinued", text: "Discontinued", hero: "vas_190-tone-warn" }
        };

        var TYPE_META = {
            "I": { key: "VAS_190_TypeItem",     text: "Item" },
            "S": { key: "VAS_190_TypeService",  text: "Service" },
            "R": { key: "VAS_190_TypeResource", text: "Resource" },
            "E": { key: "VAS_190_TypeExpense",  text: "Expense" }
        };

        function productTypeLabel(code) {
            var m = TYPE_META[code];
            return m ? msg(m.key, m.text) : (code || "");
        }

        // The server returns whichever of three forms the image actually exists
        // in: an absolute URL (hosted elsewhere), a data: URI (bytes held in the
        // database), or a path relative to the application root ("Images/…").
        // Only the last needs the context prefix, and only the client knows it.
        function resolveImageSrc(stored) {
            var url = (stored || "").trim();
            if (!url) return "";
            if (/^(https?:)?\/\//i.test(url) || /^data:/i.test(url)) return url;
            var base = "";
            try { base = VIS.Application.contextUrl || ""; } catch (e) { base = ""; }
            // Never build a double slash — the context url may or may not end in one.
            if (base && base.charAt(base.length - 1) !== "/") base += "/";
            return base + url.replace(/^\//, "");
        }

        function renderSummary() {
            var p = data.Product;
            var st = STATUS_META[p.StatusCode] || STATUS_META["ACTIVE"];

            var $sec = $('<section class="vas_190-sec"></section>');
            var $hero = $('<div class="vas_190-hero"></div>').addClass(st.hero);

            var $top = $('<div class="vas_190-heroTop"></div>');

            // Image box. The server hands back either an absolute URL, a data:
            // URI, or a path relative to the application root — the last needs
            // the context prefix, which only the client knows.
            var $img = $('<div class="vas_190-imgBox"></div>').attr("title", p.Name || "");
            var imageSrc = resolveImageSrc(p.ImageUrl);
            if (imageSrc) {
                // alt is empty on purpose: the product name is already the
                // heading beside it, so announcing it twice adds nothing.
                var $tag = $('<img>').attr("alt", "").attr("src", imageSrc);
                // A file that has gone missing under the server's Images folder
                // falls back to the placeholder rather than a broken-image glyph.
                $tag.on("error", function () {
                    $img.empty().addClass("vas_190-imgEmpty").append(svgIcon("box3d"));
                });
                $img.append($tag);
            } else {
                // A product with no picture gets a 3-D box rather than a picture
                // frame: the placeholder stands for the ITEM, which is what the
                // reader is looking at, not for a missing photograph.
                $img.addClass("vas_190-imgEmpty").append(svgIcon("box3d"));
            }
            $top.append($img);

            var $id = $('<div class="vas_190-heroId"></div>');
            $id.append($('<div class="vas_190-heroName"></div>').text(p.Name || "").attr("title", p.Name || ""));
            // Subtitle: product code · category.
            var subBits = [];
            if (p.Code) subBits.push(p.Code);
            if (p.CategoryName) subBits.push(p.CategoryName);
            if (subBits.length) {
                var sub = subBits.join(" · ");
                $id.append($('<div class="vas_190-heroSub"></div>').text(sub).attr("title", sub));
            }
            $top.append($id);

            $top.append(chip(msg(st.key, st.text), st.tone).addClass("vas_190-onTint"));
            $hero.append($top);

            // The status line only appears for a state that needs explaining.
            if (p.StatusCode === "INACTIVE") {
                $hero.append($('<div class="vas_190-statusLine vas_190-lineRisk"></div>')
                    .text(msg("VAS_190_InactiveNote",
                              "Inactive — not selectable on new transactions")));
            } else if (p.StatusCode === "DISCONTINUED") {
                // The date is only shown when the schema actually carries one;
                // no discontinued-date field is invented to fill the sentence.
                var when = formatDate(p.DiscontinuedFrom);
                var line = when
                    ? msg("VAS_190_DiscontinuedFrom", "Discontinued from") + " " + when
                    : msg("VAS_190_Discontinued", "Discontinued");

                // "existing stock can still be sold" is a statement about STOCK, so
                // it is only true of a product that HAS any — an Item (ProductType
                // 'I'). A service, an expense type or a resource holds no stock to
                // sell down, and telling the reader otherwise describes inventory
                // that cannot exist. Those types get the bare discontinued note.
                if (p.ProductType === "I") {
                    line += " — " + msg("VAS_190_StockSellable",
                                        "existing stock can still be sold");
                }
                $hero.append($('<div class="vas_190-statusLine vas_190-lineWarn"></div>')
                    .text(line));
            }

            // Metric grid: four cells for an Item, exactly three for every other
            // supported type — Barcode is not shown in the reduced summary.
            var $grid = $('<div class="vas_190-heroGrid"></div>');
            $grid.append(metricCell(msg("VAS_190_SKU", "SKU"), p.SKU || "—"));
            if (p.ProductType === "I") {
                $grid.append(metricCell(msg("VAS_190_Barcode", "Barcode"), p.Barcode || "—"));
            }
            $grid.append(metricCell(msg("VAS_190_BaseUOM", "Base UOM"), p.BaseUomName || "—"));
            $grid.append(metricCell(msg("VAS_190_ProductType", "Product type"),
                                    productTypeLabel(p.ProductType)));
            $hero.append($grid);

            $sec.append($hero);
            $body.append($sec);
        }

        // ----------------------------------------------------------------- //
        //  2. Attributes                                                     //
        // ----------------------------------------------------------------- //

        var ATTR_CONTROL = {
            "LOT":           { key: "VAS_190_LotControl",     text: "Lot control" },
            "SERNO":         { key: "VAS_190_SerialControl",  text: "Serial number" },
            "GUARANTEEDATE": { key: "VAS_190_ExpiryControl",  text: "Expiry / guarantee date" }
        };

        var ATTR_CHIP = {
            "MANDATORY": { key: "VAS_190_ChipMandatory", text: "Mandatory", tone: "warn" },
            "OPTIONAL":  { key: "VAS_190_ChipOptional",  text: "Optional",  tone: "neutral" }
        };

        // M_AttributeSet.MandatoryType — WHEN the set has to be answered, which
        // is a different question from whether any single control is mandatory.
        var ATTR_SET_MANDATORY = {
            "N": { key: "VAS_190_SetNotMandatory",    text: "Not mandatory" },
            "A": { key: "VAS_190_SetAlwaysMandatory", text: "Always mandatory" },
            "S": { key: "VAS_190_SetShippingMandatory", text: "Mandatory when shipping" }
        };

        // M_Attribute.AttributeValueType. An unmapped code is shown as it is
        // stored rather than guessed at.
        var ATTR_VALUE_TYPE = {
            "L": { key: "VAS_190_AttrTypeList",   text: "List" },
            "N": { key: "VAS_190_AttrTypeNumber", text: "Number" },
            "S": { key: "VAS_190_AttrTypeText",   text: "Text" }
        };

        function renderAttributes() {
            var rows = data.Attributes;

            // The set's name AND when it has to be answered: "Laptop
            // Configuration" alone never said whether a transaction can be
            // saved without it.
            var summaryBits = [];
            if (rows[0].AttributeSetName) summaryBits.push(rows[0].AttributeSetName);
            var setMand = ATTR_SET_MANDATORY[rows[0].SetMandatoryType];
            if (setMand) summaryBits.push(msg(setMand.key, setMand.text));
            else if (rows[0].SetMandatoryType) summaryBits.push(rows[0].SetMandatoryType);

            var $sec = section(msg("VAS_190_Attributes", "Attributes"),
                               summaryBits.join(" · "));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            // Five to a page. A set with a dozen attributes pushed every section
            // below it off the panel.
            paginate($sec, "attributes", rows, ATTR_ROWS_PER_PAGE, function (a) {
                var control = ATTR_CONTROL[a.Name];
                var name = control ? msg(control.key, control.text) : (a.Name || "");
                var chipMeta = ATTR_CHIP[a.ChipKey] || ATTR_CHIP["OPTIONAL"];

                // The meta line only states what the record actually says: the
                // control record a lot or serial control names, the shelf life
                // behind an expiry control, an attribute's value type and — only
                // where the attribute really is one — that it is captured per
                // instance. Nothing is inferred to fill the line.
                var bits = [];
                if (a.Kind === "control") {
                    if (a.ControlName) bits.push(a.ControlName);
                    if (a.Name === "GUARANTEEDATE" && a.GuaranteeDays > 0) {
                        bits.push(msg("VAS_190_ShelfLife", "Shelf life") + " " +
                                  a.GuaranteeDays + " " + msg("VAS_190_Days", "days"));
                    }
                } else {
                    var vt = ATTR_VALUE_TYPE[a.ValueType];
                    if (vt) bits.push(msg(vt.key, vt.text));
                    else if (a.ValueType) bits.push(a.ValueType);
                    // The set's own flag only says that SOME attribute on it is an
                    // instance attribute, so this is read per attribute — an
                    // attribute whose box is clear is not one and does not say so.
                    if (a.IsInstanceAttribute) {
                        bits.push(msg("VAS_190_InstanceAttribute", "Instance attribute"));
                    }
                    if (a.ValueCount > 0) {
                        bits.push(a.ValueCount + " " + msg("VAS_190_Values", "values"));
                    }
                }

                return listRow({
                    primary: name,
                    meta: bits.join(" · "),
                    chip: { text: msg(chipMeta.key, chipMeta.text), tone: chipMeta.tone }
                });
            }, $list);
        }

        // ----------------------------------------------------------------- //
        //  3. Tax information                                                //
        // ----------------------------------------------------------------- //

        function renderTax() {
            var t = data.Tax;
            var $sec = section(msg("VAS_190_TaxInformation", "Tax information"), "");

            var $card = $('<div class="vas_190-detailCard"></div>');
            $card.append(metricCell(msg("VAS_190_TaxCategory", "Tax category"),
                                    t.TaxCategoryName || "—"));
            $card.append(metricCell(msg("VAS_190_HsnSac", "HSN / SAC"),
                                    t.HsnSacCode || "—",
                                    t.HsnSacCode ? msg("VAS_190_EInvoiceReady", "e-invoice ready") : ""));
            $sec.append($card);
        }

        // ----------------------------------------------------------------- //
        //  4. Stock and availability (Item only)                             //
        // ----------------------------------------------------------------- //

        function renderStockSummary() {
            var s = data.StockSummary;
            var uom = data.Product.BaseUomName || "";
            var prec = +data.Product.UomPrecision || 0;

            var summary = s.WarehouseCount + " " + msg("VAS_190_Warehouses", "warehouses") +
                          " · " + s.LocatorCount + " " + msg("VAS_190_Locators", "locators");
            var $sec = section(msg("VAS_190_StockAvailability", "Stock & availability"), summary);

            var $card = $('<div class="vas_190-detailCard"></div>');
            $card.append(metricCell(msg("VAS_190_OnHand", "On hand"),
                qtyText(s.OnHandQty, prec, uom),
                msg("VAS_190_AllWarehouses", "all warehouses")));
            $card.append(metricCell(msg("VAS_190_Reserved", "Reserved"),
                qtyText(s.ReservedQty, prec, uom),
                openOrderCaption(s.ReservedOrderCount, true,
                                 s.ReservedDateOrdered, s.ReservedDatePromised), true));
            $card.append(metricCell(msg("VAS_190_OnOrder", "On order"),
                qtyText(s.OnOrderQty, prec, uom),
                openOrderCaption(s.OnOrderCount, false,
                                 s.OnOrderDateOrdered, s.OnOrderDatePromised), true));
            $card.append(metricCell(msg("VAS_190_AvailableToPromise", "Available to promise"),
                qtyText(s.AvailableToPromise, prec, uom),
                msg("VAS_190_AtpFormula", "on hand − reserved")));
            $sec.append($card);
        }

        // What the figure above it came from: how many OPEN orders — not merely
        // completed ones, which said nothing about whether anything is still to
        // move — and, where it is a single order, when it was raised and when it
        // is due. With several orders in play neither date describes the figure,
        // so neither is shown.
        function openOrderCaption(count, isSales, dateOrdered, datePromised) {
            var n = +count || 0;
            var noun = isSales
                ? (n === 1 ? msg("VAS_190_OpenSalesOrder", "open sales order")
                           : msg("VAS_190_OpenSalesOrders", "open sales orders"))
                : (n === 1 ? msg("VAS_190_OpenPurchaseOrder", "open purchase order")
                           : msg("VAS_190_OpenPurchaseOrders", "open purchase orders"));

            var bits = [n + " " + noun];
            if (n === 1) {
                var ordered = formatDate(dateOrdered);
                var due     = formatDate(datePromised);
                if (ordered) bits.push(msg("VAS_190_Dated", "dated") + " " + ordered);
                if (due)     bits.push(msg("VAS_190_Due", "due") + " " + due);
            }
            return bits.join(" · ");
        }

        function qtyText(value, precision, uom) {
            var n = formatNumber(+value || 0, precision);
            return uom ? n + " " + uom : n;
        }

        // ----------------------------------------------------------------- //
        //  5. Stock details (Item only)                                      //
        // ----------------------------------------------------------------- //

        function renderStockDetails() {
            var rows = data.StockDetails;
            var prec = +data.Product.UomPrecision || 0;
            // M_Storage holds the on-hand in the product's BASE uom, so every
            // figure in this grid is in that unit — it is named on each row
            // rather than left to be inferred from the Stock summary above.
            var uom = data.Product.BaseUomName || "";
            var $sec = section(msg("VAS_190_StockDetails", "Stock details"),
                               rows.length + " " + msg("VAS_190_Lines", "lines"));

            var $grid = dataGrid("colsStock", [
                { label: msg("VAS_190_Warehouse", "Warehouse") },
                { label: msg("VAS_190_Locator", "Locator") },
                { label: msg("VAS_190_AttributesCol", "Attributes") },
                { label: msg("VAS_190_OnHand", "On hand"), align: "r" }
            ]);
            $sec.append($grid);

            // Warehouses take alternating icon tones so rows group visually
            // without needing a repeated warehouse name to read them.
            var tones = {}, toneOrder = ["info", "warn", "ok", "purple"], toneNext = 0;

            // The rows are painted straight into the grid — they are its children,
            // and the pager repaints them there on every page change.
            paginate($sec, "stockrows", rows, ROWS_PER_PAGE, function (r) {
                if (tones[r.M_Warehouse_ID] === undefined) {
                    tones[r.M_Warehouse_ID] = toneOrder[toneNext % toneOrder.length];
                    toneNext++;
                }
                var $row = $('<div class="vas_190-gRow"></div>');
                $row.append($('<span class="vas_190-gIcon"></span>')
                    .addClass("vas_190-ic-" + tones[r.M_Warehouse_ID])
                    .attr("title", r.WarehouseName || "")
                    .append(svgIcon("warehouse")));
                $row.append(gridCell(r.WarehouseName || "—", null, true));
                $row.append(gridCell(r.LocatorName || "—"));
                $row.append(gridCell(r.Attributes || "—"));
                $row.append(gridCell(qtyText(r.QtyOnHand, prec, uom), "r"));
                return $row;
            }, $grid);
        }

        // ----------------------------------------------------------------- //
        //  6. UOM conversions                                                //
        // ----------------------------------------------------------------- //

        function renderUomConversions() {
            var rows = data.UomConversions;
            var base = data.Product.BaseUomName || "";
            var $sec = section(msg("VAS_190_UomConversions", "UOM conversions"),
                               base ? msg("VAS_190_Base", "base") + ": " + base : "");

            // Paged at 5. A product with a dozen conversions pushed every section
            // below it off the panel, and the rows past the first few are reference
            // detail — reachable, not worth the height. The rows live in the same
            // compact list every other section uses, and the pager repaints them
            // inside it.
            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            paginate($sec, "uomrows", rows, UOM_ROWS_PER_PAGE, function (c) {
                // "= rate BaseUom" is assembled here, never in SQL.
                var value = "= " + formatNumber(+c.RateToBase || 0, rateDigits(c.RateToBase)) +
                            (base ? " " + base : "");
                return listRow({
                    primary: c.UomName || "—",
                    meta: uomConversionMeta(c),
                    value: value
                });
            }, $list);
        }

        // The units a product's documents DEFAULT to, in the order they are read.
        // Each is a column on M_Product (VAS_PurchaseUOM_ID, VAS_SalesUOM_ID,
        // VAS_ConsumableUOM_ID); a unit that answers none of them is only a
        // conversion the product happens to define.
        var UOM_ROLES = [
            { flag: "IsPurchaseUom",   key: "VAS_190_PurchaseUom",   text: "Default purchase UOM" },
            { flag: "IsSalesUom",      key: "VAS_190_SalesUom",      text: "Default sales UOM" },
            { flag: "IsConsumableUom", key: "VAS_190_ConsumableUom", text: "Default consumable UOM" }
        ];

        // What a conversion row says about ITSELF, beneath the unit's name.
        //
        // It used to read "Product-specific conversion" / "Generic UOM conversion"
        // and nothing else — so on a product whose purchase, sales or consumable
        // unit differs from the base one, the row a reader was looking for was
        // indistinguishable from every other row. The unit's ROLE on this product
        // leads the line now, and how the conversion is DEFINED follows it: a unit
        // that is none of the three defaults is only a conversion the product (or
        // the dictionary) happens to define.
        function uomConversionMeta(c) {
            var bits = [];
            for (var i = 0; i < UOM_ROLES.length; i++) {
                if (c[UOM_ROLES[i].flag]) bits.push(msg(UOM_ROLES[i].key, UOM_ROLES[i].text));
            }
            bits.push(c.IsProductSpecific
                ? msg("VAS_190_ProductConversion", "Product-specific conversion")
                : msg("VAS_190_GenericConversion", "Generic UOM conversion"));
            return bits.join(" · ");
        }

        // A conversion rate is meaningless rounded to the currency precision: 12
        // to a box is exact, 0.0833 the other way. Show up to four decimals and
        // drop the trailing zeros.
        function rateDigits(rate) {
            var r = Math.abs(+rate || 0);
            return (r === Math.floor(r)) ? 0 : 4;
        }

        // ----------------------------------------------------------------- //
        //  7. Pricing                                                        //
        // ----------------------------------------------------------------- //

        function renderPricing() {
            var rows = data.Pricing;
            // The count is of PRICE LISTS, not of rows: a list carrying several
            // versions, and a price per unit and per attribute set within each,
            // contributes many rows, and "9 price lists" for four of them was
            // simply wrong.
            var listIds = [];
            for (var n = 0; n < rows.length; n++) {
                var id = rows[n].M_PriceList_ID;
                if (listIds.indexOf(id) < 0) listIds.push(id);
            }
            var $sec = section(msg("VAS_190_Pricing", "Pricing"),
                listIds.length + " " + (listIds.length === 1
                    ? msg("VAS_190_PriceList", "price list")
                    : msg("VAS_190_PriceLists", "price lists")));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            paginate($sec, "pricing", rows, PRICE_ROWS_PER_PAGE, function (p) {
                var sym = p.CurSymbol || p.ISO_Code || "";
                // A price list holds SEVERAL prices for one product — one per unit
                // and per attribute set instance. Those two are what tells its
                // rows apart, so they ride on the row's OWN line beside the list's
                // name; without them the rows read as one price list repeated with
                // different figures. The version name is the same name again in
                // nearly every tenant, so it goes last, where clipping costs least.
                var idBits = [];
                // A price row that names no unit of its own is stated in the
                // product's base unit, which is what the documents will use.
                var priceUom = p.UomName || data.Product.BaseUomName || "";
                if (priceUom)     idBits.push(msg("VAS_190_Per", "per") + " " + priceUom);
                if (p.Attributes) idBits.push(p.Attributes);

                var metaBits = [];
                var eff = formatDate(p.ValidFrom);
                if (eff) metaBits.push(msg("VAS_190_Effective", "effective") + " " + eff);
                metaBits.push(msg("VAS_190_ListPrice", "list") + " " +
                              formatAmount(p.PriceList, sym, p.CurPrecision));
                metaBits.push(msg("VAS_190_LimitPrice", "limit") + " " +
                              formatAmount(p.PriceLimit, sym, p.CurPrecision));
                if (p.VersionName) metaBits.push(p.VersionName);

                return listRow({
                    primary: p.PriceListName || "—",
                    primarySoft: idBits.join(" · "),
                    meta: metaBits.join(" · "),
                    // Every version the product is priced on is listed — a list
                    // can carry several and the section used to show one of them
                    // per list — so the row has to say which one is actually in
                    // force today. The rest are history or not yet effective.
                    chip: p.IsCurrentVersion
                        ? { text: msg("VAS_190_CurrentVersion", "Current"), tone: "ok" }
                        : { text: msg("VAS_190_OtherVersion", "Other version"), tone: "neutral" },
                    value: formatAmount(p.PriceStd, sym, p.CurPrecision),
                    valueSub: msg("VAS_190_StdPrice", "std price")
                });
            }, $list);
        }

        // ----------------------------------------------------------------- //
        //  8. Manufacturing - BOMs (Item only)                               //
        // ----------------------------------------------------------------- //

        function renderManufacturing() {
            var rows = data.Manufacturing;
            var own = 0, usedIn = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].Kind === "own") own++; else usedIn++;
            }
            var $sec = section(msg("VAS_190_Manufacturing", "Manufacturing — BOMs"),
                own + " " + msg("VAS_190_Own", "own") + " · " +
                msg("VAS_190_UsedInCount", "used in") + " " + usedIn);

            var $list = $('<div class="vas_190-elist"></div>');
            $sec.append($list);

            // Five to a page, newest first — the server orders both sources
            // together on when each record was created, so page one is what
            // changed most recently rather than whichever source was read first.
            paginate($sec, "bom", rows, BOM_ROWS_PER_PAGE, buildBomRow, $list);
        }

        function buildBomRow(b) {
            var $row = $('<div class="vas_190-eRow"></div>');
            $row.append($('<div class="vas_190-tile"></div>').append(svgIcon("bom")));

            var $id = $('<div class="vas_190-eId"></div>');
            var $titleRow = $('<div class="vas_190-eTitleRow"></div>');
            $titleRow.append($('<span class="vas_190-eP"></span>')
                .text(b.Name || "—").attr("title", b.Name || ""));
            if (b.Kind === "usedin") {
                $titleRow.append(chip(msg("VAS_190_UsedIn", "Used in"), "purple"));
            }
            $id.append($titleRow);

            // No default flag, no version, no version date — those fields are
            // not read at all.
            var metaBits = [];
            if (b.Kind === "own") {
                metaBits.push(b.ComponentCount + " " + msg("VAS_190_Components", "components"));
                if (b.Description) metaBits.push(b.Description);
            } else {
                metaBits.push(msg("VAS_190_AsComponent", "this product as component"));
                // The ATTRIBUTE SET the detail line is specified for. A parent can
                // consume this product under one attribute set and not another,
                // and without it two lines of the same parent were the same row
                // printed twice.
                if (b.Attributes) metaBits.push(b.Attributes);
            }
            // When the record itself was created — the BOM on an own row, the
            // detail line on a where-used one. It is what the section orders on,
            // so it is stated rather than left to be inferred from the order.
            var created = formatDate(b.Created);
            if (created) {
                metaBits.push((b.Kind === "own"
                    ? msg("VAS_190_BomCreated", "created")
                    : msg("VAS_190_BomLineAdded", "added")) + " " + created);
            }
            metaBits.push(b.IsVerified
                ? msg("VAS_190_Verified", "verified")
                : msg("VAS_190_NotVerified", "not verified"));
            var meta = metaBits.join(" · ");
            $id.append($('<div class="vas_190-eM"></div>').text(meta).attr("title", meta));
            $row.append($id);

            if (b.Kind === "usedin") {
                var $val = $('<div class="vas_190-eVal"></div>');
                $val.append($('<div class="vas_190-eV"></div>')
                    .text("× " + formatNumber(+b.QtyPerParent || 0, 2)));
                $val.append($('<div class="vas_190-eS"></div>')
                    .text(msg("VAS_190_PerUnit", "per unit")));
                $row.append($val);
            }
            return $row;
        }

        // ----------------------------------------------------------------- //
        //  9. Quality parameters (Item only) - specification, never results   //
        // ----------------------------------------------------------------- //

        // The section is drawn for either half: a product can carry a plan with
        // no check against it yet, and a product whose plan has since been
        // cleared has still been checked.
        function hasQuality() {
            return any(data.Quality)
                || !!(data.QualityCheck && any(data.QualityCheck.Lines));
        }

        function renderQuality() {
            var rows = data.Quality || [];
            var check = data.QualityCheck;

            var $sec = section(msg("VAS_190_QualityParameters", "Quality parameters"),
                               (rows.length && rows[0].PlanName) ? rows[0].PlanName : "");

            // What was FOUND leads, above what is configured: the reader opening
            // this section on a product wants the last result before they want
            // the specification behind it.
            if (check && any(check.Lines)) $sec.append(buildQualityCheck(check));
            if (!rows.length) return;

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            paginate($sec, "quality", rows, QUALITY_ROWS_PER_PAGE, function (q) {
                // The specification is assembled from the fields that actually
                // carry a value, and nothing beyond them is invented. No range
                // bound is stated: a min and a max are a numeric tolerance that
                // nearly every parameter here leaves unset, and the section read
                // "Min 0.00 · Max 0.00" against tests that have no numeric range.
                //
                // BOTH descriptions are shown — the test parameter's own (what
                // the test is) and the plan's (what it is checked for here). They
                // answer different questions, so one no longer suppresses the
                // other; only an exact repeat is dropped.
                var specBits = [];
                if (q.ListValue)   specBits.push(q.ListValue);
                if (q.Observation) specBits.push(q.Observation);
                pushDistinct(specBits, q.ParameterDescription);
                pushDistinct(specBits, q.AssignedDescription);

                var trailing = "";
                if (q.Weightage !== null && q.Weightage !== undefined && +q.Weightage !== 0) {
                    trailing = formatNumber(q.Weightage, 0) + "%";
                }

                return listRow({
                    primary: q.ParameterName || "—",
                    meta: specBits.join(" · "),
                    value: trailing,
                    valueSub: trailing ? msg("VAS_190_Weightage", "weightage") : ""
                });
            }, $list);
        }

        // Appends a value the list does not already carry. Two descriptions that
        // happen to hold the same sentence are one fact, not two.
        function pushDistinct(bits, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            for (var i = 0; i < bits.length; i++) {
                if (bits[i] === text) return;
            }
            bits.push(text);
        }

        // Which confirmation the check was raised on. The panel says it in the
        // reader's words rather than printing the table it came from.
        var QC_SOURCE = {
            "RECEIPT":  { key: "VAS_190_QcOnReceipt",  text: "on receipt confirmation" },
            "MOVEMENT": { key: "VAS_190_QcOnMovement", text: "on transfer confirmation" }
        };

        // The latest quality CHECK: the document it was raised on, then every
        // parameter read on it with what was expected against what was found.
        // A parameter still to be read shows its result column empty rather than
        // a zero — nothing is invented to fill it.
        function buildQualityCheck(check) {
            var $card = $('<div class="vas_190-qcCard"></div>');

            var $head = $('<div class="vas_190-qcHead"></div>');
            $head.append($('<span class="vas_190-qcTitle"></span>')
                .text(msg("VAS_190_LatestQualityCheck", "Latest quality check")));
            $head.append(check.IsComplete
                ? chip(msg("VAS_190_QcComplete", "Checked"), "ok")
                : chip(msg("VAS_190_QcPending", "Pending"), "warn"));
            $card.append($head);

            var $list = $('<div class="vas_190-clist"></div>');
            $card.append($list);

            // The document the check hangs off, and the row opens it.
            var src = QC_SOURCE[check.Source];
            var docBits = [];
            if (src) docBits.push(msg(src.key, src.text));
            if (check.ConfirmationNo) docBits.push(check.ConfirmationNo);
            if (check.BPartnerName)   docBits.push(check.BPartnerName);
            if (check.QtyToVerify !== null && check.QtyToVerify !== undefined
                && +check.QtyToVerify !== 0) {
                docBits.push(formatNumber(check.QtyToVerify, 2) + " " +
                             msg("VAS_190_QcToVerify", "to verify"));
            }

            $list.append(listRow({
                primary: check.DocumentNo || msg("VAS_190_QcNoDocument", "(no document)"),
                meta: docBits.join(" · "),
                value: formatDate(check.CheckDate),
                openTable: check.DocTableName,
                openId: check.DocRecordId,
                openSOTrx: check.DocIsSOTrx
            }));

            for (var i = 0; i < check.Lines.length; i++) {
                var line = check.Lines[i];
                var expected = (line.AcceptableValue || "").trim();
                var actual   = (line.ActualValue || "").trim();

                var metaBits = [];
                if (expected) {
                    metaBits.push(msg("VAS_190_QcExpected", "expected") + " " + expected);
                }
                if (line.Remark) metaBits.push(line.Remark);

                $list.append(listRow({
                    primary: line.ParameterName || "—",
                    meta: metaBits.join(" · "),
                    value: actual || "—",
                    valueSub: actual
                        ? msg("VAS_190_QcResult", "result")
                        : msg("VAS_190_QcNotChecked", "not checked")
                }));
            }
            return $card;
        }

        // ----------------------------------------------------------------- //
        //  10. Supplier information                                          //
        // ----------------------------------------------------------------- //

        // A vendor row leads with WHICH vendor this is to the reader: the one the
        // product was last bought from, or an alternative to it. "Preferred" is a
        // separate statement — it is the vendor-product record's own flag, not a
        // fact about any purchase — so it rides on the row's detail line.
        function renderSuppliers() {
            var rows = data.Suppliers;

            var lastUsedCount = 0;
            for (var n = 0; n < rows.length; n++) if (rows[n].IsLastUsed) lastUsedCount++;
            var summary = rows.length + " " + (rows.length === 1
                ? msg("VAS_190_Vendor", "vendor")
                : msg("VAS_190_Vendors", "vendors"));
            if (rows.length > lastUsedCount) {
                summary += " · " + (rows.length - lastUsedCount) + " " +
                           msg("VAS_190_Alternative", "alternative");
            }
            var $sec = section(msg("VAS_190_SupplierInformation", "Supplier information"),
                               summary);

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            paginate($sec, "suppliers", rows, SUPPLIER_ROWS_PER_PAGE, function (v) {
                var sym = v.CurSymbol || v.ISO_Code || "";

                // What this vendor was last bought on, read from the orders
                // themselves. The stored PriceLastPO / PriceLastPODate stand in
                // only where there is no order history to read — on a tenant that
                // does not maintain them, the last used vendor showed nothing.
                var detail = [];
                var lastDate = formatDate(v.LastOrderDate || v.PriceLastPODate);
                var lastPrice = (v.LastOrderPrice !== null && v.LastOrderPrice !== undefined
                                 && +v.LastOrderPrice !== 0)
                    ? v.LastOrderPrice
                    : ((v.PriceLastPO !== null && v.PriceLastPO !== undefined
                        && +v.PriceLastPO !== 0) ? v.PriceLastPO : null);

                if (lastDate) {
                    var bought = msg("VAS_190_LastBought", "last bought") + " " + lastDate;
                    if (v.LastOrderNo) bought += " · " + v.LastOrderNo;
                    detail.push(bought);
                } else {
                    detail.push(msg("VAS_190_NoPurchaseYet", "no purchase yet"));
                }
                if (v.DeliveryTimePromised > 0) {
                    detail.push(msg("VAS_190_LeadTime", "lead time") + " " +
                                v.DeliveryTimePromised + " " + msg("VAS_190_Days", "days"));
                }
                // The vendor-product record's own flag, stated as what it is.
                if (v.IsCurrentVendor) {
                    detail.push(msg("VAS_190_PreferredVendor", "preferred vendor"));
                }

                return listRow({
                    primary: v.VendorName || "—",
                    meta: detail.join(" · "),
                    chip: v.IsLastUsed
                        ? { text: msg("VAS_190_LastUsed", "Last used"), tone: "ok" }
                        : { text: msg("VAS_190_AlternativeVendor", "Alternative"), tone: "neutral" },
                    // The PRICE the product was last bought at, which is what the
                    // reader compares vendors on. The vendor's own catalogue
                    // number was here and told them nothing about this vendor.
                    value: lastPrice === null ? "" : formatAmount(lastPrice, sym, v.CurPrecision),
                    valueSub: lastPrice === null ? "" : msg("VAS_190_LastPrice", "last price"),
                    openTable: "C_BPartner",
                    openId: v.C_BPartner_ID,
                    // A supplier row opens the VENDOR master. C_BPartner's zoom
                    // target is the customer master, so the click landed on the
                    // customer window with a vendor's id in it.
                    openWindows: VENDOR_WINDOW_NAMES
                });
            }, $list);
        }

        // ----------------------------------------------------------------- //
        //  11 / 12. Sales and purchase orders                                //
        // ----------------------------------------------------------------- //

        // The exact DocStatus labels, defined once and used by both order
        // renderers. The label is always the status name; only the tone varies.
        var DOC_STATUS = {
            "??": { key: "VAS_190_StatusUnknown",  text: "Unknown",              tone: "neutral" },
            "AP": { key: "VAS_190_StatusApproved", text: "Approved",             tone: "info" },
            "CL": { key: "VAS_190_StatusClosed",   text: "Closed",               tone: "neutral" },
            "CO": { key: "VAS_190_StatusCompleted",text: "Completed",            tone: "ok" },
            "DR": { key: "VAS_190_StatusDrafted",  text: "Drafted",              tone: "neutral" },
            "IN": { key: "VAS_190_StatusInvalid",  text: "Invalid",              tone: "crit" },
            "IP": { key: "VAS_190_StatusInProgress", text: "In Progress",        tone: "info" },
            "NA": { key: "VAS_190_StatusNotApproved", text: "Not Approved",      tone: "crit" },
            "RE": { key: "VAS_190_StatusReversed", text: "Reversed",             tone: "crit" },
            "VO": { key: "VAS_190_StatusVoided",   text: "Voided",               tone: "crit" },
            "WC": { key: "VAS_190_StatusWaitingConfirmation", text: "Waiting Confirmation", tone: "warn" },
            "WP": { key: "VAS_190_StatusWaitingPayment", text: "Waiting Payment", tone: "warn" }
        };

        function docStatusMeta(code) {
            var m = DOC_STATUS[code] || DOC_STATUS["??"];
            return { label: msg(m.key, m.text), tone: m.tone };
        }

        function renderSalesOrders() {
            renderOrderSection(data.SalesOrders, "so",
                msg("VAS_190_SalesOrders", "Sales orders"));
        }

        function renderPurchaseOrders() {
            renderOrderSection(data.PurchaseOrders, "po",
                msg("VAS_190_PurchaseOrders", "Purchase orders"));
        }

        // How much of what was ordered has actually moved. The wording follows the
        // direction: a purchase order is RECEIVED against, a sales order is
        // delivered against, and "due" means nothing has moved yet.
        var FULFIL_META = {
            "DUE":   { tone: "warn",
                       po: { key: "VAS_190_FulfilDue",           text: "Due" },
                       so: { key: "VAS_190_FulfilDue",           text: "Due" } },
            "SHORT": { tone: "info",
                       po: { key: "VAS_190_FulfilShortReceived",  text: "Short received" },
                       so: { key: "VAS_190_FulfilShortDelivered", text: "Short delivered" } },
            "FULL":  { tone: "ok",
                       po: { key: "VAS_190_FulfilFullyReceived",  text: "Fully received" },
                       so: { key: "VAS_190_FulfilFullyDelivered", text: "Fully delivered" } }
        };

        function fulfilChip(o) {
            var m = FULFIL_META[o.FulfilStatus];
            if (!m) return null;   // NONE — nothing ordered, nothing to report
            var word = o.IsSOTrx ? m.so : m.po;
            return { text: msg(word.key, word.text), tone: m.tone };
        }

        function renderOrderSection(rows, key, title) {
            var $sec = section(title, msg("VAS_190_Latest", "latest") + " " + rows.length);
            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var o = rows[i];
                var st = docStatusMeta(o.DocStatus);
                var sym = o.CurSymbol || o.ISO_Code || "";
                var fulfil = fulfilChip(o);

                var metaBits = [];
                var when = formatDate(o.DateOrdered);
                if (when) metaBits.push(when);
                // The ENTERED quantity, which is stated in the unit named beside
                // it. It used to be the base-unit figure under the line's own unit
                // name — 120 pieces reported as 120 cartons.
                metaBits.push(formatNumber(+o.Qty || 0, 2) + (o.UomName ? " " + o.UomName : ""));
                // An order with something still to move states WHEN it is due.
                // The outstanding QUANTITY used to sit here and is gone: the chip
                // already says the order is short or untouched, and a second
                // figure beside the ordered one only invited the two to be read
                // as the same thing. The date is what a reader acts on.
                if (o.FulfilStatus === "DUE" || o.FulfilStatus === "SHORT") {
                    var due = formatDate(o.DatePromised);
                    if (due) metaBits.push(msg("VAS_190_DueOn", "due") + " " + due);
                }
                // ONE chip on the row, and it is the fulfilment one. The
                // DOCUMENT's own state moves to the detail line rather than being
                // dropped — a drafted or voided order must not read as simply
                // "Due" — except where there is no fulfilment to report, and then
                // the document status is the chip so the row is never chipless.
                if (fulfil) metaBits.push(st.label);

                var primary = (o.DocumentNo || "—") +
                              (o.BPartnerName ? " · " + o.BPartnerName : "");

                $list.append(listRow({
                    primary: primary,
                    meta: metaBits.join(" · "),
                    chip: fulfil || { text: st.label, tone: st.tone },
                    value: formatAmount(o.LineNetAmt, sym, o.CurPrecision),
                    openTable: "C_Order",
                    openId: o.C_Order_ID,
                    openSOTrx: o.IsSOTrx
                }));
            }
        }

        // ----------------------------------------------------------------- //
        //  13. Recent transactions (Item only)                               //
        // ----------------------------------------------------------------- //

        // MovementType -> icon, tone and the name the icon's tooltip carries.
        var MOVEMENT = {
            "C-": { icon: "arrowUp",   tone: "ok",   key: "VAS_190_MvCustomerShipment", text: "Customer shipment" },
            "C+": { icon: "arrowDown", tone: "info", key: "VAS_190_MvCustomerReturn",   text: "Customer return" },
            "V+": { icon: "arrowDown", tone: "info", key: "VAS_190_MvVendorReceipt",    text: "Vendor receipt" },
            "V-": { icon: "arrowUp",   tone: "ok",   key: "VAS_190_MvVendorReturn",     text: "Vendor return" },
            "I+": { icon: "move",      tone: "warn", key: "VAS_190_MvInventoryIn",      text: "Inventory in" },
            "I-": { icon: "move",      tone: "warn", key: "VAS_190_MvInventoryOut",     text: "Inventory out" },
            "M+": { icon: "move",      tone: "warn", key: "VAS_190_MvMovementTo",       text: "Movement to" },
            "M-": { icon: "move",      tone: "warn", key: "VAS_190_MvMovementFrom",     text: "Movement from" },
            "P+": { icon: "move",      tone: "warn", key: "VAS_190_MvProductionIn",     text: "Production receipt" },
            "P-": { icon: "move",      tone: "warn", key: "VAS_190_MvProductionOut",    text: "Production issue" },
            "W+": { icon: "move",      tone: "warn", key: "VAS_190_MvWorkOrderIn",      text: "Work order receipt" },
            "W-": { icon: "move",      tone: "warn", key: "VAS_190_MvWorkOrderOut",     text: "Work order issue" }
        };

        function renderTransactions() {
            var rows = data.Transactions;
            var prec = +data.Product.UomPrecision || 0;
            // M_Transaction.MovementQty is in the product's BASE uom, as the
            // stock figures above are, so the unit is named on the row.
            var uom = data.Product.BaseUomName || "";

            // The section carries the product's WHOLE movement history — the set
            // its Transaction tab lists — and pages through it, so the count is
            // of all of them. The "of" clause stays for the case the two figures
            // ever disagree, which would mean rows the reader cannot see.
            var total = +data.TransactionTotal || rows.length;
            var summary = rows.length + " " +
                (rows.length === 1 ? msg("VAS_190_Movement", "movement")
                                   : msg("VAS_190_Movements", "movements"));
            if (total > rows.length) summary += " " + msg("VAS_190_Of", "of") + " " + total;
            var $sec = section(msg("VAS_190_RecentTransactions", "Recent transactions"), summary);

            var $grid = dataGrid("colsTx", [
                { label: msg("VAS_190_Document", "Document") },
                { label: msg("VAS_190_Date", "Date") },
                { label: msg("VAS_190_Qty", "Qty"), align: "r" },
                // The money column is the COST the movement was booked at, not a
                // price agreed on a document.
                { label: msg("VAS_190_UnitCost", "Unit cost"), align: "r" }
            ]);
            $sec.append($grid);

            paginate($sec, "tx", rows, ROWS_PER_PAGE, function (t) {
                // An unmapped movement type gets a named fallback rather than its
                // stored code — "M?" tells a reader nothing.
                var mv = MOVEMENT[t.MovementType] ||
                         { icon: "move", tone: "warn", key: "VAS_190_MvOther", text: "Stock movement" };
                var mvName = msg(mv.key, mv.text);
                var sym = t.CurSymbol || t.ISO_Code || "";

                var $row = $('<div class="vas_190-gRow"></div>');

                // The whole row opens the document it reports, through the same
                // zoom path every other navigating row uses. A movement whose
                // source document could not be resolved simply stays inert.
                var canOpen = !!(t.DocTableName && +t.DocRecordId > 0);
                if (canOpen) {
                    $row.addClass("vas_190-clickable")
                        .attr("role", "button")
                        .attr("tabindex", "0")
                        .attr("data-open-table", t.DocTableName)
                        .attr("data-open-id", t.DocRecordId);
                    // WHICH screen the document lives on cannot be read off the
                    // table: a receipt and a shipment are both M_InOut, a count
                    // and an internal use both M_Inventory. The server names the
                    // window from the document behind the movement's LINE, and
                    // the direction flag picks the sales or purchase window when
                    // the name resolves to nothing.
                    if (t.DocWindowName) {
                        $row.attr("data-open-windows", t.DocWindowName);
                    }
                    if (t.DocIsSOTrx) $row.attr("data-open-sotrx", "Y");
                }

                $row.append($('<span class="vas_190-gIcon"></span>')
                    .addClass("vas_190-ic-" + mv.tone)
                    .attr("title", mvName)
                    .append(svgIcon(mv.icon)));

                // Document type name in front of the number — the number alone
                // does not say what the movement was. The tenant's own C_DocType
                // name is used, so a renamed document type reads as it does
                // everywhere else in the application.
                //
                // A movement the panel cannot name a document for — an assembly
                // or production issue, whose document this section does not read
                // — is still a movement of this product, and the section carries
                // every one of them. It reads under its movement type rather
                // than as a bare dash.
                var docText;
                if (t.DocumentNo) {
                    docText = t.DocTypeName ? t.DocTypeName + " · " + t.DocumentNo : t.DocumentNo;
                } else {
                    docText = t.DocTypeName || mvName;
                }

                // WHERE the movement happened, under the document it was posted
                // by. One document posts a row per line, so without this two
                // movements of the same product on the same receipt were the same
                // row printed twice with nothing to tell them apart.
                var detailBits = [];
                if (t.LocatorName) {
                    detailBits.push(t.WarehouseName
                        ? t.WarehouseName + " · " + t.LocatorName : t.LocatorName);
                } else if (t.WarehouseName) {
                    detailBits.push(t.WarehouseName);
                }
                if (t.Attributes) detailBits.push(t.Attributes);
                var detail = detailBits.join(" · ");

                var $doc = $('<span class="vas_190-gDoc"></span>');
                $doc.append($('<span class="vas_190-gId"></span>').text(docText));
                if (detail) {
                    $doc.append($('<span class="vas_190-gSub"></span>').text(detail));
                }
                // The tooltip carries the movement type as well, which the row's
                // own text does not repeat.
                $doc.attr("title", docText + " — " + mvName + (detail ? " — " + detail : ""));
                if (canOpen) $doc.addClass("vas_190-gLink");
                $row.append($doc);

                $row.append(gridCell(formatDate(t.MovementDate) || "—"));
                $row.append(gridCell(qtyText(t.MovementQty, prec, uom), "r"));
                // The cost the movement was booked at. A movement with none
                // recorded shows a dash — nothing is computed to fill the column,
                // and a document price is not stood in for a cost.
                $row.append(gridCell(
                    (t.UnitCost === null || t.UnitCost === undefined)
                        ? "—" : formatAmount(t.UnitCost, sym, t.CurPrecision), "r"));
                return $row;
            }, $grid);
        }

        // ----------------------------------------------------------------- //
        //  14. Accounting details                                            //
        // ----------------------------------------------------------------- //

        // Every account the product's accounting tab can carry. The server sends
        // only the ones actually set on the product, in this order; a column this
        // map does not know still renders, under its own column name.
        var ACCOUNT_ROLE = {
            "P_Asset_Acct":                 { key: "VAS_190_AcctAsset",     text: "Product asset" },
            "P_Revenue_Acct":               { key: "VAS_190_AcctRevenue",   text: "Product revenue" },
            "P_COGS_Acct":                  { key: "VAS_190_AcctCogs",      text: "Product COGS" },
            "P_PurchasePriceVariance_Acct": { key: "VAS_190_AcctPpv",       text: "Purchase price variance" },
            "P_Expense_Acct":               { key: "VAS_190_AcctExpense",   text: "Product expense" },
            "P_Resource_Absorption_Acct":   { key: "VAS_190_AcctResource",  text: "Resource absorption" },
            "P_InvoicePriceVariance_Acct":  { key: "VAS_190_AcctIpv",       text: "Invoice price variance" },
            "P_InventoryClearing_Acct":     { key: "VAS_190_AcctInvClear",  text: "Inventory clearing" },
            "P_CostAdjustment_Acct":        { key: "VAS_190_AcctCostAdj",   text: "Cost adjustment" },
            "P_TradeDiscountRec_Acct":      { key: "VAS_190_AcctTdRec",     text: "Trade discount received" },
            "P_TradeDiscountGrant_Acct":    { key: "VAS_190_AcctTdGrant",   text: "Trade discount granted" },
            "P_MaterialOverhd_Acct":        { key: "VAS_190_AcctMatOverhd", text: "Material overhead" }
        };

        function renderAccounting() {
            var acct = data.Accounting;
            var summaryBits = [];
            // Which schema answered is part of the answer — a tenant with more
            // than one posts different accounts under each.
            if (acct.SchemaName)    summaryBits.push(acct.SchemaName);
            if (acct.CostingMethod) summaryBits.push(acct.CostingMethod);
            if (acct.CurrencyISO)   summaryBits.push(acct.CurrencyISO);

            var $sec = section(msg("VAS_190_AccountingDetails", "Accounting details"),
                               summaryBits.join(" · "));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < acct.Rows.length; i++) {
                var a = acct.Rows[i];
                var role = ACCOUNT_ROLE[a.AccountRole];
                // Every row here is an account set on the PRODUCT's own accounting
                // tab. Nothing is inherited from the product category any more, so
                // there is no "from category" qualifier to print — what the panel
                // shows is what that tab holds.
                $list.append(listRow({
                    primary: role ? msg(role.key, role.text) : a.AccountRole,
                    meta: a.Description || "",
                    value: a.Combination || "—"
                }));
            }
        }

        // ----------------------------------------------------------------- //
        //  15. Activity                                                      //
        // ----------------------------------------------------------------- //

        // Tag chip per event type: tone, icon and the word on the chip. The set
        // and the layout below it are VAS_092's — the two panels' feeds are read
        // by the same people and there is no reason for them to differ.
        var ACT_TYPES = {
            "mail":        { tone: "info",    icon: "mail",     key: "VAS_190_TagMail",        text: "Mail" },
            "workflow":    { tone: "ok",      icon: "check",    key: "VAS_190_TagWorkflow",    text: "Workflow" },
            "task":        { tone: "warn",    icon: "doc",      key: "VAS_190_TagTask",        text: "Task" },
            "appointment": { tone: "purple",  icon: "calendar", key: "VAS_190_TagAppointment", text: "Appointment" },
            "fieldupdate": { tone: "neutral", icon: "pencil",   key: "VAS_190_TagFieldUpdate", text: "Updated" },
            "note":        { tone: "neutral", icon: "doc",      key: "VAS_190_TagNote",        text: "Note" },
            // A chat comment is somebody typing on the record, which is not the
            // same event as a system-raised note — so it carries its own chip.
            "chat":        { tone: "info",    icon: "chat",     key: "VAS_190_TagChat",        text: "Chat" },
            // An inbound LETTER is a MailAttachment1 record like a mail, filed
            // under AttachmentType 'I' — the model splits the two now, where both
            // used to arrive typed "mail" and a letter was reported as an e-mail.
            "letter":      { tone: "purple",  icon: "mail",     key: "VAS_190_TagLetter",      text: "Letter" },
            // Calls (VA048_CallDetails), the one shared source this panel was
            // missing.
            "call":        { tone: "ok",      icon: "chat",     key: "VAS_190_TagCall",        text: "Call" }
        };

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            var $sec = section(msg("VAS_190_Activity", "Activity"),
                               rows.length + " " + msg("VAS_190_Events", "events"));

            // No events: the header states it and nothing else is drawn. There is
            // no fake row.
            if (!rows.length) return;

            var $list = $('<div class="vas_190-actList"></div>');
            $sec.append($list);

            // Entries are painted into the list itself, so page two lands where
            // page one did.
            paginate($sec, "activity", rows, ACTIVITY_PER_PAGE, buildActivityEntry, $list);
        }

        // One feed entry, in VAS_092's shape: tag chip | headline with its
        // sub-lines | right-aligned "when · who", and — for a mail — a caret and
        // the message body folded underneath. Row and body live in one wrapper so
        // the pager owns them as a single item.
        function buildActivityEntry(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES["note"];
            // A LETTER is the same MailAttachment1 record as a mail, filed under a
            // different attachment type, so it gets the same treatment throughout:
            // its addresses under the subject and its body on click.
            var isMail = (a.Type === "mail" || a.Type === "letter");
            // A task carries its own tone: closed reads as done, open as pending.
            var tone = (a.Type === "task" && a.IsClosed) ? "ok" : meta.tone;

            var $item = $('<div class="vas_190-actItem"></div>');
            var $row = $('<div class="vas_190-actRow"></div>');

            var $tag = $('<span class="vas_190-actTag"></span>').addClass("vas_190-tone-" + tone);
            if (meta.icon) $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.key, meta.text)));
            $row.append($tag);

            var title = activityTitle(a);
            var $title = $('<span class="vas_190-actTitle"></span>');
            $title.append($('<span class="vas_190-actLead"></span>')
                .text(title).attr("title", title));

            // A mail names its recipients under the subject — every address on
            // the To, Cc and Bcc lists, in full, so the line is not an
            // abridgement the reader has to open the message to resolve.
            if (isMail) {
                var to = recipientSummary(a);
                if (to) $title.append($('<small class="vas_190-actSub"></small>').text(to));
            }

            // A field edit headlines with the FIELD and states the MOVE beneath
            // it: was X → now Y, the old value struck through. Everything else
            // puts its own detail on that sub-line.
            if (a.Type === "fieldupdate") {
                if (a.OldValue || a.NewValue) $title.append(changeDelta(a));
            } else {
                var sub = activityMeta(a);
                if (sub) {
                    $title.append($('<small class="vas_190-actSub"></small>')
                        .text(sub).attr("title", sub));
                }
            }
            $row.append($title);

            var when = formatDateTime(a.EventDate) || "";
            if (a.Actor) when += (when ? " · " : "") + a.Actor;
            if (when) $row.append($('<span class="vas_190-actWhen"></span>').text(when));

            // A mail with a body opens on click; the caret shows the state.
            if (isMail && a.Body && String(a.Body).trim()) {
                $row.addClass("vas_190-is-openable")
                    .attr("role", "button")
                    .attr("tabindex", "0")
                    .attr("aria-expanded", "false")
                    .attr("data-openkind", "mail")
                    .attr("title", msg("VAS_190_ShowMail", "Show full mail"));
                $row.append($('<span class="vas_190-actCaret"></span>').append(svgIcon("chevRight")));
                $item.append($row);
                $item.append(buildMailBlock(a));
                return $item;
            }

            // A task or appointment opens onto the e-mails sent against IT — the
            // ones filed on AppointmentsInfo rather than on the product. The row
            // states how many; the drawer holds each one's recipient, subject,
            // moment, sender and message.
            if ((a.Type === "task" || a.Type === "appointment") && activityMails(a).length) {
                $row.addClass("vas_190-is-openable")
                    .attr("role", "button")
                    .attr("tabindex", "0")
                    .attr("aria-expanded", "false")
                    .attr("data-openkind", "appt")
                    .attr("title", msg("VAS_190_ShowMails", "Show e-mails"));
                $row.append($('<span class="vas_190-actCaret"></span>').append(svgIcon("chevRight")));
                $item.append($row);
                $item.append(buildApptMailBlock(a));
                return $item;
            }

            $item.append($row);
            return $item;
        }

        // The e-mails sent against a task or appointment (MailAttachment1 keyed on
        // AppointmentsInfo). Always an array, so callers can count and loop
        // without guarding.
        function activityMails(a) {
            return (a && a.Mails && a.Mails.length) ? a.Mails : [];
        }

        function mailCountLabel(n) {
            return n + " " + (n === 1 ? msg("VAS_190_Email", "email")
                                      : msg("VAS_190_Emails", "emails"));
        }

        // "was X → now Y" under the field's name. A value the log recorded as
        // empty reads as an em dash rather than as a blank, so a cleared field is
        // visibly cleared instead of looking like a rendering gap. VAS_092's.
        function changeDelta(a) {
            var blank = "—";
            var oldText = (a.OldValue === null || a.OldValue === undefined || a.OldValue === "")
                ? blank : String(a.OldValue);
            var newText = (a.NewValue === null || a.NewValue === undefined || a.NewValue === "")
                ? blank : String(a.NewValue);

            var $d = $('<small class="vas_190-actSub vas_190-actDelta"></small>');
            $d.append($('<span class="vas_190-cvOld"></span>').text(oldText));
            $d.append($('<span class="vas_190-cvArrow"></span>').text("→"));
            $d.append($('<span class="vas_190-cvNew"></span>').text(newText));
            $d.attr("title", oldText + " → " + newText);
            return $d;
        }

        // Every address the mail went to, written out in full and labelled.
        function recipientSummary(a) {
            var bits = [];
            appendAddressBit(bits, msg("VAS_190_To", "To"), a.MailTo);
            appendAddressBit(bits, msg("VAS_190_Cc", "Cc"), a.MailCc);
            appendAddressBit(bits, msg("VAS_190_Bcc", "Bcc"), a.MailBcc);
            return bits.join(" · ");
        }

        function appendAddressBit(bits, label, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(label + " " + text);
        }

        function activityTitle(a) {
            if (a.Type === "fieldupdate") {
                // The tag already says "Updated"; the FIELD is what tells one edit
                // from the next.
                return (a.Title || msg("VAS_190_FieldChanged", "Field changed"));
            }
            if (a.Type === "mail") {
                return (a.Title || "").trim() || msg("VAS_190_NoSubject", "(no subject)");
            }
            if (a.Type === "chat") {
                // The comment itself is the headline. It clips to one line in the
                // card and the full text is on the row's tooltip, so a long
                // comment is readable without an expander.
                var text = (a.Title || "").replace(/\s+/g, " ").trim();
                return text || msg("VAS_190_EmptyComment", "(empty comment)");
            }
            return (a.Title || "").trim() || msg("VAS_190_Event", "Event");
        }

        // The sub-line of every event EXCEPT a field edit, which states its move
        // through changeDelta instead.
        function activityMeta(a) {
            var bits = [];
            if (a.Type === "mail") {
                // The recipients have their own sub-line on the row now, so this
                // one states only the direction.
                bits.push(a.IsSent ? msg("VAS_190_MailSent", "Mail sent")
                                   : msg("VAS_190_MailReceived", "Mail received"));
            } else if (a.Type === "task") {
                bits.push(a.IsClosed ? msg("VAS_190_TaskCompleted", "Completed")
                                     : msg("VAS_190_TaskOpen", "Open"));
                appendMailCountBit(bits, a);
            } else if (a.Type === "appointment") {
                if (a.IsCancelled) bits.push(msg("VAS_190_Cancelled", "Cancelled"));
                if (a.Location) bits.push(a.Location);
                appendMailCountBit(bits, a);
            } else if (a.Type === "workflow") {
                // The dictionary label, resolved server-side in the reader's own
                // language. The stored code is only the last resort — a list
                // value must not reach the screen as "CC".
                if (a.StateName) bits.push(a.StateName);
                else if (a.StateCode) bits.push(a.StateCode);
            } else if (a.Type === "note") {
                if (a.Body) bits.push(String(a.Body).replace(/\s+/g, " ").substring(0, 160));
            }

            // The actor and the timestamp are the ROW's own right-hand column, so
            // they are not repeated here.
            return bits.join(" · ");
        }

        // The mail body, folded under its row. Every value goes in through
        // .text() — the stored message is untrusted text and is never handed to
        // the browser as markup.
        function buildMailBlock(a) {
            var $block = $('<div class="vas_190-actBody" style="display:none;"></div>');
            $block.append($('<div class="vas_190-mailSub"></div>')
                .text((a.Title || "").trim() || msg("VAS_190_NoSubject", "(no subject)")));

            var $meta = $('<div class="vas_190-mailMeta"></div>');
            appendMailRow($meta, msg("VAS_190_From", "From"), a.MailFrom);
            appendMailRow($meta, msg("VAS_190_To", "To"), a.MailTo);
            appendMailRow($meta, msg("VAS_190_Cc", "Cc"), a.MailCc);
            appendMailRow($meta, msg("VAS_190_Bcc", "Bcc"), a.MailBcc);
            appendMailRow($meta, msg("VAS_190_Date", "Date"), formatDateTime(a.EventDate));
            $block.append($meta);

            $block.append($('<div class="vas_190-mailBody"></div>').text(a.Body || ""));
            return $block;
        }

        function appendMailRow($meta, label, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) text = "—";
            var $row = $('<div class="vas_190-mailRow"></div>');
            $row.append($('<span class="vas_190-mailK"></span>').text(label));
            $row.append($('<span class="vas_190-mailV"></span>').text(text));
            $meta.append($row);
        }

        // How many e-mails were sent about this task or appointment. The count
        // only — the addresses, subjects and bodies are in the drawer, and a
        // meeting that generated several notices would otherwise fill the row.
        function appendMailCountBit(bits, a) {
            var n = activityMails(a).length;
            if (n) bits.push(mailCountLabel(n));
        }

        // The e-mails sent against a task or appointment, folded under its row —
        // each with who it went to, what it was about, when it went and who sent
        // it, then the message. Every value goes in through .text(): a stored
        // message is untrusted text and is never handed to the browser as markup.
        function buildApptMailBlock(a) {
            var $block = $('<div class="vas_190-actBody" style="display:none;"></div>');
            var mails = activityMails(a);

            for (var i = 0; i < mails.length; i++) {
                var m = mails[i];
                var $one = $('<div class="vas_190-actMailItem"></div>');
                // Ruled off from the one before it, so several notices about the
                // same meeting do not read as one long message.
                if (i > 0) $one.addClass("vas_190-actMailSplit");

                $one.append($('<div class="vas_190-mailSub"></div>')
                    .text((m.Subject || "").trim() || msg("VAS_190_NoSubject", "(no subject)")));

                var $meta = $('<div class="vas_190-mailMeta"></div>');
                appendMailRow($meta, msg("VAS_190_To", "To"), m.MailTo);
                appendMailRow($meta, msg("VAS_190_Date", "Date"), formatDateTime(m.SentOn));
                appendMailRow($meta, msg("VAS_190_SentBy", "Sent by"), m.SentBy);
                $one.append($meta);

                $one.append($('<div class="vas_190-mailBody"></div>').text(m.Body || ""));
                $block.append($one);
            }
            return $block;
        }

        // ----------------------------------------------------------------- //
        //  Icons (inline SVG - no icon font, which the host may not load)    //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            chevLeft:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            chevDown:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>',
            mail:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>',
            warehouse: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21V8l9-5 9 5v13"/><path d="M9 21v-6h6v6"/></svg>',
            bom:       '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>',
            arrowUp:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="7" y1="17" x2="17" y2="7"/><polyline points="7 7 17 7 17 17"/></svg>',
            arrowDown: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="17" y1="7" x2="7" y2="17"/><polyline points="17 17 7 17 7 7"/></svg>',
            move:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>',
            // The hero placeholder for a product with no picture: a 3-D box, drawn
            // as the three faces of one solid so it reads as the ITEM rather than
            // as a missing photograph.
            box3d:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.3 7 12 12 20.7 7"/><line x1="12" y1="22" x2="12" y2="12"/></svg>',
            // The activity tags. Same set and same drawing as VAS_092's, so the
            // two panels' feeds read as one pattern.
            check:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            doc:       '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            pencil:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            calendar:  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            chat:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2Z"/></svg>'
        };

        // Returns a span wrapping the named inline SVG (innerHTML so the browser
        // parses the SVG in HTML context — no namespace juggling). The markup is
        // this file's own constant, never database text.
        function svgIcon(name) {
            var $wrap = $('<span class="vas_190-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting                                                        //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        // The currency symbol always comes from the document / price list the
        // amount belongs to; nothing is hardcoded here.
        function formatAmount(value, symbol, precision) {
            var v = +value || 0;
            var sign = v < 0 ? "-" : "";
            var p = (precision >= 0) ? precision : 2;
            var text = Math.abs(v).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (symbol ? symbol + " " : "") + text;
        }

        // Parses a .NET/Newtonsoft value into a Date.
        //
        // asUtc = true  → genuine timestamps (Created / mail / event stamps). The
        //   DB stores these in UTC and Newtonsoft emits no timezone designator,
        //   which the browser would otherwise read as local wall-clock time. We
        //   tag it "Z" so toLocale* renders it in the viewer's own zone.
        // asUtc = false → date-only fields (order / valid-from / last-PO dates).
        //   These carry no meaningful time of day, so the value is parsed as-is
        //   and never shifted — the calendar day shown matches the day stored.
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

    VAS.VAS_190_ProductOverviewRightPanel.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_190_ProductOverviewRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        // The insert check is what makes New Record / Copy Record behave: the id
        // handed in for an unsaved row can still be the previously selected (or
        // copied-from) product's, so the tab's own insert state decides.
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        // Held rather than fetched outright: the insert flag is not always up yet
        // when we get here, so scheduleFetch asks once more before loading.
        this.scheduleFetch(recordID);
    };

    /* The platform Refresh button — exposed on the prototype as well as on the
       instance, since the host may reach either. */
    VAS.VAS_190_ProductOverviewRightPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) this.fetchData(this.record_ID);
        else this.clear();
    };

    /* Set width as per window width */
    VAS.VAS_190_ProductOverviewRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_190_ProductOverviewRightPanel.prototype.dispose = function () {
        // Kill any held fetch first — its timer would otherwise fire against a
        // panel whose curTab has just been nulled out below.
        if (typeof this.abortPendingFetch === "function") {
            try { this.abortPendingFetch(); } catch (e) { }
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
