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
        var ACTIVITY_PER_PAGE = 6;

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

        // Delegated once on the root so it survives every re-render: a document
        // row opens the record it points at, a mail card toggles its body.
        function bindEvents() {
            $root.on("click", "[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"),
                           $(this).attr("data-open-id"),
                           $(this).attr("data-open-sotrx") === "Y");
            });
            // Those rows are buttons, so they have to answer the keyboard the
            // way a button does.
            $root.on("keydown", "[data-open-table]", function (e) {
                if (e.which !== 13 && e.which !== 32) return;
                e.preventDefault();
                openRecord($(this).attr("data-open-table"),
                           $(this).attr("data-open-id"),
                           $(this).attr("data-open-sotrx") === "Y");
            });

            $root.on("click", ".vas_190-tlCard.vas_190-clickable", function () {
                toggleMail($(this));
            });
            // Mail expansion is keyboard-operable: Enter and Space both toggle.
            $root.on("keydown", ".vas_190-tlCard.vas_190-clickable", function (e) {
                if (e.which === 13 || e.which === 32) {
                    e.preventDefault();
                    toggleMail($(this));
                }
            });
        }

        function toggleMail($card) {
            var nowOpen = !$card.hasClass("vas_190-is-open");
            $card.toggleClass("vas_190-is-open", nowOpen)
                 .attr("aria-expanded", nowOpen ? "true" : "false");
        }

        // Opens a record's window filtered to that row through the platform's
        // zoom API. Never a full-page navigation — that crashes the host from
        // inside a panel. Degrades silently so a click can never throw.
        function openRecord(tableName, recordId, isSOTrx) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = 0;
                if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
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
            { key: "quality",     condition: function () { return isItem() && any(data.Quality); }, render: renderQuality },
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
        function metricCell(label, value, meta) {
            var $c = $('<div class="vas_190-mcell"></div>');
            $c.append($('<div class="vas_190-mLabel"></div>').text(label));
            $c.append($('<div class="vas_190-mVal"></div>').text(value).attr("title", value));
            if (meta) $c.append($('<div class="vas_190-mMeta"></div>').text(meta).attr("title", meta));
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
            if (opts.chip) $rhs.append(chip(opts.chip.text, opts.chip.tone));
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
            }
            return $row;
        }

        // Paginates a list of rows into a section: draws one page and, when there
        // is more than one, a pager beneath it. Page state lives in `pages` under
        // the section key, so each section pages independently.
        function paginate($sec, key, rows, perPage, buildRow) {
            var $host = $('<div class="vas_190-pageHost"></div>');
            var $pager = $('<div class="vas_190-pager"></div>');
            $sec.append($host);

            function paint() {
                var pageCount = Math.max(1, Math.ceil(rows.length / perPage));
                var page = pages[key] || 0;
                if (page >= pageCount) page = pageCount - 1;
                if (page < 0) page = 0;
                pages[key] = page;

                var start = page * perPage;
                var end = Math.min(rows.length, start + perPage);

                $host.empty();
                for (var i = start; i < end; i++) $host.append(buildRow(rows[i], i));

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
                    $img.empty().addClass("vas_190-imgEmpty").append(svgIcon("image"));
                });
                $img.append($tag);
            } else {
                $img.addClass("vas_190-imgEmpty").append(svgIcon("image"));
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
                $hero.append($('<div class="vas_190-statusLine vas_190-lineWarn"></div>')
                    .text(when
                        ? msg("VAS_190_DiscontinuedFrom", "Discontinued from") + " " + when +
                          " — " + msg("VAS_190_StockSellable", "existing stock can still be sold")
                        : msg("VAS_190_DiscontinuedNote",
                              "Discontinued — existing stock can still be sold")));
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
            "ON":        { key: "VAS_190_ChipOn",        text: "On",        tone: "info" },
            "MANDATORY": { key: "VAS_190_ChipMandatory", text: "Mandatory", tone: "warn" },
            "OPTIONAL":  { key: "VAS_190_ChipOptional",  text: "Optional",  tone: "neutral" }
        };

        function renderAttributes() {
            var rows = data.Attributes;
            var $sec = section(msg("VAS_190_Attributes", "Attributes"), rows[0].AttributeSetName || "");

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var a = rows[i];
                var control = ATTR_CONTROL[a.Name];
                var name = control ? msg(control.key, control.text) : (a.Name || "");
                var chipMeta = ATTR_CHIP[a.ChipKey] || ATTR_CHIP["OPTIONAL"];

                // The meta line only states what the record actually says: a
                // shelf life where one is configured, a value count for an
                // instance attribute. Nothing is inferred to fill the line.
                var meta = "";
                if (a.Kind === "control" && a.Name === "GUARANTEEDATE" && a.GuaranteeDays > 0) {
                    meta = msg("VAS_190_ShelfLife", "Shelf life") + " " + a.GuaranteeDays + " " +
                           msg("VAS_190_Days", "days");
                } else if (a.Kind === "instance") {
                    meta = msg("VAS_190_InstanceAttribute", "Instance attribute");
                    if (a.ValueCount > 0) {
                        meta += " · " + a.ValueCount + " " + msg("VAS_190_Values", "values");
                    }
                }

                $list.append(listRow({
                    primary: name,
                    meta: meta,
                    chip: { text: msg(chipMeta.key, chipMeta.text), tone: chipMeta.tone }
                }));
            }
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
                s.ReservedOrderCount + " " + msg("VAS_190_CompletedSalesOrders", "completed sales orders")));
            $card.append(metricCell(msg("VAS_190_OnOrder", "On order"),
                qtyText(s.OnOrderQty, prec, uom),
                msg("VAS_190_CompletedPurchaseOrders", "completed purchase orders")));
            $card.append(metricCell(msg("VAS_190_AvailableToPromise", "Available to promise"),
                qtyText(s.AvailableToPromise, prec, uom),
                msg("VAS_190_AtpFormula", "on hand − reserved")));
            $sec.append($card);
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
                $row.append(gridCell(formatNumber(+r.QtyOnHand || 0, prec), "r"));
                return $row;
            });

            // The pager and the grid rows are siblings, so the rows have to land
            // inside the grid rather than in the paginator's own host.
            $sec.find(".vas_190-pageHost").children().appendTo($grid);
            $sec.find(".vas_190-pageHost").remove();
        }

        // ----------------------------------------------------------------- //
        //  6. UOM conversions                                                //
        // ----------------------------------------------------------------- //

        function renderUomConversions() {
            var rows = data.UomConversions;
            var base = data.Product.BaseUomName || "";
            var $sec = section(msg("VAS_190_UomConversions", "UOM conversions"),
                               base ? msg("VAS_190_Base", "base") + ": " + base : "");

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var c = rows[i];
                // "= rate BaseUom" is assembled here, never in SQL.
                var value = "= " + formatNumber(+c.RateToBase || 0, rateDigits(c.RateToBase)) +
                            (base ? " " + base : "");
                $list.append(listRow({
                    primary: c.UomName || "—",
                    meta: c.IsProductSpecific
                        ? msg("VAS_190_ProductConversion", "Product-specific conversion")
                        : msg("VAS_190_GenericConversion", "Generic UOM conversion"),
                    value: value
                }));
            }
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
            var $sec = section(msg("VAS_190_Pricing", "Pricing"),
                rows.length + " " + (rows.length === 1
                    ? msg("VAS_190_PriceList", "price list")
                    : msg("VAS_190_PriceLists", "price lists")));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var p = rows[i];
                var sym = p.CurSymbol || p.ISO_Code || "";
                var metaBits = [];
                var eff = formatDate(p.ValidFrom);
                if (eff) metaBits.push(msg("VAS_190_Effective", "effective") + " " + eff);
                metaBits.push(msg("VAS_190_ListPrice", "list") + " " +
                              formatAmount(p.PriceList, sym, p.CurPrecision));
                metaBits.push(msg("VAS_190_LimitPrice", "limit") + " " +
                              formatAmount(p.PriceLimit, sym, p.CurPrecision));

                $list.append(listRow({
                    primary: p.PriceListName || "—",
                    primarySoft: p.VersionName || "",
                    meta: metaBits.join(" · "),
                    value: formatAmount(p.PriceStd, sym, p.CurPrecision),
                    valueSub: msg("VAS_190_StdPrice", "std price")
                }));
            }
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

            for (var j = 0; j < rows.length; j++) {
                var b = rows[j];
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
                $list.append($row);
            }
        }

        // ----------------------------------------------------------------- //
        //  9. Quality parameters (Item only) - specification, never results   //
        // ----------------------------------------------------------------- //

        function renderQuality() {
            var rows = data.Quality;
            var $sec = section(msg("VAS_190_QualityParameters", "Quality parameters"),
                               rows[0].PlanName || "");

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var q = rows[i];

                // The specification is assembled from the fields that actually
                // carry a value — a missing bound is left out rather than
                // printed as zero, and nothing beyond them is invented.
                var specBits = [];
                var hasMin = (q.MinValue !== null && q.MinValue !== undefined);
                var hasMax = (q.MaxValue !== null && q.MaxValue !== undefined);
                if (hasMin && hasMax) {
                    specBits.push(msg("VAS_190_Min", "Min") + " " + formatNumber(q.MinValue, 2) +
                                  " · " + msg("VAS_190_Max", "Max") + " " + formatNumber(q.MaxValue, 2));
                } else if (hasMin) {
                    specBits.push(msg("VAS_190_Min", "Min") + " " + formatNumber(q.MinValue, 2));
                } else if (hasMax) {
                    specBits.push(msg("VAS_190_Max", "Max") + " " + formatNumber(q.MaxValue, 2));
                }
                if (q.ListValue)   specBits.push(q.ListValue);
                if (q.Observation) specBits.push(q.Observation);
                if (q.AssignedDescription) specBits.push(q.AssignedDescription);
                else if (q.ParameterDescription) specBits.push(q.ParameterDescription);

                var trailing = "";
                if (q.Weightage !== null && q.Weightage !== undefined && +q.Weightage !== 0) {
                    trailing = formatNumber(q.Weightage, 0) + "%";
                }

                $list.append(listRow({
                    primary: q.ParameterName || "—",
                    meta: specBits.join(" · "),
                    value: trailing,
                    valueSub: trailing ? msg("VAS_190_Weightage", "weightage") : ""
                }));
            }
        }

        // ----------------------------------------------------------------- //
        //  10. Supplier information                                          //
        // ----------------------------------------------------------------- //

        function renderSuppliers() {
            var rows = data.Suppliers;
            var $sec = section(msg("VAS_190_SupplierInformation", "Supplier information"), "");

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var v = rows[i];
                var sym = v.CurSymbol || v.ISO_Code || "";

                // Only terms the vendor-product row actually stores.
                var metaBits = [];
                if (v.PriceLastPO !== null && v.PriceLastPO !== undefined && +v.PriceLastPO !== 0) {
                    metaBits.push(msg("VAS_190_LastPoPrice", "last PO") + " " +
                                  formatAmount(v.PriceLastPO, sym, v.CurPrecision));
                }
                var lastDate = formatDate(v.PriceLastPODate);
                if (lastDate) metaBits.push(lastDate);
                if (v.DeliveryTimePromised > 0) {
                    metaBits.push(msg("VAS_190_LeadTime", "lead time") + " " +
                                  v.DeliveryTimePromised + " " + msg("VAS_190_Days", "days"));
                }

                $list.append(listRow({
                    primary: v.VendorName || "—",
                    meta: metaBits.join(" · "),
                    // "Preferred" only on the current-vendor flag. Every other
                    // vendor is neutral — no other business status is invented.
                    chip: v.IsCurrentVendor
                        ? { text: msg("VAS_190_Preferred", "Preferred"), tone: "ok" }
                        : null,
                    value: v.VendorProductNo || "",
                    valueSub: v.VendorProductNo ? msg("VAS_190_VendorProductNo", "vendor product no.") : "",
                    openTable: "C_BPartner",
                    openId: v.C_BPartner_ID
                }));
            }
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

        function renderOrderSection(rows, key, title) {
            var $sec = section(title, msg("VAS_190_Latest", "latest") + " " + rows.length);
            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < rows.length; i++) {
                var o = rows[i];
                var st = docStatusMeta(o.DocStatus);
                var sym = o.CurSymbol || o.ISO_Code || "";

                var metaBits = [];
                var when = formatDate(o.DateOrdered);
                if (when) metaBits.push(when);
                metaBits.push(formatNumber(+o.Qty || 0, 2) + (o.UomName ? " " + o.UomName : ""));

                var primary = (o.DocumentNo || "—") +
                              (o.BPartnerName ? " · " + o.BPartnerName : "");

                $list.append(listRow({
                    primary: primary,
                    meta: metaBits.join(" · "),
                    chip: { text: st.label, tone: st.tone },
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
            var $sec = section(msg("VAS_190_RecentTransactions", "Recent transactions"),
                               msg("VAS_190_Showing", "showing") + " " + rows.length);

            var $grid = dataGrid("colsTx", [
                { label: msg("VAS_190_Document", "Document") },
                { label: msg("VAS_190_Date", "Date") },
                { label: msg("VAS_190_Qty", "Qty"), align: "r" },
                { label: msg("VAS_190_UnitPrice", "Unit price"), align: "r" }
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
                }

                $row.append($('<span class="vas_190-gIcon"></span>')
                    .addClass("vas_190-ic-" + mv.tone)
                    .attr("title", mvName)
                    .append(svgIcon(mv.icon)));

                // Document type name in front of the number — the number alone
                // does not say what the movement was. The tenant's own C_DocType
                // name is used, so a renamed document type reads as it does
                // everywhere else in the application.
                var docText = t.DocumentNo || "—";
                if (t.DocTypeName) docText = t.DocTypeName + " · " + docText;
                var $doc = gridCell(docText, null, true);
                // The tooltip carries the movement type as well, which the row's
                // own text does not repeat.
                $doc.attr("title", docText + " — " + mvName);
                if (canOpen) $doc.addClass("vas_190-gLink");
                $row.append($doc);

                $row.append(gridCell(formatDate(t.MovementDate) || "—"));
                $row.append(gridCell(formatNumber(+t.MovementQty || 0, prec), "r"));
                // A movement with no genuine price shows a dash. No cost is
                // computed to fill the column.
                $row.append(gridCell(
                    (t.UnitPrice === null || t.UnitPrice === undefined)
                        ? "—" : formatAmount(t.UnitPrice, sym, t.CurPrecision), "r"));
                return $row;
            });

            $sec.find(".vas_190-pageHost").children().appendTo($grid);
            $sec.find(".vas_190-pageHost").remove();
        }

        // ----------------------------------------------------------------- //
        //  14. Accounting details                                            //
        // ----------------------------------------------------------------- //

        var ACCOUNT_ROLE = {
            "P_Asset_Acct":                 { key: "VAS_190_AcctAsset",    text: "Product asset" },
            "P_Revenue_Acct":               { key: "VAS_190_AcctRevenue",  text: "Product revenue" },
            "P_COGS_Acct":                  { key: "VAS_190_AcctCogs",     text: "Product COGS" },
            "P_PurchasePriceVariance_Acct": { key: "VAS_190_AcctPpv",      text: "Purchase price variance" },
            "P_Expense_Acct":               { key: "VAS_190_AcctExpense",  text: "Product expense" },
            "P_Resource_Absorption_Acct":   { key: "VAS_190_AcctResource", text: "Resource absorption" }
        };

        function renderAccounting() {
            var acct = data.Accounting;
            var summaryBits = [];
            if (acct.CostingMethod) summaryBits.push(acct.CostingMethod);
            if (acct.CurrencyISO)   summaryBits.push(acct.CurrencyISO);

            var $sec = section(msg("VAS_190_AccountingDetails", "Accounting details"),
                               summaryBits.join(" · "));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            for (var i = 0; i < acct.Rows.length; i++) {
                var a = acct.Rows[i];
                var role = ACCOUNT_ROLE[a.AccountRole];
                var metaBits = [];
                if (a.Description) metaBits.push(a.Description);
                // Whether the product overrode the account or inherited it from
                // its category is worth stating — the figure is the same, the
                // place to change it is not.
                if (a.IsFromCategory) {
                    metaBits.push(msg("VAS_190_FromCategory", "from product category"));
                }

                $list.append(listRow({
                    primary: role ? msg(role.key, role.text) : a.AccountRole,
                    meta: metaBits.join(" · "),
                    value: a.Combination || "—"
                }));
            }
        }

        // ----------------------------------------------------------------- //
        //  15. Activity                                                      //
        // ----------------------------------------------------------------- //

        var ACT_TYPES = {
            "mail":        { tone: "info",    key: "VAS_190_TagMail",        text: "Mail" },
            "workflow":    { tone: "ok",      key: "VAS_190_TagWorkflow",    text: "Workflow" },
            "task":        { tone: "warn",    key: "VAS_190_TagTask",        text: "Task" },
            "appointment": { tone: "purple",  key: "VAS_190_TagAppointment", text: "Appointment" },
            "fieldupdate": { tone: "neutral", key: "VAS_190_TagFieldUpdate", text: "Field update" },
            "note":        { tone: "neutral", key: "VAS_190_TagNote",        text: "Note" },
            // A chat comment is somebody typing on the record, which is not the
            // same event as a system-raised note — so it carries its own chip.
            "chat":        { tone: "info",    key: "VAS_190_TagChat",        text: "Chat" }
        };

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            var $sec = section(msg("VAS_190_Activity", "Activity"),
                               rows.length + " " + msg("VAS_190_Events", "events"));

            // No events: the header states it and nothing else is drawn. There is
            // no fake row.
            if (!rows.length) return;

            var $timeline = $('<div class="vas_190-timeline"></div>');
            $sec.append($timeline);

            paginate($sec, "activity", rows, ACTIVITY_PER_PAGE, buildActivityEntry);
            $sec.find(".vas_190-pageHost").children().appendTo($timeline);
            $sec.find(".vas_190-pageHost").remove();
        }

        function buildActivityEntry(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES["note"];
            var isMail = (a.Type === "mail");
            // A task carries its own tone: closed reads as done, open as pending.
            var tone = (a.Type === "task" && a.IsClosed) ? "ok" : meta.tone;

            var $entry = $('<div class="vas_190-tlEntry"></div>');

            var $rail = $('<div class="vas_190-tlRail"></div>');
            $rail.append($('<div class="vas_190-tlDot"></div>'));
            $rail.append($('<div class="vas_190-tlTrail"></div>'));
            $entry.append($rail);

            var $card = $('<div class="vas_190-tlCard"></div>');
            if (isMail) {
                $card.addClass("vas_190-clickable")
                     .attr("role", "button")
                     .attr("tabindex", "0")
                     .attr("aria-expanded", "false")
                     .attr("title", msg("VAS_190_ShowMail", "Show full mail"));
            }

            var title = activityTitle(a);
            var $top = $('<div class="vas_190-tlTop"></div>');
            var $t = $('<span class="vas_190-tlTitle"></span>').attr("title", title);
            if (isMail) $t.append(svgIcon("mail"));
            $t.append($('<span></span>').text(title));
            if (isMail) $t.append(svgIcon("chevDown").addClass("vas_190-chev"));
            $top.append($t);
            $top.append(chip(msg(meta.key, meta.text), tone));
            $card.append($top);

            var metaText = activityMeta(a);
            if (metaText) {
                $card.append($('<div class="vas_190-tlMeta"></div>').text(metaText).attr("title", metaText));
            }

            if (isMail) $card.append(buildMailBlock(a));

            $entry.append($card);
            return $entry;
        }

        function activityTitle(a) {
            if (a.Type === "fieldupdate") {
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

        function activityMeta(a) {
            var bits = [];
            if (a.Type === "fieldupdate") {
                // "old → new", exactly as stored. No foreign-key id is resolved
                // to a display name — this source stays deliberately simple.
                var oldV = (a.OldValue === null || a.OldValue === undefined || a.OldValue === "")
                    ? msg("VAS_190_Empty", "(empty)") : String(a.OldValue);
                var newV = (a.NewValue === null || a.NewValue === undefined || a.NewValue === "")
                    ? msg("VAS_190_Empty", "(empty)") : String(a.NewValue);
                bits.push(oldV + " → " + newV);
            } else if (a.Type === "mail") {
                bits.push(a.IsSent ? msg("VAS_190_MailSent", "Mail sent")
                                   : msg("VAS_190_MailReceived", "Mail received"));
                if (a.MailTo) bits.push(a.MailTo);
            } else if (a.Type === "task") {
                bits.push(a.IsClosed ? msg("VAS_190_TaskCompleted", "Completed")
                                     : msg("VAS_190_TaskOpen", "Open"));
            } else if (a.Type === "appointment") {
                if (a.IsCancelled) bits.push(msg("VAS_190_Cancelled", "Cancelled"));
                if (a.Location) bits.push(a.Location);
            } else if (a.Type === "workflow") {
                // The dictionary label, resolved server-side in the reader's own
                // language. The stored code is only the last resort — a list
                // value must not reach the screen as "CC".
                if (a.StateName) bits.push(a.StateName);
                else if (a.StateCode) bits.push(a.StateCode);
            } else if (a.Type === "note") {
                if (a.Body) bits.push(String(a.Body).replace(/\s+/g, " ").substring(0, 160));
            }

            if (a.Actor) bits.push(msg("VAS_190_By", "by") + " " + a.Actor);
            var when = formatDateTime(a.EventDate);
            if (when) bits.push(when);
            return bits.join(" · ");
        }

        // The mail body, collapsed inside its own card. Every value goes in
        // through .text() — the stored message is untrusted text and is never
        // handed to the browser as markup.
        function buildMailBlock(a) {
            var $block = $('<div class="vas_190-mailInline"></div>');
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
            image:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>'
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
