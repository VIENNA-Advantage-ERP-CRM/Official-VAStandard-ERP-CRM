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
 *   VAI163   2026-08-26  A LETTER reads as a letter throughout the feed, not as a
 *                        mail wearing a different word. The model had already
 *                        split the two kinds; the panel had not followed it all
 *                        the way:
 *                        - The chip carries the DOCUMENT icon instead of the
 *                          envelope. At a glance down a feed the icon is what the
 *                          eye sorts on, so a letter still read as a mail however
 *                          the chip was worded.
 *                        - Its sub-line says "Letter sent" / "Letter received".
 *                          It had no branch of its own and carried nothing there.
 *                        - Its headline falls back to "(no subject)" as a mail's
 *                          does; it used to drop through to the generic branch and
 *                          a letter with no subject read "Event".
 *                        - Opening it is offered as "Show full letter". The row
 *                          carries its own open-kind (data-openkind="letter") so
 *                          the hint names the kind of correspondence being opened.
 *   VAI163   2026-08-26  - Recent transactions drops the UNIT COST column and runs
 *                          three columns (stylesheet: four tracks, the freed width
 *                          going to Document rather than being shared out). A
 *                          movement is a quantity leaving or arriving; what it was
 *                          valued at is an accounting question, and the Accounting
 *                          details section states the costing method it is valued
 *                          under. The column also read empty on every movement with
 *                          no cost detail recorded, which is most of them where
 *                          costing has not been run.
 *                        - The Document cell states the document TYPE in front of
 *                          the number on EVERY row. Where the document carries no
 *                          type - a production or job-work document whose table has
 *                          no C_DocType_ID - the movement's own name stands in, so
 *                          the column never prints a bare number with nothing
 *                          saying what it is.
 *                        - The Accounting section is drawn whenever the server
 *                          could name a schema, not only when the product sets an
 *                          account. A product with none says so, under the schema
 *                          and costing method it is still valued by, and notes that
 *                          postings fall back to the product category. Every
 *                          SERVICE product had no such section at all, with nothing
 *                          saying whether that was an absence or a failure.
 *   VAI163   2026-09-04  - A row that names its own window actually opens it. The
 *                          name was resolved through VIS.dataContext.getJSONRecord,
 *                          which the framework does not expose, off a response
 *                          envelope that was never unwrapped, and returned
 *                          synchronously from a lookup that goes over the wire —
 *                          so every name resolved to 0, was cached as a miss, and
 *                          a SUPPLIER row opened the customer window through the
 *                          zoom-target fallback. It asks this panel's own
 *                          controller now, the way the payload is fetched, and the
 *                          record opens in the callback.
 *                        - A price row reads down three lines: the price list with
 *                          its VERSION beside the name, then the three figures the
 *                          list holds named together (list, limit and standard),
 *                          then what those figures are FOR (per unit, attribute
 *                          set). The version used to trail the meta line, first to
 *                          be clipped, and the standard price sat alone in the
 *                          row's value slot away from the other two.
 *                        - The attribute set's mandatory type reads as words. The
 *                          stored code for "always mandatory" is 'Y', not the 'A'
 *                          the map named, so the section header printed the raw
 *                          letter against a set that is always mandatory.
 *   VAI163   2026-09-04  - Reserved and On order name the ONE open order behind
 *                          the figure, alongside its dates. Both figures answer
 *                          on the same terms now: the count is stated on the
 *                          purchase side as well as the sales side, and a single
 *                          order names itself under either.
 *                        - A RECEIVED mail reads as a mail and leads with WHO IT
 *                          CAME FROM. Inbound mails were typed as letters by the
 *                          model (AttachmentType 'I' is the inbox, not a letter),
 *                          so the reply to something sent from the product never
 *                          appeared in the feed as a mail; and the sub-line only
 *                          ever listed To / Cc / Bcc, which for an inbound message
 *                          names our own address and answers nothing.
 *                        - A vendor row states PREFERRED VENDOR and LAST USED as
 *                          labels, and a row carrying either is never also called
 *                          an alternative. "Preferred" was a phrase buried in the
 *                          detail line while the chip beside it said
 *                          "Alternative"; the section header counts alternatives
 *                          on the same test the rows do. The last price names the
 *                          unit it is in (PriceActual is per the product's BASE
 *                          unit), and the section lists vendors reached through
 *                          the purchase history as well as through the Vendor tab.
 *                        - The open-order caption leads with the order's NUMBER
 *                          and prints ONE date. An order raised and promised on
 *                          the same day printed that day twice under two labels,
 *                          which reads as the due date repeated; the due date is
 *                          the one that describes the figure, so "dated" is only
 *                          added where it differs.
 *                        - An INVENTORY REVALUATION and an INVOICE COST ADJUSTMENT
 *                          are named ('IR', 'VI'). Neither moves stock — both
 *                          restate what the stock on hand is worth — and both were
 *                          falling through to the unmapped-movement fallback and
 *                          reading "Stock movement", which is the one thing they
 *                          are not.
 *                        - The Latest quality check card is ONE row: the
 *                          confirmation's number, its document type, the sales
 *                          representative and the quantity to verify, with the
 *                          date at the right and the Checked / Pending chip beside
 *                          the heading. The per-parameter RESULT rows that
 *                          followed it are gone — they repeated the parameter
 *                          names the section lists below with a second reading
 *                          beside them, so the card said everything twice and
 *                          pushed the configured parameters, which the section is
 *                          named after, off the panel. Those parameters page five
 *                          at a time under the card, which stays at the top.
 *   VAI163   2026-09-04  - An activity entry opens its own DETAIL SHEET over the
 *                          panel: label and subject, then the fields that entry
 *                          actually carries, then its content, then its actions.
 *                          It replaces the drawer that folded open under a mail
 *                          row, which could hold a message body and nothing else
 *                          — an appointment's people, its meeting link and its
 *                          transcript, and a task's assignee and result, had
 *                          nowhere to go. Appointments, tasks, notes, mails and
 *                          letters all open; a field edit and a workflow step
 *                          state everything they have on the row itself. A mail
 *                          offers Reply, a recorded meeting offers its transcript
 *                          as a download.
 *                        - NO TOOLTIPS in the feed. Nothing there is abridged any
 *                          more — what a row cannot fit, the sheet holds — so a
 *                          tooltip repeating the line under the cursor was noise
 *                          that followed the pointer down the section.
 *                        - A task row leads with its PRIORITY, in the colours the
 *                          task screens use, and states who it is assigned to and
 *                          when it is due BEFORE its open / completed state, with
 *                          the completion percentage after it. None of the four
 *                          was on the row.
 *                        - A note reads "Note", not "Chat": CM_ChatEntry is the
 *                          plumbing, and what somebody writes on a product is a
 *                          note everywhere else in the application.
 *                        - Pagers state WHAT IS ON SCREEN at the leading edge
 *                          ("Showing 1 – 5 of 12") and put the controls at the
 *                          trailing one. All three used to sit together in the
 *                          middle, which said which page you were on but never
 *                          how much there was. Grid sections page at FIVE like
 *                          every list section; they ran at ten, so stock by
 *                          locator and transactions were blocks twice the height
 *                          of everything around them.
 *                        - The empty state is written for the reader who actually
 *                          sees it — somebody on a NEW record, whose product does
 *                          not exist yet. "No product selected" read as a fault on
 *                          a row they had just chosen to create.
 *   VAI163   2026-09-08  Corrections reported off the running panel:
 *                        - ACCOUNTING pages at five like every other list section,
 *                          and each row is now the account's NAME on the left, the
 *                          accounting default's own fields (Related To, Variance
 *                          Type, Recognize Type, Foreign Currency Revaluation)
 *                          beneath it, and the account COMBINATION in the bold
 *                          right-hand slot. The section header names the costing
 *                          method rather than printing its stored code.
 *                        - PRICING names its three figures in full — List Price,
 *                          Limit Price, Standard Price. The effective date moved
 *                          off the version's title onto the figures' own line,
 *                          which it qualifies, and the STANDARD PRICE became the
 *                          row's value beside the Current / Other version chip.
 *                        - A LETTER carries no direction and no correspondents: it
 *                          is an attached document, not a message that went one way
 *                          or the other. Only a mail states either.
 *                        - The QUALITY CHECK row opens the Ship/GRN or Material
 *                          Transfer confirmation screen BY NAME; the zoom target it
 *                          used is not one the reader's role may open.
 *                        - A SUPPLIER row no longer says "not on vendor tab", and
 *                          its last price is stated in the purchase order's own
 *                          unit rather than the product's base unit.
 *                        - A BOM row states the ATTRIBUTE SET it is specified for
 *                          in its right-hand slot — on an own BOM as well, which
 *                          never reported one at all.
 *
 *   VAI163   2026-09-08  Activity section, second pass:
 *                        - The feed keeps itself current. The 20s signature poll
 *                          is now only the BACKSTOP: any XHR on the page that is
 *                          not this panel's own brings the next check forward to
 *                          ~1.2s, so filing a note or sending a mail from the
 *                          window's own dialog shows up as soon as it commits
 *                          instead of up to twenty seconds later. VAS_105 does
 *                          this by listening for 'CreateJson_Task' by name — it
 *                          owns the button that raises that dialog; this panel
 *                          owns none of them, so it reacts to any request and
 *                          lets the SIGNATURE decide whether anything changed.
 *                          Two rules keep that safe: a pending check is only ever
 *                          moved EARLIER (otherwise steady page traffic would
 *                          defer it for ever), and the settling retry is armed
 *                          only when a nudge actually moved it.
 *                        - The detail sheet has ONE way out. The header cross was
 *                          a second control doing the footer Close button's job.
 *                        - The chat chip reads through VAS_190_TagChatNote, a key
 *                          of its own, so a tenant that had seeded
 *                          VAS_190_TagChat as "Chat" gets "Note".
 *   VAI163   2026-09-08  Activity section, third pass — the two entries that lead
 *                        somewhere the panel cannot go itself:
 *                        - A TASK row opens the platform's task FORM
 *                          (WSP.EditTaskForm), the call VAS_105 and VAS_123 both
 *                          make. A task is the one entry a reader opens in order
 *                          to DO something — reassign it, move its date, tick it
 *                          off — and this panel's sheet is read-only, so it showed
 *                          all of that and let them change none of it. A page with
 *                          no task form loaded still falls back to the sheet.
 *                        - REPLY opens the application's own composer
 *                          (VIS.Email in a VIS.CFrame), as VAS_105's e-mail detail
 *                          does. It was a `mailto:` link, which leaves the
 *                          application entirely: composed outside the tenant, sent
 *                          from the reader's personal account, filed against
 *                          nothing — so the reply never came back to the feed it
 *                          was sent from, and did nothing at all where no mail
 *                          client is registered. The quoted body is escaped on the
 *                          way into the composer's HTML.
 *                        - A mail body is shown FORMATTED — the sender's
 *                          paragraphs, tables, lists and links — as VAS_105's
 *                          e-mail modal shows one, instead of flattened to a
 *                          single run of text. It renders ActivityData.BodyHtml,
 *                          which the server sanitises to a whitelist; VAS_105
 *                          assigns the stored body straight to innerHTML and that
 *                          is the part not copied. This is the only .html() call
 *                          in the panel that takes a server string — everything
 *                          else, a.Body included, must keep using .text().
 *   VAI163   2026-09-08  The task popup did not open, and the reason was one
 *                        global. WSP.EditTaskForm reaches for window.$backBtn_ID
 *                        and throws where it is undefined; the click went into
 *                        openTaskForm's catch and put the read-only sheet on
 *                        screen, which looks exactly like the click being
 *                        ignored. VAS_105 and VAS_123 both define that global —
 *                        but only on their NEW-task button, so their EDIT path
 *                        works because that button has already run at some point
 *                        in the session. This panel raises no tasks of its own,
 *                        so nothing ever defined it. Defined here before the
 *                        call, and both failure paths now say so in the console
 *                        instead of falling through in silence.
 *                        The popup's CLOSE is watched as well: wsptask.js can
 *                        throw inside its own success callback, which aborts
 *                        jQuery's chain so `ajaxComplete` never fires and the
 *                        feed's usual nudge never happens.
 *                        Second cause, from the same report: the activity row's
 *                        click was left to BUBBLE. VAS_105 and VAS_123 both stop
 *                        theirs, and for good reason — the popup opens and the
 *                        tail of the very click that asked for it reaches the
 *                        document handlers that dismiss one, so nothing appears
 *                        to happen. Stopped on both the click and the keyboard
 *                        path.
 *                        Third cause, and the actual one: WSP.EditTaskForm DOES
 *                        NOT EXIST. Grepping the framework bundles it is in
 *                        (VIS.all.min*.js, VIS2_0.min*.js) finds no
 *                        "EditTaskForm", no "wsptask", no "divTaskContinerFrom"
 *                        and no "wsp-task-form" anywhere — the name VAS_105 and
 *                        VAS_123 call on their task rows resolves to undefined,
 *                        so THEIR task click is dead on this deployment too and
 *                        copying it could only ever reproduce that. What the
 *                        platform really has is WSP.WSP_AppointmentsForm, which
 *                        is what the VIS history panel's own edit button calls,
 *                        with VIS.AppointmentsForm.init as the CREATE wrapper
 *                        over WSP.TaskForm / WSP.AppointmentsForm. WSP itself is
 *                        an optional add-on; the framework guards every call to
 *                        it with `if (window.WSP)`.
 *                        WSP_AppointmentsForm is not exposed on this
 *                        installation either, so on the user's instruction the
 *                        row opens VIS.AppointmentsForm.init(tableId, recordId,
 *                        userId, userName, true) — the same five-argument call
 *                        VAS_105, VAS_123 and VAS_120 all make successfully.
 *                        KNOWN LIMIT: that is the CREATE entry point and its
 *                        sixth argument is a boolean, not a record id, so the
 *                        popup opens the task form on the PRODUCT and is not
 *                        loaded with the task that was clicked. Opening the
 *                        clicked task needs an API this installation does not
 *                        have.
 *                        And where WSP is absent ALTOGETHER the row goes back to
 *                        the read-only detail sheet it opened before any of
 *                        this. That needs its own check: VIS.AppointmentsForm's
 *                        whole body is `if (window.WSP) {…} else alert(…)`, and
 *                        it returns normally in the else — so without the guard
 *                        the reader gets a browser alert about a module they
 *                        cannot install, and the panel, having seen no
 *                        exception, shows nothing at all.
 *                        Task PRIORITY colours now come from VAS_105's set
 *                        (.vas_105_acct-prio--*) instead of this panel's generic
 *                        warn / info tokens, which had drifted; a closed task
 *                        takes the fourth, resolved colour whatever it was
 *                        raised at.
 *
 * ── Labels / Message Keys added 2026-09-08 ─────────────────────────────
 *  VAS_190_Yes ("Yes"), VAS_190_No ("No"), VAS_190_AttributeSet
 *  ("Attribute set"), VAS_190_TagChatNote ("Note" — replaces
 *  VAS_190_TagChat, which is no longer read). VAS_190_ListPrice,
 *  VAS_190_LimitPrice and VAS_190_StdPrice keep their keys but their
 *  English defaults changed to "List Price", "Limit Price" and "Standard
 *  Price" — a tenant that has SEEDED those three keys must update the
 *  seeded text as well, or the old abbreviations stay on screen.
 *  VAS_190_NotOnVendorTab and VAS_190_LetterSent / VAS_190_LetterReceived
 *  are no longer read.
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

    // Instance counter, so each panel's document-level handlers get their own
    // event namespace and one panel's dispose cannot unbind another's.
    VAS._vas190Seq = VAS._vas190Seq || 0;

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
        // FIVE everywhere. The grid sections (stock by locator, transactions) ran
        // at ten while every list section ran at five, so a product with stock in
        // a dozen locators — or any real movement history — put a block twice the
        // height of everything else in the middle of the panel.
        var ROWS_PER_PAGE = 5;
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
        // Accounting pages at five too. A tenant on the FRPT scheme sets an
        // account per ROLE — a dozen and more on a fully configured product — and
        // the section used to list all of them at once.
        var ACCOUNT_ROWS_PER_PAGE = 5;
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
            // The empty state is read almost entirely by somebody on a NEW record:
            // the panel has nothing to show because the product does not exist
            // yet, which is not the same as nothing being selected. "No product
            // selected" read as a fault on a row the user had just chosen to
            // create.
            $emptyState = $('<div class="vas_190-empty" style="display:none;"></div>');
            $emptyState.append($('<div class="vas_190-emptyTitle"></div>')
                .text(msg("VAS_190_NoData", "No product information added yet")));
            $emptyState.append($('<div class="vas_190-emptyHint"></div>')
                .text(msg("VAS_190_NoDataHint", "Add product details to see them here.")));
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

            // An activity row opens its own DETAIL SHEET over the panel. It used
            // to fold a drawer open beneath itself, which could hold a message
            // body and nothing else — an appointment's people, its meeting link
            // and its transcript, and a task's assignee and result, had nowhere to
            // go at all.
            //
            // The click is STOPPED here, as VAS_105 and VAS_123 stop theirs on
            // the equivalent row. A task hands over to a platform popup, and a
            // click left to bubble reaches the document handlers that dismiss
            // one — so the form opened and was closed again by the tail of the
            // very click that asked for it, which looks exactly like nothing
            // having happened.
            $root.on("click", ".vas_190-actRow.vas_190-is-openable", function (e) {
                e.stopPropagation();
                e.stopImmediatePropagation();
                openActivityDetail(+$(this).attr("data-act-index"));
            });
            // Those rows are buttons, so they answer the keyboard as one.
            $root.on("keydown", ".vas_190-actRow.vas_190-is-openable", function (e) {
                if (e.which === 13 || e.which === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    e.stopImmediatePropagation();
                    openActivityDetail(+$(this).attr("data-act-index"));
                }
            });
        }

        // Opens a record's window filtered to that row through the platform's
        // zoom API. Never a full-page navigation — that crashes the host from
        // inside a panel. Degrades silently so a click can never throw.
        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click.
        var windowIdByName = {};

        // Resolves ONE window name and hands the answer to `cb` — the id, or 0
        // when the name names nothing here.
        //
        // Three things were wrong with the way it asked before, and each on its
        // own was enough to make every name resolve to 0:
        //   * VIS.dataContext.getJSONRecord is not a function the framework
        //     exposes, so the guard above the call was never satisfied and the
        //     lookup never left the browser;
        //   * the controller answers with its JSON envelope, { windowId: n }, and
        //     the id was parsed straight off that object rather than out of it;
        //   * a lookup over the wire cannot answer synchronously, so a function
        //     RETURNING the id could only ever return the miss.
        // The name was then cached as -1 — "the dictionary does not know it" — so
        // the one screen this exists for, a supplier row, opened the CUSTOMER
        // window through the zoom-target fallback for the rest of the session.
        //
        // It asks the panel's own controller the same way the payload is fetched.
        function resolveWindowIdByName(windowName, cb) {
            if (!windowName) { cb(0); return; }
            if (windowIdByName.hasOwnProperty(windowName)) {
                cb(windowIdByName[windowName] > 0 ? windowIdByName[windowName] : 0);
                return;
            }

            var base = "";
            try { base = VIS.Application.contextUrl || ""; } catch (e) { cb(0); return; }

            $.ajax({
                url: base + "VAS_190_ProductOverviewRightPanel/GetWindowId",
                type: "GET",
                dataType: "json",
                data: { windowName: windowName },
                success: function (raw) {
                    var id = 0;
                    try {
                        var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        id = res ? parseInt(res.windowId, 10) : 0;
                    } catch (e2) { id = 0; }
                    if (isNaN(id) || id < 0) id = 0;
                    // A name that resolves to nothing is remembered as -1 so the
                    // miss is not asked again on every click.
                    windowIdByName[windowName] = id > 0 ? id : -1;
                    cb(id);
                },
                error: function () {
                    windowIdByName[windowName] = -1;
                    cb(0);
                }
            });
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
        var VENDOR_WINDOW_NAMES = ["VAS_VendorMaster"];

        // The names are tried one after the other rather than all at once: the
        // first that resolves is the answer, and asking for the rest would be
        // work whose result is thrown away.
        function resolveFirstWindowId(names, cb) {
            if (!names || !names.length) { cb(0); return; }
            var i = 0;
            (function next() {
                if (i >= names.length) { cb(0); return; }
                resolveWindowIdByName(names[i++], function (id) {
                    if (id > 0) cb(id); else next();
                });
            })();
        }

        // Opens a record's window filtered to that row through the platform's
        // zoom API. Never a full-page navigation — that crashes the host from
        // inside a panel. Degrades silently so a click can never throw.
        function openRecord(tableName, recordId, isSOTrx, windowNames) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            // A window named on the ROW wins: it is the only thing that can tell
            // two records of the same table apart, which is exactly the
            // customer-versus-vendor case. Resolving it is a round trip, so the
            // open happens in the callback — the first click on a name pays for
            // the lookup, every click after it is answered from the cache.
            resolveFirstWindowId(windowNames, function (namedId) {
                try {
                    var windowId = namedId;
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
            });
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
                    // Re-baseline the activity watcher against what was just painted,
                    // so the next poll compares like with like.
                    startActivityWatch(recordID, data && data.ActivityStamp);
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
            stopActivityWatch();
            data = null;
            shownRecordId = 0;
            pages = {};
            render();
            // A discarded reply never reaches its own showBusy(false), so the
            // spinner would otherwise sit on the empty panel for good.
            showBusy(false);
        };

        // ----------------------------------------------------------------- //
        //  Activity watcher — keeps the feed current on its own              //
        // ----------------------------------------------------------------- //
        //
        // Mails, notes, appointments, tasks and calls are raised from the
        // window's OWN toolbars and dialogs. Those are framework code the panel
        // cannot hook, and none of them touches M_Product — so no record-level
        // event fires, refreshPanelData is never called, and the feed sat stale
        // until somebody pressed Refresh by hand.
        //
        // So the panel watches instead: it asks the server for a cheap signature
        // of the activity sources (row count + latest change stamp) and re-reads
        // the overview only when that differs from what is on screen. The full
        // overview runs every section's query, which is why it is not what gets
        // polled.
        //
        // The watch is deliberately cheap AND polite:
        //   - it stops entirely while the browser tab is hidden, and checks once
        //     immediately on return, which is the case that matters — the user
        //     went to another tab, sent the mail, and came back;
        //   - a check already in flight is never stacked on by another;
        //   - it is torn down on record change, on clear and on dispose, so a
        //     timer can never outlive the panel it belongs to.
        //
        // THE POLL ALONE IS NOT ENOUGH, and that is what "it does not refresh"
        // meant: a reader who files a note, sends a mail or books an appointment
        // is looking straight at the feed when they close the dialog, and up to
        // twenty seconds of nothing reads as a panel that has not noticed.
        //
        // So the poll became the BACKSTOP and the AJAX traffic became the signal.
        // Every one of these activities is saved by an XHR — the platform's own
        // dialogs are framework code this panel cannot hook, but it can hear them
        // finish. A completed request that is not one of this panel's own brings
        // the next signature check forward to a second and a bit, which is long
        // enough for the save's transaction to have committed and short enough to
        // read as immediate. Nothing is assumed about WHICH request it was: the
        // signature says whether anything actually changed, and where nothing did
        // the check costs one indexed count and paints nothing.
        //
        // This is VAS_105's mechanism generalised. That panel listens for
        // 'CreateJson_Task' by name because it OWNS the button that raises the
        // dialog; this one owns none of them, so it cannot name the endpoints and
        // does not try to.
        var ACTIVITY_POLL_MS = 20000;
        // How long after a foreign XHR the check runs. Long enough for the save
        // to have committed, short enough that the feed appears to react to it.
        var ACTIVITY_NUDGE_MS = 1200;
        // The one follow-up a nudged check gets when it found nothing, for the
        // save that had not committed when it asked.
        var ACTIVITY_SETTLE_MS = 2500;
        var activityTimer = null;
        var activityStamp = null;
        var activityWatchId = 0;
        var activityCheckInFlight = false;
        // When the pending check is due, so a later request cannot postpone it.
        var activityDueAt = 0;
        // A nudged check is outstanding: if it finds nothing, look once more.
        var activityNudged = false;

        // Signature comparison. Count AND stamp, because neither alone is enough:
        // the count misses an EDIT to an existing row, and the stamp misses a
        // DELETE (which lowers the count while leaving the maximum untouched).
        function stampDiffers(a, b) {
            if (!a || !b) return false;      // nothing to compare yet — never refresh on a guess
            return (+a.Count || 0) !== (+b.Count || 0)
                || String(a.LastChange || "") !== String(b.LastChange || "");
        }

        function stopActivityWatch() {
            activityWatchId++;               // orphan any reply still in flight
            if (activityTimer) { clearTimeout(activityTimer); activityTimer = null; }
            activityStamp = null;
            activityCheckInFlight = false;
            activityDueAt = 0;
            activityNudged = false;
        }

        function startActivityWatch(recordID, stamp) {
            stopActivityWatch();
            if (!(recordID > 0)) return;
            activityStamp = stamp || null;
            var myWatch = activityWatchId;
            scheduleActivityCheck(recordID, myWatch);
        }

        // Schedules the one outstanding check, and only ever brings it EARLIER.
        //
        // The "only earlier" rule is what makes the AJAX nudge safe. A nudge that
        // simply replaced the pending timer would, on a page with steady
        // background traffic, push the check back by ACTIVITY_NUDGE_MS on every
        // request and never let it run at all — the feed would be starved by the
        // very mechanism meant to keep it current. A request that asks for a
        // check no sooner than the one already booked is therefore ignored.
        // Returns whether it actually (re)booked the check.
        function scheduleActivityCheck(recordID, myWatch, delayMs) {
            if (myWatch !== activityWatchId) return false;
            var delay = delayMs > 0 ? delayMs : ACTIVITY_POLL_MS;
            var due = (new Date()).getTime() + delay;
            if (activityTimer) {
                if (due >= activityDueAt) return false;  // already booked, sooner
                clearTimeout(activityTimer);
                activityTimer = null;
            }
            activityDueAt = due;
            activityTimer = setTimeout(function () {
                activityTimer = null;
                checkActivity(recordID, myWatch);
            }, delay);
            return true;
        }

        // Something else on the page finished an XHR — very likely the dialog the
        // reader just saved an activity in. Bring the next check forward.
        //
        // The flag is what covers a dialog that closes over SEVERAL requests. The
        // check lands a second after the FIRST of them (see the rule above), which
        // can be before the save has committed; a nudged check that finds nothing
        // therefore books one more soon after instead of dropping straight back to
        // the twenty-second backstop.
        function nudgeActivityCheck() {
            if (!(shownRecordId > 0)) return;
            if (document.hidden) return;
            // The flag is raised only where the nudge actually MOVED the check
            // forward. Raising it on every request would mark the ordinary
            // backstop poll as nudged on any busy page, and the settling retry
            // would quietly turn a twenty-second watch into a two-and-a-half
            // second one.
            if (scheduleActivityCheck(shownRecordId, activityWatchId, ACTIVITY_NUDGE_MS)) {
                activityNudged = true;
            }
        }
        this.nudgeActivityCheck = function () { nudgeActivityCheck(); };

        function checkActivity(recordID, myWatch) {
            if (myWatch !== activityWatchId) return;
            if (recordID !== shownRecordId) return;   // panel moved on
            // Hidden tab: do not poll at all. document.visibilitychange below asks
            // once as soon as it comes back, so nothing is missed — it is only the
            // pointless traffic behind a hidden tab that is skipped.
            if (document.hidden) { scheduleActivityCheck(recordID, myWatch); return; }
            // A check is already out. Retry SOON rather than at the full interval:
            // this branch is reached when a save lands while the backstop poll is
            // mid-flight, and that is precisely the moment something changed.
            if (activityCheckInFlight) {
                scheduleActivityCheck(recordID, myWatch, ACTIVITY_NUDGE_MS);
                return;
            }

            activityCheckInFlight = true;
            // Whether THIS check is the one a nudge asked for. Taken now, so a
            // request arriving while it is in flight raises the flag again for
            // the next one rather than being answered by this one's reply.
            var wasNudged = activityNudged;
            activityNudged = false;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_190_ProductOverviewRightPanel/GetActivityStamp",
                type: "GET",
                dataType: "json",
                data: { M_Product_ID: recordID },
                success: function (raw) {
                    activityCheckInFlight = false;
                    if (myWatch !== activityWatchId) return;
                    var next = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (stampDiffers(activityStamp, next)) {
                        // fetchData re-baselines the watcher itself on success, so
                        // this does not reschedule — that would double the timer.
                        $self.fetchData(recordID);
                        return;
                    }
                    if (next) activityStamp = next;
                    // Nothing yet. A check that a save prompted looks once more
                    // before dropping back to the backstop: it may simply have
                    // asked before that save's transaction committed.
                    scheduleActivityCheck(recordID, myWatch,
                                          wasNudged ? ACTIVITY_SETTLE_MS : 0);
                },
                error: function () {
                    activityCheckInFlight = false;
                    if (myWatch !== activityWatchId) return;
                    // A failed check is not a reason to stop watching — the next one
                    // may well succeed (a dropped connection, a restarted app pool).
                    scheduleActivityCheck(recordID, myWatch);
                }
            });
        }

        // Coming back to the tab is the moment worth checking at once: the user
        // very likely just did the thing the feed needs to show.
        this.onVisibilityChange = function () {
            if (document.hidden) return;
            if (!(shownRecordId > 0)) return;
            if (activityTimer) { clearTimeout(activityTimer); activityTimer = null; }
            checkActivity(shownRecordId, activityWatchId);
        };

        // Exposed so dispose() can tear the watch down from the prototype.
        this.stopActivityWatch = function () { stopActivityWatch(); };

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
            // Drawn whenever the server could name an accounting schema, whether or
            // not the product sets an account of its own. It used to need at least
            // one account row, so a product with none — and every SERVICE product,
            // which the reader refused outright — had no Accounting section at all,
            // with nothing saying whether that was an absence or a failure.
            { key: "accounting",  condition: function () { return !!data.Accounting; }, render: renderAccounting },
            // Activity is the one section that renders empty — it reports the
            // absence of events rather than hiding the fact that there are none.
            { key: "activity",    condition: function () { return true; },                  render: renderActivity }
        ];

        function render() {
            if (!$body) return;    // the host can hand us a record before init()

            // A detail sheet belongs to the entry that opened it, and that entry
            // belongs to the product being repainted away.
            closeDetail();
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
            // A THIRD line, for a row that carries two unrelated groups of facts
            // under its name — a price row states its figures and, separately,
            // the unit and attribute set those figures are for, and running the
            // two together on one line reads as one list of six things.
            if (opts.meta2) {
                $lhs.append($('<div class="vas_190-clM"></div>').text(opts.meta2).attr("title", opts.meta2));
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
                    // WHAT IS ON SCREEN on the left, the controls on the right.
                    // The three used to sit together in the middle of the panel,
                    // which said which page you were on but never how much there
                    // was — "1 of 3" leaves the reader to multiply.
                    $pager.append($('<span class="vas_190-pgRange"></span>').append(
                        $('<span></span>').text(msg("VAS_190_Showing", "Showing") + " "),
                        $('<b></b>').text((start + 1) + " – " + end),
                        $('<span></span>').text(" " + msg("VAS_190_Of", "of") + " "),
                        $('<b></b>').text(String(rows.length))));

                    var $ctl = $('<span class="vas_190-pgCtl"></span>');
                    $ctl.append(pagerButton("prev", page <= 0, function () {
                        pages[key] = page - 1; paint();
                    }));
                    $ctl.append($('<span class="vas_190-pgText"></span>').text(
                        (page + 1) + " " + msg("VAS_190_Of", "of") + " " + pageCount));
                    $ctl.append(pagerButton("next", page >= pageCount - 1, function () {
                        pages[key] = page + 1; paint();
                    }));
                    $pager.append($ctl);
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
        //
        // The stored code for "always mandatory" is 'Y', not 'A'
        // (MAttributeSet.MANDATORYTYPE_AlwaysMandatory). The map named 'A', so a
        // set that IS always mandatory matched nothing and the section summary
        // fell through to printing the raw code — the attribute group read
        // "Laptop Configuration · Y".
        var ATTR_SET_MANDATORY = {
            "N": { key: "VAS_190_SetNotMandatory",    text: "Not mandatory" },
            "Y": { key: "VAS_190_SetAlwaysMandatory", text: "Always mandatory" },
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
                openOrderCaption(s.ReservedOrderCount, true, s.ReservedDocumentNo,
                                 s.ReservedDateOrdered, s.ReservedDatePromised), true));
            $card.append(metricCell(msg("VAS_190_OnOrder", "On order"),
                qtyText(s.OnOrderQty, prec, uom),
                openOrderCaption(s.OnOrderCount, false, s.OnOrderDocumentNo,
                                 s.OnOrderDateOrdered, s.OnOrderDatePromised), true));
            $card.append(metricCell(msg("VAS_190_AvailableToPromise", "Available to promise"),
                qtyText(s.AvailableToPromise, prec, uom),
                msg("VAS_190_AtpFormula", "on hand − reserved")));
            $sec.append($card);
        }

        // What the figure above it came from: how many OPEN orders — not merely
        // completed ones, which said nothing about whether anything is still to
        // move — and, where it is a single order, WHICH order it is, when it was
        // raised and when it is due. Both figures answer on the same terms: the
        // count is stated for the purchase side as well as the sales side, and a
        // single order names itself under Reserved and under On order alike.
        //
        // With several orders in play none of the three describes the figure, so
        // none of them is shown — only the count.
        function openOrderCaption(count, isSales, documentNo, dateOrdered, datePromised) {
            var n = +count || 0;
            var noun = isSales
                ? (n === 1 ? msg("VAS_190_OpenSalesOrder", "open sales order")
                           : msg("VAS_190_OpenSalesOrders", "open sales orders"))
                : (n === 1 ? msg("VAS_190_OpenPurchaseOrder", "open purchase order")
                           : msg("VAS_190_OpenPurchaseOrders", "open purchase orders"));

            var bits = [n + " " + noun];
            if (n === 1) {
                var docNo = (documentNo === null || documentNo === undefined)
                    ? "" : String(documentNo).trim();
                var ordered = formatDate(dateOrdered);
                var due     = formatDate(datePromised);

                // WHICH order it is comes first. Its number is what a reader
                // looks the order up by; a date is not.
                if (docNo) bits.push(docNo);

                // An order raised and promised on the same day printed that day
                // twice, under two labels, which reads as the due date repeated —
                // and on a caption with no document number that was the whole of
                // it. The due date is the one that says something about the
                // figure, so it is the one kept.
                if (due) {
                    bits.push(msg("VAS_190_Due", "due") + " " + due);
                    if (ordered && ordered !== due) {
                        bits.push(msg("VAS_190_Dated", "dated") + " " + ordered);
                    }
                } else if (ordered) {
                    bits.push(msg("VAS_190_Dated", "dated") + " " + ordered);
                }
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

                // Line 1 — the price list, with the VERSION beside its name. The
                // version is what tells one row of a list from another, so it
                // belongs against the name and not at the tail of the meta line
                // where it sat, first candidate for clipping.
                var idBits = [];
                if (p.VersionName) idBits.push(p.VersionName);

                // Line 2 — WHEN this version takes effect, then the two figures
                // that stay on the left. Each price is named in full: "list",
                // "limit" and "std price" were abbreviations of the price list's
                // own field names, and a reader comparing rows had to know which
                // of the three shorthands meant what.
                //
                // The effective date leads the line rather than trailing the name
                // above it: it qualifies every figure that follows on this line,
                // and against the name it read as part of the version's title.
                var priceBits = [];
                var eff = formatDate(p.ValidFrom);
                if (eff) priceBits.push(msg("VAS_190_Effective", "effective") + " " + eff);
                priceBits.push(msg("VAS_190_ListPrice", "List Price") + " " +
                        formatAmount(p.PriceList, sym, p.CurPrecision));
                priceBits.push(msg("VAS_190_LimitPrice", "Limit Price") + " " +
                        formatAmount(p.PriceLimit, sym, p.CurPrecision));

                // Line 3 — what those figures are FOR. A price list holds one
                // price per unit and per attribute set instance, so these two are
                // what tells its rows apart; without them the rows read as one
                // price list repeated with different figures.
                var scopeBits = [];
                // A price row that names no unit of its own is stated in the
                // product's base unit, which is what the documents will use.
                var priceUom = p.UomName || data.Product.BaseUomName || "";
                if (priceUom)     scopeBits.push(msg("VAS_190_Per", "per") + " " + priceUom);
                if (p.Attributes) scopeBits.push(p.Attributes);

                return listRow({
                    primary: p.PriceListName || "—",
                    primarySoft: idBits.join(" · "),
                    meta: priceBits.join(" · "),
                    meta2: scopeBits.join(" · "),
                    // Every version the product is priced on is listed — a list
                    // can carry several and the section used to show one of them
                    // per list — so the row has to say which one is actually in
                    // force today. The rest are history or not yet effective.
                    chip: p.IsCurrentVersion
                        ? { text: msg("VAS_190_CurrentVersion", "Current"), tone: "ok" }
                        : { text: msg("VAS_190_OtherVersion", "Other version"), tone: "neutral" },
                    // The STANDARD PRICE is the row's value, beside that chip and
                    // in the bold slot: it is the price the documents will use,
                    // and the one figure a reader scanning the section compares
                    // across rows. The other two stay on the meta line, where they
                    // read as the band this one sits in.
                    value: formatAmount(p.PriceStd, sym, p.CurPrecision),
                    valueSub: msg("VAS_190_StdPrice", "Standard Price")
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

            // The RIGHT-hand slot. A where-used row leads it with how much of this
            // product one parent takes; both kinds then state the ATTRIBUTE SET
            // the BOM is specified for.
            //
            // The attribute set lives on the BOM DETAIL line, so an own BOM — a
            // header row — never reported one at all, and a where-used row buried
            // its own at the tail of the meta line among the dates. A BOM built
            // for a particular attribute set is a different BOM, which is exactly
            // what a reader checks on this row, so it is stated where the row's
            // other facts about itself are stated.
            var hasQty = (b.Kind === "usedin");
            if (hasQty || b.Attributes) {
                var $val = $('<div class="vas_190-eVal"></div>');
                // An attribute set runs to a sentence where a quantity is three
                // characters, so a slot carrying one is allowed to shrink.
                if (b.Attributes) $val.addClass("vas_190-eVal-attr");
                if (hasQty) {
                    $val.append($('<div class="vas_190-eV"></div>')
                        .text("× " + formatNumber(+b.QtyPerParent || 0, 2)));
                    $val.append($('<div class="vas_190-eS"></div>')
                        .text(msg("VAS_190_PerUnit", "per unit")));
                }
                if (b.Attributes) {
                    // Labelled, because on an own BOM it is the only thing in the
                    // slot and an unlabelled attribute set there reads as a code.
                    $val.append($('<div class="vas_190-eS"></div>')
                        .text(msg("VAS_190_AttributeSet", "Attribute set"))
                        .attr("title", b.Attributes));
                    $val.append($('<div class="vas_190-eV"></div>')
                        .text(b.Attributes).attr("title", b.Attributes));
                }
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

        // What KIND of confirmation the check was raised on, for the side that
        // cannot say so itself. A receipt confirmation carries its own type
        // (M_InOutConfirm.ConfirmType) and the server sends the dictionary's name
        // for it; a transfer confirmation has no type column at all, so the panel
        // names the document.
        var QC_SOURCE = {
            "RECEIPT":  { key: "VAS_190_QcOnReceipt",  text: "Ship / receipt confirmation" },
            "MOVEMENT": { key: "VAS_190_QcOnMovement", text: "Material transfer confirmation" }
        };

        // The latest quality CHECK — the confirmation it was raised on, and
        // nothing else. It is ONE row: the confirmation's number, what kind of
        // document it is, who the sales representative was and how much is to be
        // verified, with the date on the right; the row opens the confirmation.
        //
        // The per-parameter RESULT rows that used to follow are gone. They
        // repeated the parameter names listed under this card with a second
        // reading beside them, so the section said everything twice and pushed the
        // configured parameters — the thing the section is named after — off the
        // panel. Whether every parameter has been read is still stated, by the
        // Checked / Pending chip beside the heading.
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

            var docBits = [];
            // The document's own TYPE where it has one, else the kind of
            // confirmation this is. Both answer "what document is this"; only the
            // first is the tenant's own word for it.
            var src = QC_SOURCE[check.Source];
            if (check.DocTypeName)  docBits.push(check.DocTypeName);
            else if (src)           docBits.push(msg(src.key, src.text));
            // The SALES REPRESENTATIVE. A transfer has none — it moves stock
            // between the tenant's own warehouses — so the bit is simply absent
            // there rather than standing empty.
            if (check.SalesRepName) docBits.push(check.SalesRepName);
            if (check.QtyToVerify !== null && check.QtyToVerify !== undefined
                && +check.QtyToVerify !== 0) {
                docBits.push(formatNumber(check.QtyToVerify, 2) + " " +
                             msg("VAS_190_QcToVerify", "to verify"));
            }

            // The CONFIRMATION's number, and the row opens the confirmation — not
            // the receipt or transfer behind it, which is a different document and
            // does not carry the check.
            $list.append(listRow({
                primary: check.DocumentNo || msg("VAS_190_QcNoDocument", "(no document)"),
                meta: docBits.join(" · "),
                value: formatDate(check.CheckDate),
                openTable: check.DocTableName,
                openId: check.DocRecordId,
                openSOTrx: check.DocIsSOTrx,
                // The Ship/GRN or Material Transfer confirmation screen, by NAME.
                // The dictionary's zoom target for these two tables is not one the
                // reader's role may open, so the click raised an access error
                // instead of showing the confirmation the check was recorded on.
                openWindows: check.DocWindowName ? [check.DocWindowName] : null
            }));
            return $card;
        }

        // ----------------------------------------------------------------- //
        //  10. Supplier information                                          //
        // ----------------------------------------------------------------- //

        // A vendor row leads with WHICH vendor this is to the reader, and says so
        // in LABELS rather than in prose: PREFERRED (the vendor-product record's
        // own flag) and LAST USED (the most recent purchase) are both chips, and
        // a row carrying either is not an alternative to anything — "Alternative"
        // is what is left when a row claims neither. It used to be the blanket
        // opposite of "Last used", so the preferred vendor read "Alternative"
        // with the word "preferred vendor" buried in its detail line.
        function renderSuppliers() {
            var rows = data.Suppliers;

            // An alternative is a vendor that is neither preferred nor the one
            // last bought from — the same test the row's own chips make, so the
            // header and the rows cannot disagree.
            var alternatives = 0;
            for (var n = 0; n < rows.length; n++) {
                if (!rows[n].IsCurrentVendor && !rows[n].IsLastUsed) alternatives++;
            }
            var summary = rows.length + " " + (rows.length === 1
                ? msg("VAS_190_Vendor", "vendor")
                : msg("VAS_190_Vendors", "vendors"));
            if (alternatives > 0) {
                summary += " · " + alternatives + " " +
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
                // The "not on vendor tab" note is gone. It explained why the terms
                // above it were absent, but it explained it to a reader who had
                // not asked: the row is here to say who the product was last
                // bought from, and where the vendor is recorded is a fact about
                // the Vendor tab rather than about this purchase.

                // Preferred and last used are both labels, and each is its own
                // statement: the first is the vendor-product record's flag, the
                // second is what the purchase history says. A row with either is
                // never also called an alternative.
                var chips = [];
                if (v.IsCurrentVendor) {
                    chips.push({ text: msg("VAS_190_PreferredVendor", "Preferred vendor"),
                                 tone: "info" });
                }
                if (v.IsLastUsed) {
                    chips.push({ text: msg("VAS_190_LastUsed", "Last used"), tone: "ok" });
                }
                if (!chips.length) {
                    chips.push({ text: msg("VAS_190_AlternativeVendor", "Alternative"),
                                 tone: "neutral" });
                }

                // The unit the last price is stated in — the PURCHASE ORDER's own
                // unit, which is what the reader sees on the document and what
                // they are comparing vendors on.
                //
                // It used to be the product's base unit, because the figure was
                // PriceActual and that column is always base-unit whatever unit
                // the line was written in. The server now sends the line's ENTERED
                // price with the line's own unit beside it, so the two agree; the
                // base unit is named only where the line carries neither and the
                // base-unit figure is what came back.
                var priceUom = v.LastOrderUomName || data.Product.BaseUomName || "";
                var priceSub = msg("VAS_190_LastPrice", "last price");
                if (priceUom) priceSub += " " + msg("VAS_190_Per", "per") + " " + priceUom;

                return listRow({
                    primary: v.VendorName || "—",
                    meta: detail.join(" · "),
                    chips: chips,
                    // The PRICE the product was last bought at, which is what the
                    // reader compares vendors on. The vendor's own catalogue
                    // number was here and told them nothing about this vendor.
                    value: lastPrice === null ? "" : formatAmount(lastPrice, sym, v.CurPrecision),
                    valueSub: lastPrice === null ? "" : priceSub,
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
            "W-": { icon: "move",      tone: "warn", key: "VAS_190_MvWorkOrderOut",     text: "Work order issue" },
            // Two types with no sign, because neither moves stock — both restate
            // what the stock on hand is WORTH. They were falling through to the
            // unmapped fallback and reading "Stock movement", which is the one
            // thing they are not.
            "IR": { icon: "move",      tone: "info", key: "VAS_190_MvRevaluation",      text: "Inventory revaluation" },
            "VI": { icon: "move",      tone: "info", key: "VAS_190_MvInvoiceCost",      text: "Invoice cost adjustment" }
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

            // Three columns. The money one is gone: a movement is a quantity
            // leaving or arriving, and what it was VALUED at is an accounting
            // question this section is not the place to answer — the Accounting
            // details section below states the costing method the product is
            // valued under. The column also read empty on every movement with no
            // cost detail recorded against its line, which is most of them on a
            // tenant that has not run costing.
            var $grid = dataGrid("colsTx", [
                { label: msg("VAS_190_Document", "Document") },
                { label: msg("VAS_190_Date", "Date") },
                { label: msg("VAS_190_Qty", "Qty"), align: "r" }
            ]);
            $sec.append($grid);

            paginate($sec, "tx", rows, ROWS_PER_PAGE, function (t) {
                // An unmapped movement type gets a named fallback rather than its
                // stored code — "M?" tells a reader nothing.
                var mv = MOVEMENT[t.MovementType] ||
                         { icon: "move", tone: "warn", key: "VAS_190_MvOther", text: "Stock movement" };
                var mvName = msg(mv.key, mv.text);

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

                // Document TYPE in front of the number, on every row. The
                // tenant's own C_DocType name is used, so a renamed document type
                // reads as it does everywhere else in the application.
                //
                // Where the document carries no type at all — a production or
                // job-work document whose table has no C_DocType_ID — the
                // MOVEMENT's own name stands in as the type. The column used to
                // print a bare number in that case, which said what the document
                // was called but not what it was.
                var docType = t.DocTypeName || mvName;
                var docText = t.DocumentNo ? docType + " · " + t.DocumentNo : docType;

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

        // "Related To Product · Variance Type Purchase", from the accounting
        // default record behind the account. Empty on the classic scheme, whose
        // account is a column and has no such record — and on a row whose fields
        // are all unset, which is not the same as the section having failed.
        //
        // 'Y' and 'N' are the one thing the server leaves as stored: they are a
        // yes-no field's value, not a code with a reference list behind it, and
        // the words for them belong in the reader's language on this side.
        function accountDetailText(a) {
            var details = (a && a.Details) || [];
            var bits = [];
            for (var i = 0; i < details.length; i++) {
                var d = details[i];
                if (!d || !d.Label) continue;
                var value = (d.Value === null || d.Value === undefined) ? "" : String(d.Value);
                if (value === "Y") value = msg("VAS_190_Yes", "Yes");
                else if (value === "N") value = msg("VAS_190_No", "No");
                if (!value) continue;
                bits.push(d.Label + " " + value);
            }
            return bits.join(" · ");
        }

        function renderAccounting() {
            var acct = data.Accounting;
            var summaryBits = [];
            // Which schema answered is part of the answer — a tenant with more
            // than one posts different accounts under each.
            if (acct.SchemaName)    summaryBits.push(acct.SchemaName);
            // The costing method by NAME. The stored code reached the screen here
            // — "S", which is the dictionary's shorthand for Standard Costing and
            // not a word anybody outside the accounting tables reads — and the
            // code stands in only where the reference resolves to nothing.
            var costing = acct.CostingMethodName || acct.CostingMethod;
            if (costing)            summaryBits.push(costing);
            if (acct.CurrencyISO)   summaryBits.push(acct.CurrencyISO);

            var $sec = section(msg("VAS_190_AccountingDetails", "Accounting details"),
                               summaryBits.join(" · "));

            var $list = $('<div class="vas_190-clist"></div>');
            $sec.append($list);

            var rows = acct.Rows || [];

            // A product that sets no account of its own says so, under the schema
            // and costing method it is still valued by. The section used not to be
            // drawn at all in that case, which reads as the panel having failed
            // rather than as there being nothing set.
            if (!rows.length) {
                $list.append(listRow({
                    primary: msg("VAS_190_NoAccountsSet", "No accounts set on this product"),
                    meta: msg("VAS_190_AccountsFromCategory",
                              "Postings fall back to the product category's accounts"),
                    value: ""
                }));
                return;
            }

            // Five to a page. A tenant running the FRPT scheme sets an account per
            // ROLE — a dozen and more on a product that is fully configured — and
            // the section listed every one of them, pushing the Activity feed
            // below it off the panel. Every other list section here pages at five
            // and this one now does too.
            paginate($sec, "accounting", rows, ACCOUNT_ROWS_PER_PAGE, function (a) {
                var role = ACCOUNT_ROLE[a.AccountRole];
                // Every row here is an account set on the PRODUCT's own accounting
                // tab. Nothing is inherited from the product category any more, so
                // there is no "from category" qualifier to print — what the panel
                // shows is what that tab holds.
                //
                // The account's NAME leads on the left and its combination is the
                // row's value on the right; both are the bold slots. Between them,
                // under the name, are the accounting default's own fields — what
                // the accounting defaults screen states against this account, and
                // what tells two accounts of a similar name apart. The
                // combination's description keeps its own line beneath those: it
                // describes the ACCOUNT, not the default, and running the two
                // together read as one list.
                return listRow({
                    primary: role ? msg(role.key, role.text) : a.AccountRole,
                    meta: accountDetailText(a),
                    meta2: a.Description || "",
                    value: a.Combination || "—"
                });
            }, $list);
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
            // Somebody typing on the record. It is a CM_ChatEntry underneath, but
            // "Chat" named the plumbing rather than the thing: what a reader wrote
            // on a product is a note, and that is what the rest of the application
            // calls it. Still its own source, separate from the system-raised
            // AD_Note above.
            // Read through a key of its OWN — not VAS_190_TagChat. "Chat" named
            // the plumbing (it is a CM_ChatEntry underneath) rather than the
            // thing: what somebody types on a product is a NOTE, and that is what
            // the rest of the application calls it. The old key was already
            // defaulted to "Note" here, but a tenant that had SEEDED
            // VAS_190_TagChat as "Chat" kept seeing "Chat" — a seeded message
            // beats the default, and the panel had no way to say otherwise. A key
            // nobody has seeded cannot be overridden by the old wording, and a
            // tenant that wants its own word seeds this one.
            "chat":        { tone: "info",    icon: "chat",     key: "VAS_190_TagChatNote",    text: "Note" },
            // MailAttachment1 with AttachmentType 'I' — an attached LETTER
            // document, which is how VAS_105, VAS_123 and the shared activity
            // sources have always read that value. A document icon rather than an
            // envelope, and no direction anywhere on it: a letter filed against
            // the product neither went out nor came in, it is simply there.
            "letter":      { tone: "purple",  icon: "doc",      key: "VAS_190_TagLetter",      text: "Letter" },
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
        // The entry types that OPEN, and the order the feed hands them to the
        // detail sheet. A field edit and a workflow step state everything they
        // have on the row itself, so neither opens onto anything.
        function activityOpens(a) {
            return a.Type === "appointment" || a.Type === "task" || a.Type === "chat"
                || a.Type === "mail" || a.Type === "letter";
        }

        // NO TOOLTIPS anywhere in this function or the rows it builds. The feed's
        // text is not abridged any more — what a row cannot fit, the detail sheet
        // holds — so a tooltip repeating the line under the cursor was noise that
        // followed the pointer down the whole section.
        function buildActivityEntry(a, index) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES["note"];
            // Only a MAIL names correspondents. A letter is an attached document
            // filed against the product — it has no To, no Cc and no direction —
            // and listing the mail address columns under it described a message
            // that was never sent.
            var isMail = (a.Type === "mail");
            // A task carries its own tone: closed reads as done, open as pending.
            var tone = (a.Type === "task" && a.IsClosed) ? "ok" : meta.tone;

            var $item = $('<div class="vas_190-actItem"></div>');
            var $row = $('<div class="vas_190-actRow"></div>');

            var $tag = $('<span class="vas_190-actTag"></span>').addClass("vas_190-tone-" + tone);
            if (meta.icon) $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.key, meta.text)));
            $row.append($tag);

            var $title = $('<span class="vas_190-actTitle"></span>');
            var $lead = $('<span class="vas_190-actLead"></span>');
            // A task's PRIORITY leads the headline, in the colour the task screen
            // gives it — it is the first thing a reader sorts on and it was not on
            // the row at all.
            if (a.Type === "task" && a.PriorityName) {
                $lead.append(priorityChip(a));
            }
            $lead.append($('<span></span>').text(activityTitle(a)));
            $title.append($lead);

            // A mail names its correspondents under the subject — every address on
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
                if (sub) $title.append($('<small class="vas_190-actSub"></small>').text(sub));
            }
            $row.append($title);

            var when = formatDateTime(a.EventDate) || "";
            if (a.Actor) when += (when ? " · " : "") + a.Actor;
            if (when) $row.append($('<span class="vas_190-actWhen"></span>').text(when));

            // The whole row opens its own detail sheet. It used to fold a drawer
            // open underneath itself, which could only ever hold the message body
            // — an appointment's people, its meeting link and its transcript, and
            // a task's assignee and result, had nowhere to go.
            if (activityOpens(a)) {
                $row.addClass("vas_190-is-openable")
                    .attr("role", "button")
                    .attr("tabindex", "0")
                    .attr("data-act-index", index);
                $row.append($('<span class="vas_190-actCaret"></span>').append(svgIcon("chevRight")));
            }

            $item.append($row);
            return $item;
        }

        // The priority badge on a task row: the dictionary's own word for the
        // code, in the colour the task screens use for it.
        //
        // A CLOSED task is painted in the resolved colour whatever it was raised
        // at. Its priority is a record of how urgent it WAS, and leaving a
        // finished task in red kept it competing for attention with the ones
        // still to be done — the same rule, and the same four colours, as the
        // account panel's task list.
        function priorityChip(a) {
            return $('<span class="vas_190-prio"></span>')
                .addClass("vas_190-prio-" + (a.IsClosed ? "resolved" : priorityTone(a.PriorityCode)))
                .text(a.PriorityName);
        }

        // AppointmentsInfo.PriorityKey — '1' high, '2' medium, anything else low.
        // The same three the account panel's task list paints.
        function priorityTone(code) {
            var c = String(code === null || code === undefined ? "" : code).toLowerCase();
            if (c === "1" || c === "high")   return "high";
            if (c === "2" || c === "medium") return "medium";
            return "low";
        }

        // ----------------------------------------------------------------- //
        //  Activity detail sheet                                             //
        // ----------------------------------------------------------------- //

        // The sheet currently open, so a second open replaces it and a product
        // change closes it.
        var $sheet = null;

        function closeDetail() {
            if (!$sheet) return;
            $sheet.remove();
            $sheet = null;
        }

        // Opens one activity entry over the panel: a labelled header, the fields
        // that entry actually has, its content, and the actions it offers.
        //
        // The shape is the engagement view on the customer master — label and
        // subject at the top, then type / when / detail / people, then the
        // content, then the actions — so a reader who knows one knows the other.
        // The platform's own task form, opened on the task the row reports —
        // WSP.EditTaskForm, the same call VAS_105 and VAS_123 make from their
        // task rows.
        //
        // A task is the one entry in this feed that a reader opens in order to DO
        // something: reassign it, move its due date, tick it off. This panel's
        // detail sheet is read-only, so it could show all of that and let them
        // change none of it — and the form they actually wanted was two screens
        // away. Appointments, mails and letters keep the sheet: there is no
        // editor behind them that this panel could sensibly hand over to.
        //
        // The parent pair is (AD_Table_ID, Record_ID) = this window's table and
        // the product on screen, exactly as VAS_105 passes its business partner
        // and VAS_123 its order. The busy element is the platform's own — the
        // form takes ownership of it and removes it when it has painted.
        //
        // Returns false where the form is not loaded on this page, and the caller
        // then falls back to the sheet: a click that does nothing at all is worse
        // than a click that shows what the panel already knows.
        function openTaskForm(a) {
            var taskId = +(a && a.Id) || 0;
            if (taskId <= 0) return false;

            // VIS.AppointmentsForm is the entry point this application actually
            // has. WSP.EditTaskForm — the name VAS_105 and VAS_123 call on their
            // task rows — is in none of the framework bundles, so that call has
            // always resolved to undefined and their task clicks do nothing here.
            //
            // The trade this makes is worth stating: VIS.AppointmentsForm is the
            // CREATE entry point. Its sixth argument is a boolean (the VIS
            // toolbar's own cmd_appointment passes `true` there), not a record
            // id, so it opens the task form ON THIS PRODUCT rather than loaded
            // with the task that was clicked. Targeting the clicked task needs
            // WSP.WSP_AppointmentsForm, which this installation does not expose.
            if (!window.VIS || !VIS.AppointmentsForm
                || typeof VIS.AppointmentsForm.init !== "function") {
                console.log("VAS_190: VIS.AppointmentsForm is not available on this "
                          + "window; task " + taskId + " opens the read-only detail.");
                return false;
            }

            // WSP IS CHECKED HERE, BEFORE THE CALL, and this is not belt-and-braces.
            //
            // VIS.AppointmentsForm.init is a thin wrapper whose entire body is
            // `if (window.WSP) { …open the form… } else alert("please download
            // WSP !!!")`. It RETURNS NORMALLY in the second case — nothing
            // throws — so calling it on an installation without WSP would put a
            // browser alert in front of the reader, and the catch below would
            // never run, so the panel would report success and show nothing.
            // The reader would have clicked a task and been handed an alert
            // about a module they cannot install.
            //
            // Answering the question ourselves keeps that alert off the screen
            // and returns false, which is what puts the read-only detail sheet
            // back — the behaviour the panel had before it ever tried to open a
            // form, and the right answer where no form exists to open.
            if (!window.WSP) {
                console.log("VAS_190: the WSP module is not installed, so there is "
                          + "no task form to open. Task " + taskId + " falls back "
                          + "to the read-only detail.");
                return false;
            }

            var tableId = $self.table_ID || 0;
            var userId = 0, userName = "";
            try {
                if (VIS.context && typeof VIS.context.getAD_User_ID === "function") {
                    userId = VIS.context.getAD_User_ID();
                }
                if (VIS.context && typeof VIS.context.getAD_UserName === "function") {
                    userName = VIS.context.getAD_UserName();
                }
            } catch (e) { }

            // The form reaches for this global on its way up and throws where it
            // is undefined. VAS_105 and VAS_123 both define it, but only on their
            // NEW-task button; their edit path then works because that button has
            // already run at some point in the session and left the global
            // behind. This panel raises no tasks of its own, so nothing here ever
            // defined it. An empty jQuery set, exactly as those two use, and only
            // when it is missing.
            try {
                if (typeof window.$backBtn_ID === "undefined") window.$backBtn_ID = $();
            } catch (e3) { }

            try {
                // Five arguments, exactly as VAS_105, VAS_123 and VAS_120 call it,
                // with isTask = true so the wrapper routes to the TASK form rather
                // than the appointment one. No busy overlay is built here —
                // VIS.AppointmentsForm creates #divAptBusy itself and hands it to
                // the form, and a second one would sit on the page for ever.
                VIS.AppointmentsForm.init(tableId, shownRecordId, userId, userName, true);
            } catch (err) {
                console.log("VAS_190: VIS.AppointmentsForm.init failed for task "
                          + taskId + " — falling back to the detail sheet.", err);
                return false;
            }
            // Whatever the reader changes in there is an activity change. The
            // watcher normally hears the form's own save request, but not always
            // — hence the close watch below.
            watchTaskFormClose();
            return true;
        }


        // Refreshes the feed when the task popup goes away.
        //
        // The AJAX nudge is the usual route and it covers the ordinary case. It
        // does not cover the one VAS_105 documents: wsptask.js can throw inside
        // its own success callback, which aborts jQuery's chain — the task IS
        // saved but `ajaxComplete` never fires, so nothing nudges and the change
        // waits for the twenty-second backstop. Watching for the popup's removal
        // catches that, and it costs one observer that disconnects itself.
        //
        // Only the CLOSE is watched, not the save: whether the reader changed
        // anything is the signature's question, and asking it once on close is
        // cheaper than trying to work out the answer from the DOM.
        //
        // BEST EFFORT, and deliberately so. The popup's own markup belongs to the
        // WSP module, which is not part of this solution and cannot be read from
        // here; the ids below are the ones VAS_105 and VAS_123 watch for, and
        // they may not be what this installation's form actually renders. Nothing
        // depends on it — the AJAX nudge catches the save either way, and this
        // only closes the gap where the form's own request never fires one.
        function watchTaskFormClose() {
            if (typeof MutationObserver !== "function") return;

            var done = false;
            var obs = null;
            var giveUp = null;

            function finish(nudge) {
                if (done) return;
                done = true;
                if (obs) { try { obs.disconnect(); } catch (e) { } obs = null; }
                if (giveUp) { clearTimeout(giveUp); giveUp = null; }
                if (nudge) nudgeActivityCheck();
            }

            // Is this removed node the task popup, or does it contain it?
            function isTaskForm(node) {
                if (!node || node.nodeType !== 1) return false;
                if (node.id === "divTaskContinerFrom") return true;
                try {
                    if (node.className && String(node.className).indexOf("wsp-task-form") >= 0) {
                        return true;
                    }
                    if (node.querySelector) {
                        return !!node.querySelector("#divTaskContinerFrom, .wsp-task-form");
                    }
                } catch (e) { }
                return false;
            }

            obs = new MutationObserver(function (mutations) {
                for (var i = 0; i < mutations.length; i++) {
                    var removed = mutations[i].removedNodes;
                    for (var j = 0; j < removed.length; j++) {
                        if (isTaskForm(removed[j])) { finish(true); return; }
                    }
                }
            });
            try {
                obs.observe(document.body, { childList: true, subtree: true });
            } catch (e) {
                finish(false);
                return;
            }

            // A reader who leaves the form open all afternoon must not leave an
            // observer on the document with them.
            giveUp = setTimeout(function () { finish(false); }, 300000);
        }

        function openActivityDetail(index) {
            var rows = (data && data.Activity) || [];
            if (isNaN(index) || index < 0 || index >= rows.length) return;

            var a = rows[index];
            // Any sheet already open goes first, whichever way this one opens —
            // the task form is a page of its own and must not appear behind this
            // panel's overlay.
            closeDetail();
            // A task hands over to the platform form instead of opening the
            // read-only sheet; anything else, and a page with no task form on it,
            // carries on into the sheet below.
            if (a.Type === "task" && openTaskForm(a)) return;

            var meta = ACT_TYPES[a.Type] || ACT_TYPES["note"];
            var tone = (a.Type === "task" && a.IsClosed) ? "ok" : meta.tone;

            $sheet = $('<div class="vas_190-sheet" role="dialog" aria-modal="true"></div>');

            // ----- Header: what kind of thing this is, then what it is about ---
            var $head = $('<div class="vas_190-sheetHead"></div>');
            var $tag = $('<span class="vas_190-actTag"></span>').addClass("vas_190-tone-" + tone);
            if (meta.icon) $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.key, meta.text)));
            $head.append($tag);
            $head.append($('<span class="vas_190-sheetTitle"></span>').text(activityTitle(a)));
            // NO close cross here. The sheet offers exactly ONE way out, the
            // Close button in its footer: a header cross beside it gave every
            // entry two controls that did the same thing, and a reader deciding
            // between two identical actions is a reader who has been given a
            // choice that is not one. The footer is where the sheet's other
            // actions live, so that is where leaving it belongs.
            $sheet.append($head);

            // ----- Body: the fields, then the content -----
            var $sBody = $('<div class="vas_190-sheetBody"></div>');
            var fields = detailFields(a);
            for (var i = 0; i < fields.length; i++) {
                $sBody.append(detailRow(fields[i]));
            }

            var content = detailContent(a);
            if (content.html) {
                // The message AS IT WAS WRITTEN — paragraphs, tables, lists and
                // links, the way VAS_105's e-mail detail shows one.
                //
                // This is the only place in the panel that hands a string to the
                // browser as MARKUP instead of escaping it, and the string's
                // author is whoever sent the mail. What makes that acceptable is
                // upstream: the server sends BodyHtml through a whitelist —
                // scripts, handlers, forms, frames and executable URLs are gone
                // before it leaves — and sends an empty string when it cannot
                // vouch for the result, which is why this branch is a strict
                // `if` with the text one below it. Never assign a.Body or any
                // other server string this way.
                $sBody.append($('<div class="vas_190-sheetLabel"></div>').text(content.label));
                $sBody.append($('<div class="vas_190-sheetText vas_190-sheetHtml"></div>')
                    .html(content.html));
            } else if (content.text) {
                $sBody.append($('<div class="vas_190-sheetLabel"></div>').text(content.label));
                $sBody.append($('<div class="vas_190-sheetText"></div>').text(content.text));
            }

            // The e-mails sent against a task or appointment keep their own block:
            // each is a message in its own right, not a field of the meeting.
            var mails = activityMails(a);
            if (mails.length) {
                $sBody.append($('<div class="vas_190-sheetLabel"></div>')
                    .text(msg("VAS_190_Emails", "emails")));
                $sBody.append(buildApptMailBlock(a).show().removeClass("vas_190-actBody"));
            }
            $sheet.append($sBody);

            // ----- Footer: close, and whatever this kind of entry can do -----
            var $foot = $('<div class="vas_190-sheetFoot"></div>');

            // A recorded meeting's transcript is offered as a FILE. It runs to
            // pages, so putting it on screen would bury everything above it.
            if (a.Transcript && String(a.Transcript).trim()) {
                $foot.append(sheetButton(msg("VAS_190_DownloadTranscript", "Download transcript"),
                    false, function () { downloadTranscript(a); }));
            }
            // Replying is only offered where there is somebody to reply TO.
            if (a.Type === "mail" && replyAddress(a)) {
                $foot.append(sheetButton(msg("VAS_190_Reply", "Reply"), true,
                    function () { replyToMail(a); }));
            }
            var $close = sheetButton(msg("VAS_190_Close", "Close"), false, closeDetail);
            $foot.append($close);
            $sheet.append($foot);

            $root.append($sheet);
            // Focus lands on the one control that closes the sheet, so Escape's
            // job is done by the key the keyboard reader already has under a
            // finger. It was the header cross, which is gone.
            try { $close.focus(); } catch (e) { }
        }

        function sheetButton(text, primary, handler) {
            var $b = $('<button type="button" class="vas_190-sheetBtn"></button>').text(text);
            if (primary) $b.addClass("vas_190-sheetBtn--primary");
            $b.on("click", handler);
            return $b;
        }

        function detailRow(field) {
            var $r = $('<div class="vas_190-sheetRow"></div>');
            $r.append($('<span class="vas_190-sheetK"></span>').text(field.label));

            var $v = $('<span class="vas_190-sheetV"></span>');
            if (field.href) {
                $v.append($('<a target="_blank" rel="noopener noreferrer"></a>')
                    .attr("href", field.href).text(field.value));
            } else {
                $v.text(field.value);
            }
            $r.append($v);
            return $r;
        }

        function pushField(fields, label, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            fields.push({ label: label, value: text });
        }

        // The labelled fields one entry carries. Only what the record actually
        // holds is listed — an absent field is left out rather than shown empty,
        // so the sheet is never a form with blanks in it.
        function detailFields(a) {
            var fields = [];
            var typeMeta = ACT_TYPES[a.Type] || ACT_TYPES["note"];

            if (a.Type === "appointment" || a.Type === "task") {
                // The category the engagement was filed under is its type; the
                // kind of entry stands in where the record names none.
                pushField(fields, msg("VAS_190_DetailType", "Type"),
                          a.CategoryName || msg(typeMeta.key, typeMeta.text));
                pushField(fields, msg("VAS_190_DetailWhen", "When"), meetingWhen(a));

                if (a.Type === "task") {
                    pushField(fields, msg("VAS_190_AssignedTo", "Assigned to"), a.AssigneeName);
                    pushField(fields, msg("VAS_190_DueOn", "Due on"), formatDate(a.EndDate));
                    pushField(fields, msg("VAS_190_Priority", "Priority"), a.PriorityName);
                    if (a.PercentComplete !== null && a.PercentComplete !== undefined) {
                        pushField(fields, msg("VAS_190_Status", "Status"), a.PercentComplete + "%");
                    }
                    pushField(fields, msg("VAS_190_TaskState", "State"),
                              a.IsClosed ? msg("VAS_190_TaskCompleted", "Completed")
                                         : msg("VAS_190_TaskOpen", "Open"));
                    pushField(fields, msg("VAS_190_TaskResult", "Result"), a.TaskResult);
                } else if (a.IsCancelled) {
                    pushField(fields, msg("VAS_190_TaskState", "State"),
                              msg("VAS_190_Cancelled", "Cancelled"));
                }

                pushField(fields, msg("VAS_190_DetailDetail", "Detail"), a.Location);
                pushField(fields, msg("VAS_190_DetailPeople", "People"), a.People);

                if (a.MeetingUrl && String(a.MeetingUrl).trim()) {
                    fields.push({
                        label: msg("VAS_190_MeetingUrl", "Meeting URL"),
                        value: String(a.MeetingUrl).trim(),
                        href:  String(a.MeetingUrl).trim()
                    });
                }
                pushField(fields, msg("VAS_190_UrlDescription", "URL description"), a.UrlDescription);
                pushField(fields, msg("VAS_190_Comment", "Comment"), a.Comments);
                pushField(fields, msg("VAS_190_RaisedBy", "By"), a.Actor);
                return fields;
            }

            if (a.Type === "mail") {
                pushField(fields, msg("VAS_190_DetailType", "Type"), msg(typeMeta.key, typeMeta.text));
                pushField(fields, msg("VAS_190_DetailDirection", "Direction"),
                          a.IsReceived ? msg("VAS_190_MailReceived", "Received")
                                       : msg("VAS_190_MailSent", "Sent"));
                pushField(fields, msg("VAS_190_DetailWhen", "When"), formatDateTime(a.EventDate));
                pushField(fields, msg("VAS_190_From", "From"), a.MailFrom);
                pushField(fields, msg("VAS_190_To", "To"), a.MailTo);
                pushField(fields, msg("VAS_190_Cc", "Cc"), a.MailCc);
                pushField(fields, msg("VAS_190_Bcc", "Bcc"), a.MailBcc);
                pushField(fields, msg("VAS_190_DetailPeople", "People"), a.Actor);
                return fields;
            }

            if (a.Type === "letter") {
                pushField(fields, msg("VAS_190_DetailType", "Type"), msg(typeMeta.key, typeMeta.text));
                pushField(fields, msg("VAS_190_DetailWhen", "When"), formatDateTime(a.EventDate));
                pushField(fields, msg("VAS_190_RaisedBy", "By"), a.Actor);
                return fields;
            }

            // A note: what it is, when it was written and who wrote it.
            pushField(fields, msg("VAS_190_DetailType", "Type"), msg(typeMeta.key, typeMeta.text));
            pushField(fields, msg("VAS_190_DetailWhen", "When"), formatDateTime(a.EventDate));
            pushField(fields, msg("VAS_190_RaisedBy", "By"), a.Actor);
            return fields;
        }

        // The block of prose under the fields, labelled for what it is.
        //
        // `html` is only ever set from BodyHtml, which the server sanitises; every
        // other body on this feed is stored HTML-ENCODED by the CRM screens that
        // wrote it and is text by the time it arrives, so it stays on `text`.
        function detailContent(a) {
            if (a.Type === "chat") {
                return { label: msg("VAS_190_DetailContent", "Content"), text: a.Body || a.Title };
            }
            if (a.Type === "appointment" || a.Type === "task") {
                return { label: msg("VAS_190_DetailDetail", "Detail"), text: a.Body };
            }
            // A mail or a letter: the formatted message where the server could
            // vouch for one, the flattened text otherwise — a plain-text mail has
            // no markup to show, and neither has one whose markup sanitised away.
            return {
                label: msg("VAS_190_DetailContent", "Content"),
                html: (a.Type === "mail" || a.Type === "letter") ? (a.BodyHtml || "") : "",
                text: a.Body
            };
        }

        // "12 Aug 2026 09:00 – 10:30", or just the start where there is no end.
        function meetingWhen(a) {
            var from = formatDateTime(a.StartDate);
            var to   = formatDateTime(a.EndDate);
            if (from && to) return from + " – " + to;
            return from || to || formatDateTime(a.EventDate) || "";
        }

        // The address a reply goes to: whoever sent an inbound mail, else whoever
        // the outbound one went to.
        function replyAddress(a) {
            var addr = a.IsReceived ? a.MailFrom : a.MailTo;
            return (addr === null || addr === undefined) ? "" : String(addr).trim();
        }

        // Opens the APPLICATION's mail composer on a reply — VIS.Email inside a
        // VIS.CFrame, which is what VAS_105 does from its own e-mail detail.
        //
        // It used to hand the reply to `mailto:`, which is a different thing
        // wearing the same word. That leaves the application: it opens whatever
        // the workstation has configured, composes outside the tenant, sends from
        // the reader's personal account and files NOTHING back against the
        // product — so the reply never appeared in the feed it was sent from, and
        // on a workstation with no mail client registered the button did nothing
        // at all. The platform composer sends through the tenant's mail server and
        // records the message against (AD_Table_ID, Record_ID), which is what puts
        // it back on this feed.
        //
        // The quoted body is ESCAPED on the way in. The composer takes HTML, and
        // the body this panel holds is plain text the server already flattened —
        // pasting it in raw would have the original message's own characters read
        // as markup.
        function replyToMail(a) {
            var to = replyAddress(a);
            if (!to) return;

            var subject = (a.Title || "").trim();
            if (subject && subject.toLowerCase().indexOf("re:") !== 0) subject = "RE: " + subject;

            if (window.VIS && typeof VIS.Email === "function" && typeof VIS.CFrame === "function") {
                closeDetail();
                try {
                    // The original quoted under the reply, formatted where the
                    // server vouched for its markup and as escaped text where it
                    // did not. Same rule as the detail sheet above: BodyHtml is
                    // sanitised, a.Body is not markup and must be escaped.
                    var quoted = "<br><br><hr>" + (a.BodyHtml || textToHtml(a.Body));
                    var email = new VIS.Email(to, null, null, shownRecordId, true, true,
                                              $self.table_ID || 0, quoted, subject, null);
                    var frame = new VIS.CFrame();
                    var label = VIS.Msg.getMsg("EMail");
                    frame.setName(label);
                    frame.setTitle(label);
                    frame.hideHeader(true);
                    frame.setContent(email);
                    frame.show();
                    email.initializeComponent();
                    // email.js hard-codes "Contacts" in its header when it is opened
                    // outside a window frame. Named for the product instead, so the
                    // composer says what it is replying about. Cosmetic, and its own
                    // try/catch: a markup change in the framework must not take the
                    // composer down with it.
                    try {
                        var about = (data && data.Product && data.Product.Name) ? data.Product.Name : "";
                        email.getRoot().find(".vis-awindow-header p").first()
                             .text(label + (about ? " (" + about + ")" : ""));
                    } catch (e2) { }
                    return;
                } catch (e) {
                    console.log(e);
                    // fall through to the workstation composer
                }
            }

            // No platform composer on this page. Better the workstation's than
            // nothing — the reply will not be filed against the product, but the
            // reader still gets an addressed message.
            try {
                window.open("mailto:" + encodeURIComponent(to) +
                            "?subject=" + encodeURIComponent(subject), "_blank");
            } catch (e3) { console.log(e3); }
        }

        // Plain text into the composer's HTML body: escaped, with line breaks
        // kept. The feed's bodies are already flattened to text server-side, so
        // this is the only place markup is reintroduced — and it reintroduces
        // exactly two tags, neither of them from the message.
        function textToHtml(text) {
            var s = (text === null || text === undefined) ? "" : String(text);
            if (!s) return "";
            return s.replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/\r\n/g, "\n")
                    .replace(/\r/g, "\n")
                    .replace(/\n/g, "<br>");
        }

        // Saves the meeting transcript as a text file. It runs to pages, so it is
        // offered as a download rather than put on screen under everything else.
        function downloadTranscript(a) {
            try {
                var name = (a.Title || "transcript").replace(/[\\/:*?"<>|]+/g, " ").trim();
                var blob = new Blob([String(a.Transcript)], { type: "text/plain;charset=utf-8" });

                // The IE / legacy Edge route, which the VIS shell can still be
                // hosted in.
                if (window.navigator && window.navigator.msSaveOrOpenBlob) {
                    window.navigator.msSaveOrOpenBlob(blob, name + ".txt");
                    return;
                }
                var url = URL.createObjectURL(blob);
                var link = document.createElement("a");
                link.href = url;
                link.download = name + ".txt";
                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);
                // Released on the next tick: revoking it synchronously can beat
                // the click the browser has not finished acting on.
                setTimeout(function () { URL.revokeObjectURL(url); }, 0);
            } catch (e) { console.log(e); }
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
            return $d;
        }

        // Who the mail was between, written out in full and labelled — and which
        // WAY it went, which is the first thing a reader wants from a feed that
        // now carries both. A RECEIVED mail leads with its sender: it is the reply
        // to something sent from the product, and "To <our own address>" answers
        // nothing about it. A sent one leads with where it went, as before.
        function recipientSummary(a) {
            var bits = [];
            if (a.IsReceived) {
                bits.push(msg("VAS_190_MailReceived", "Received"));
                appendAddressBit(bits, msg("VAS_190_From", "From"), a.MailFrom);
                appendAddressBit(bits, msg("VAS_190_To", "To"), a.MailTo);
            } else {
                appendAddressBit(bits, msg("VAS_190_To", "To"), a.MailTo);
            }
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
            // A letter is headlined by its subject exactly as a mail is, and falls
            // back the same way. It used to drop through to the generic branch
            // below and a letter with no subject read "Event".
            if (a.Type === "mail" || a.Type === "letter") {
                return (a.Title || "").trim() || msg("VAS_190_NoSubject", "(no subject)");
            }
            if (a.Type === "chat") {
                // The comment itself is the headline. It clips to one line on the
                // row and the whole of it is in the detail sheet the row opens.
                var text = (a.Title || "").replace(/\s+/g, " ").trim();
                return text || msg("VAS_190_EmptyComment", "(empty note)");
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
            } else if (a.Type === "letter") {
                // Nothing. A letter is an attached document, not a message that
                // went one way or the other: it states its heading and stops.
                // "Letter sent" / "Letter received" claimed a direction the record
                // does not carry, and the chip beside it already says what it is.
                return "";
            } else if (a.Type === "task") {
                // WHO it is on and WHEN it is due come before its state: those two
                // are what a reader acts on, and neither was on the row.
                if (a.AssigneeName) {
                    bits.push(msg("VAS_190_AssignedTo", "assigned to") + " " + a.AssigneeName);
                }
                var due = formatDate(a.EndDate);
                if (due) bits.push(msg("VAS_190_DueOn", "due") + " " + due);

                bits.push(a.IsClosed ? msg("VAS_190_TaskCompleted", "Completed")
                                     : msg("VAS_190_TaskOpen", "Open"));
                // How far along it is, after the state. Null means nobody has
                // recorded progress, which is not the same as 0% and is not shown.
                if (a.PercentComplete !== null && a.PercentComplete !== undefined) {
                    bits.push(a.PercentComplete + "%");
                }
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

        // The mail's own body block is gone: a mail opens its DETAIL SHEET now,
        // which states the same addresses as labelled fields and holds the message
        // under them. Only the appointment / task mail block below still folds,
        // and it lives inside that sheet.

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
            chat:      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2Z"/></svg>',
            // Dismisses the activity detail sheet.
            close:     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>'
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
        // Returning to a hidden browser tab is the moment the activity feed is
        // most likely to be out of date, so it is checked at once rather than
        // waiting out the poll interval. Namespaced per panel instance so two
        // open panels do not unbind each other's handler.
        var self = this;
        var seq = ++VAS._vas190Seq;
        this._visNs = "visibilitychange.vas190_" + seq;
        this._visHandler = function () { self.onVisibilityChange(); };
        try { $(document).on(this._visNs, this._visHandler); } catch (e) { }

        // Every activity a reader can raise against this product — a mail, a
        // note, a task, an appointment, a letter — is saved by somebody else's
        // dialog over XHR. The panel cannot hook those dialogs (they are
        // framework code and they know nothing about it), but it can hear their
        // requests finish, and that is the moment the feed is out of date.
        //
        // So: any completed request that is NOT this panel's own brings the
        // activity check forward. It does not decide from the URL whether an
        // activity was created — it could not, without naming every endpoint the
        // platform might grow — it just asks the cheap signature question sooner.
        // Where nothing changed the answer costs one indexed count and the panel
        // repaints nothing.
        this._ajaxNs = "ajaxComplete.vas190_" + seq;
        this._ajaxHandler = function (ev, xhr, settings) {
            var url = "";
            try { url = (settings && settings.url) ? String(settings.url) : ""; } catch (e) { return; }
            // This panel's own traffic, which is what a check IS — reacting to it
            // would have the watcher chasing its own tail.
            if (url.indexOf("VAS_190_ProductOverviewRightPanel") >= 0) return;
            self.nudgeActivityCheck();
        };
        try { $(document).on(this._ajaxNs, this._ajaxHandler); } catch (e) { }
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
        // Stop the activity watcher before curTab is nulled below, or its timer
        // fires against a disposed panel.
        if (typeof this.stopActivityWatch === "function") {
            try { this.stopActivityWatch(); } catch (e) { }
        }
        if (this._visNs) {
            try { $(document).off(this._visNs); } catch (e) { }
            this._visNs = null;
            this._visHandler = null;
        }
        // The AJAX listener is document-level and would otherwise outlive the
        // panel, waking a disposed instance on every request the page makes.
        if (this._ajaxNs) {
            try { $(document).off(this._ajaxNs); } catch (e) { }
            this._ajaxNs = null;
            this._ajaxHandler = null;
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
