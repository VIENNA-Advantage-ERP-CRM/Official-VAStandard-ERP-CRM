/**
 * VAS_289 Open Sales Quotations Widget (Sales Order dashboard, 3x3 conversion worklist)
 * Purpose - The conversion worklist: open Sales Quotations (C_Order,
 *           IsSalesQuotation='Y', DocStatus IN ('CO','IP'), not expired, with
 *           pending quantity) plus the full Quotation -> Sales Order conversion
 *           wizard. This is the one widget on the dashboard that WRITES - it
 *           creates a real Sales Order (never a mock), using MOrder/MOrderLine on
 *           the server inside one transaction, never a hand-written INSERT.
 *
 *           CONFIRMED overrides of the paired mock (22_Open_Sales_Quotations_
 *           Claude_Development_Prompt.txt): already-ordered quantity is always
 *           DERIVED from linked Sales Order lines (C_OrderLine.C_Quotation_Line_ID)
 *           - there is no stored AlreadyOrderedQty column and none is ever created.
 *           A merely Drafted/In-Process linked Sales Order already consumes the
 *           quotation's quantity. Conversion status is a derived UI label
 *           ("Accepted" / "Partly ordered" / "Awaiting approval") separate from raw
 *           DocStatus - never written back to C_Order.DocStatus. Per an explicit
 *           user confirmation, the Step 2 summary strip and status vocabulary match
 *           22-open-sales-quotations.html verbatim ("Customer"/"Segment"/"Accepted"),
 *           overriding the dev prompt's schema-label correction for these three
 *           labels only; every underlying value is still sourced from
 *           C_BPartner.Name / C_BP_Group.Name exactly as the dev prompt maps them.
 *           "Delivery mode" is Shipping Method
 *           (DeliveryViaRule) using real AD_Ref_List values, never Road/Courier/
 *           Rail mock values. Header Tax and header Print Description do not exist
 *           on C_Order and are removed; both are real C_OrderLine fields copied
 *           per-line and remain editable per line.
 *
 *           No period filter - quotations are worked until they convert or expire.
 *           There is no fixed AlreadyOrderedQty to trust: every conversion
 *           re-validates current pending quantity, inside the same transaction,
 *           immediately before writing - a concurrent conversion by another user
 *           surfaces as a conflict that names the affected line(s), never a
 *           silently reduced quantity.
 *
 *           QA 2026-10-29 (8.37-8.75): only Completed quotations are listed; Customer
 *           (as-you-type search, auto-fills Price List / Payment Term / Payment
 *           Method / Sales Rep from the customer) and Sales Rep are editable;
 *           Currency is read-only from the Price List; quantities and rates are in
 *           the line UOM (qty at the UOM's StdPrecision, rate 2 decimals, editable);
 *           the new order is saved Drafted or Completed (document workflow).
 *           Target Document Type, Date Ordered, Date Promised,
 *           Location, Contact, Payment Term, Payment Method, Shipping Rule,
 *           Shipping Method, Price List, Currency, Currency Rate Type, Inco Term,
 *           Warehouse, Priority, Order Reference and Description are all genuinely
 *           editable real dropdowns/inputs backed by real master data - matching
 *           the 3-screen New Sales Order flow (Details -> Lines -> Create SO)
 *           confirmed against the reference screenshots.
 *
 * Design  - 22-open-sales-quotations.html / .md: glass 3x3 tile, header with title
 *           + subtitle + a `.chip-ok`/`.chip-neutral` ready-count chip (no period
 *           filter), a CSS-grid table (5 columns, two-line first cell: quotation id
 *           over customer name) with 6 rows per page, and a footer pager. Row click
 *           on a convertible (CO) row enters the wizard at Step 2 (line selection)
 *           directly - skipping a picker, since the user already chose. An
 *           Awaiting-Approval (IP) row instead navigates to the existing Sales
 *           Quotation record window (VAS.ZoomUtil, the same helper VAS_276 already
 *           uses for this exact case) - it can never start a conversion.
 *
 *           The wizard reuses the one shared modal component for every screen:
 *           Step 2 line selection -> Step 3 order details -> Step 4 lines review ->
 *           create. Back navigation at every step re-renders from the same live
 *           `wizard` state object, never a frozen snapshot.
 *
 * Backend - VAS_289_OpenSalesQuotationsWidget/GetOpenQuotations     (GET page,size -> ready count + paginated list)
 *           VAS_289_OpenSalesQuotationsWidget/GetQuotationLines     (GET quotationId -> header + pending lines)
 *           VAS_289_OpenSalesQuotationsWidget/GetFreeStock          (GET productId,warehouseId,attributeSetInstanceId -> free stock)
 *           VAS_289_OpenSalesQuotationsWidget/GetOrderFormDefaults  (GET quotationId -> header defaults + dropdown options)
 *           VAS_289_OpenSalesQuotationsWidget/GetBPartnerOptions    (GET bpartnerId -> locations + contacts)
 *           VAS_289_OpenSalesQuotationsWidget/CreateSalesOrder      (POST requestJson -> transactional create, or a conflict)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Open Sales Quotations                                                 | VAS_289_Title
 *  2  | Accepted and ready to convert into an SO                              | VAS_289_Subtitle
 *  3  | ready                                                                 | VAS_289_ReadySuffix
 *  4  | earliest expiry first                                                 | VAS_289_RankedBy
 *  5  | No open quotations. Sales orders can also be raised directly.         | VAS_289_ZeroState
 *  6  | Ranking unavailable                                                   | VAS_289_ErrorState
 *  7  | Quotation                                                             | VAS_289_ColQuotation
 *  8  | Lines                                                                 | VAS_289_ColLines
 *  9  | Pending qty                                                           | VAS_289_ColPendingQty
 * 10  | Valid till                                                            | VAS_289_ColValidTill
 * 11  | Status                                                                | VAS_289_ColStatus
 * 12  | Close                                                                 | VAS_289_Close
 * 13  | Back                                                                  | VAS_289_Back
 * 14  | Continue                                                              | VAS_289_Continue
 * 15  | Create SO                                                             | VAS_289_CreateSalesOrder
 * 16  | select the lines to raise a sales order against                      | VAS_289_Step2SubtitleSuffix
 * 17  | valid till                                                            | VAS_289_ValidTillLabel
 * 18  | Quotation                                                            | VAS_289_StatQuotation
 * 19  | Customer                                                             | VAS_289_StatBusinessPartner
 * 20  | Segment                                                              | VAS_289_StatBPGroup
 * 21  | Lines                                                                | VAS_289_StatLines
 * 22  | Quoted qty                                                           | VAS_289_StatQuotedQty
 * 23  | Already ordered                                                      | VAS_289_StatAlreadyOrdered
 * 24  | Pending qty                                                          | VAS_289_StatPendingQty
 * 25  | Status                                                               | VAS_289_StatStatus
 * 26  | Product                                                              | VAS_289_ColProduct
 * 27  | UoM                                                                  | VAS_289_ColUom
 * 28  | Qty to order                                                         | VAS_289_ColQtyToOrder
 * 29  | Ship from                                                            | VAS_289_ColShipFrom
 * 30  | Free stock                                                           | VAS_289_ColFreeStock
 * 31  | No lines selected                                                    | VAS_289_NoLinesSelected
 * 32  | lines selected                                                       | VAS_289_LinesSelectedSuffix
 * 33  | different warehouses — one sales order ships from a single warehouse | VAS_289_MultiWarehouseWarning
 * 34  | lines short of free stock — will backorder                          | VAS_289_ShortStockWarning
 * 35  | stock available                                                     | VAS_289_StockAvailable
 * 36  | New Sales Order · Details                                           | VAS_289_Step3Title
 * 37  | Document                                                            | VAS_289_GroupDocument
 * 38  | Target Document Type                                                | VAS_289_FieldDocType
 * 39  | Order Reference                                                     | VAS_289_FieldPOReference
 * 40  | SO Date                                                             | VAS_289_FieldDateOrdered
 * 41  | Date Promised                                                       | VAS_289_FieldDatePromised
 * 42  | Priority                                                            | VAS_289_FieldPriority
 * 43  | Customer And Payment                                                | VAS_289_GroupBPartner
 * 44  | Customer                                                            | VAS_289_FieldBusinessPartner
 * 45  | Ship-to Location                                                    | VAS_289_FieldLocation
 * 46  | Customer Contact                                                    | VAS_289_FieldContact
 * 47  | Sales Rep                                                           | VAS_289_FieldSalesRep
 * 48  | Payment Term                                                        | VAS_289_FieldPaymentTerm
 * 49  | Payment Method                                                      | VAS_289_FieldPaymentMethod
 * 50  | Delivery And Pricing                                                | VAS_289_GroupShipping
 * 51  | Warehouse                                                           | VAS_289_FieldWarehouse
 * 52  | Shipping Rule                                                       | VAS_289_FieldShippingRule
 * 53  | Shipping Method                                                     | VAS_289_FieldShippingMethod
 * 54  | Price List                                                          | VAS_289_FieldPriceList
 * 55  | Currency                                                            | VAS_289_FieldCurrency
 * 56  | Currency Rate Type                                                  | VAS_289_FieldRateType
 * 57  | Inco Term                                                           | VAS_289_FieldIncoTerm
 * 58  | Description                                                         | VAS_289_GroupDescription
 * 59  | Description                                                         | VAS_289_FieldDescription
 * 60  | Sales order lines                                                   | VAS_289_SectionLines
 * 61  | Tax                                                                 | VAS_289_ColTax
 * 62  | Subtotal                                                            | VAS_289_TotalsSubtotal
 * 63  | Tax                                                                 | VAS_289_TotalsTax
 * 64  | Order total                                                         | VAS_289_TotalsOrderTotal
 * 65  | Sales order created for                                             | VAS_289_ToastPrefix
 * 66  | lines                                                               | VAS_289_LinesSuffix
 * 67  | Search is unavailable right now. Try again in a moment.             | VAS_289_LoadError
 * 68  | Previous page                                                       | VAS_289_PrevPage
 * 69  | Next page                                                           | VAS_289_NextPage
 * 70  | of                                                                  | VAS_289_Of
 * 71  | Showing                                                             | VAS_289_Showing
 * 72  | Page 1 of 2                                                         | VAS_289_Page1Of2
 * 73  | from                                                                | VAS_289_FromQuotationPrefix
 * 74  | Order qty                                                           | VAS_289_StatOrderQty
 * 75  | Order value                                                         | VAS_289_StatOrderValue
 * 76  | Stock cover                                                         | VAS_289_StatStockCover
 * 77  | line(s) short of stock                                              | VAS_289_LinesShortSuffix
 * 78  | All lines covered                                                   | VAS_289_AllLinesCovered
 * 79  | Continue to lines                                                   | VAS_289_ContinueToLines
 * 80  | New Sales Order · Lines                                             | VAS_289_Step4Title
 * 81  | Page 2 of 2                                                         | VAS_289_Page2Of2
 * 82  | line / lines                                                        | VAS_289_LineWordSuffix
 * 83  | Back to details                                                     | VAS_289_BackToDetails
 * 84  | Rate                                                                | VAS_289_ColRate
 * 85  | Print Description                                                   | VAS_289_FieldPrintDescription
 * 86  | line(s) affected                                                    | VAS_289_ConflictLinesAffected
 * 87  | Stock Qty (In Base UOM)                                             | VAS_289_ColStockQtyBaseUom
 * 88  | Date Ordered                                                        | VAS_289_DateOrdered
 * 89  | Location                                                            | VAS_289_Location
 * 90  | Document Status                                                     | VAS_289_FieldDocStatus
 * 91  | Search customer                                                     | VAS_289_SearchCustomer
 * 92  | Please fill the mandatory fields:                                   | VAS_289_FillMandatory
 * 93  | Error: Date Promised should be greater than or equal to Date Ordered | VAS_289_DatePromisedBeforeOrdered
 * 94  | Sales Order created successfully with document number:              | VAS_289_SOCreatedDocNo
 * 95  | It was saved as Drafted because it could not be completed:          | VAS_289_SOCompleteFailed
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Sales Order dashboard request queue (shared by VAS_270-289).
       The dashboard starts ~20 widget calls at once on one login session and the server
       answers the overflow with HTTP 500 ("request queue limit of the session exceeded").
       GET calls are therefore run at most 3 at a time and retried (up to 2 times, with a
       short back-off) when the server answers 500/503/0. The first widget file loaded
       creates the singleton; every other widget reuses it. A failed card also gets a
       Retry button so the user never has to close and re-open the screen. */
    var VASSONet = window.__vasSONet = window.__vasSONet || (function () {
        var MAX_PARALLEL = 3, MAX_RETRIES = 2, running = 0, waiting = [];

        function pump() {
            while (running < MAX_PARALLEL && waiting.length) { start(waiting.shift()); }
        }

        function start(job) {
            running++;
            var o = job.opts;
            $.ajax($.extend({}, o, {
                success: function () {
                    running--;
                    if (o.success) { o.success.apply(this, arguments); }
                    pump();
                },
                error: function (xhr) {
                    var self = this, args = arguments;
                    running--;
                    var st = xhr ? xhr.status : 0;
                    if (job.tries < MAX_RETRIES && (st === 500 || st === 503 || st === 0) && xhr.statusText !== 'abort') {
                        job.tries++;
                        setTimeout(function () { waiting.push(job); pump(); }, 400 * job.tries + Math.floor(Math.random() * 300));
                    } else if (o.error) {
                        o.error.apply(self, args);
                    }
                    pump();
                }
            }));
        }

        function injectStyle() {
            if (document.getElementById('vasso-retry-style')) { return; }
            var st = document.createElement('style');
            st.id = 'vasso-retry-style';
            st.textContent = '.vasso-retry{position:absolute;right:10px;bottom:8px;z-index:3;border:1px solid currentColor;' +
                'background:rgba(255,255,255,.85);color:#1a5fb4;border-radius:12px;padding:2px 12px;font-size:12px;' +
                'line-height:18px;cursor:pointer}.vasso-retry:hover,.vasso-retry:focus{background:#fff;outline:2px solid #1a5fb4}';
            document.head.appendChild(st);
        }

        return {
            // GET calls are queued/retried; anything else (e.g. a POST that creates a record) runs untouched.
            ajax: function (o) {
                if (o && o.type && String(o.type).toUpperCase() !== 'GET') { return $.ajax(o); }
                waiting.push({ opts: o, tries: 0 });
                pump();
            },
            // Adds a Retry button to a widget root that is showing its error state.
            retry: function ($root, widget) {
                injectStyle();
                $root.find('.vasso-retry').remove();
                if ($root.css('position') === 'static') { $root.css('position', 'relative'); }
                var text = '';
                try { text = VIS.Msg.getMsg('VAS_SO_Retry'); } catch (e) { text = ''; }
                if (!text || text === 'VAS_SO_Retry') { text = 'Retry'; }
                $('<button type="button" class="vasso-retry"></button>').text(text)
                    .attr('title', text)
                    .on('click', function (e) {
                        e.stopPropagation();
                        $(this).remove();
                        widget.refreshWidget();
                    })
                    .appendTo($root);
            }
        };
    })();


    var WIDGET_PAGE_SIZE = 6;
    var MODAL_MAX_ROWS = 50;    // upper safety cap only - the real page size is measured from the available height (QA 8.39)
    var MODAL_INITIAL_ROWS = 10;
    var MODAL_MIN_ROWS = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    var TABLE_TEMPLATE = 'minmax(0,1.7fr) minmax(0,.6fr) minmax(0,.9fr) minmax(0,.9fr) minmax(0,1.05fr)';

    var ZOOM_WINDOW_NAME = 'VAS_SalesQuotation';

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }
        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }
        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_289_OpenSalesQuotationsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas289-root">');
        var $shell, $chip, $tbody, $helper, $pageTxt, $prevBtn, $nextBtn;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_289_OpenSalesQuotationsWidget/';

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [], readyCount: 0 };

        var wizard = null;          // live conversion-wizard state, see openWizard()
        var lineSelState = null;    // { size } - step 2 table page size (measured)
        var linePage2 = 0;
        var lineReviewPage = 0;
        var lineReviewState = null; // { size } - step 4 table page size (measured)
        var zoomWindowId = 0;

        var cfgStack = [];
        var currentCfg = null;

        function label(key, fallback) {
            var text = '';
            try { text = VIS.Msg.getMsg(key); } catch (e) { text = ''; }
            // VIS.Msg echoes the key back when no message is defined - show the English text then.
            return (!text || text === key) && fallback !== undefined ? fallback : text;
        }

        /* QA 8.49/8.68 and other blocking validations - the framework's info dialog when
           available, the widget toast otherwise. */
        function infoMsg(text) {
            if (window.VIS && VIS.ADialog && VIS.ADialog.info) { VIS.ADialog.info(null, null, text); }
            else { toast(text); }
        }

        /* QA 8.38: an attribute that is missing/only placeholder punctuation shows blank. */
        function cleanAttr(text) {
            var t = String(text == null ? '' : text).trim();
            return /^[\s\-_–—·.#|]*$/.test(t) ? '' : t;
        }

        /* QA 8.42: quantities follow the line UOM's StdPrecision. */
        function roundTo(value, precision) {
            var p = Math.max(0, Math.min(10, Number(precision) || 0));
            var f = Math.pow(10, p);
            return Math.round((Number(value) || 0) * f) / f;
        }

        function formatQty(value, precision) {
            var p = Math.max(0, Math.min(10, Number(precision) || 0));
            return roundTo(value, p).toLocaleString('en-IN', { minimumFractionDigits: 0, maximumFractionDigits: p });
        }

        /* QA 8.75: rate with exactly two decimals. */
        function formatRate(value) {
            return (Number(value) || 0).toFixed(2);
        }

        function qtyStep(precision) {
            var p = Math.max(0, Number(precision) || 0);
            return p === 0 ? '1' : (1 / Math.pow(10, p)).toFixed(p);
        }

        /* Free stock is in the product's base UOM - the ordered qty is in the line UOM. */
        function qtyInBase(line) {
            var r = Number(line.enteredPerBase) || 1;
            return (Number(line.qtyToOrder) || 0) / r;
        }

        function isShort(line) {
            return line.isStocked && (Number(line.freeStock) || 0) < qtyInBase(line);
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function icon(name) {
            if (name === 'close') { return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>'; }
            if (name === 'back') { return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>'; }
            if (name === 'prev') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>'; }
            if (name === 'next') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>'; }
            if (name === 'desc') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 6h16M4 12h16M4 18h10"/></svg>'; }
            return '';
        }

        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0) * 100) / 100;
        }

        function formatNumDisplay(value) {
            var n = Number(value || 0);
            return (Math.round(n * 100) / 100).toLocaleString('en-IN');
        }

        function parseIso(iso) {
            if (!iso) { return null; }
            var d = new Date(iso + 'T00:00:00');
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDateShort(iso) {
            var d = parseIso(iso);
            if (!d) { return '—'; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()];
        }

        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return '—'; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            // QA 8.38/8.62: a missing value shows blank, never a hyphen placeholder.
            var display = (value === null || value === undefined) ? '' : value;
            return '<span class="vas289-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value == null ? '' : value) + '">' + escapeHtml(display) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas289-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        function chipClass(name) { return 'vas289-chip-' + name; }

        function toast(msg) {
            var $t = $root.closest('body').find('#vas289-toast');
            if ($t.length === 0) {
                $t = $('<div id="vas289-toast" class="vas289-toast"></div>');
                $('body').append($t);
            }
            $t.text(msg).addClass('show');
            clearTimeout($t.data('h'));
            $t.data('h', setTimeout(function () { $t.removeClass('show'); }, 3200));
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + ready chip) + quotation table + pager
         * ============================================================ */
        function createWidget() {
            $shell = $('<div class="vas289-shell"></div>');

            var $head = $('<div class="vas289-head"></div>');
            var $htxt = $('<div class="vas289-head-txt"></div>');
            $htxt.append('<p class="vas289-title">' + escapeHtml(label('VAS_289_Title', 'Open Sales Quotations')) + '</p>');
            $htxt.append('<p class="vas289-sub">' + escapeHtml(label('VAS_289_Subtitle', 'Accepted and ready to convert into an SO')) + '</p>');
            $chip = $('<span class="vas289-chip vas289-chip-ok"></span>');
            $head.append($htxt, $chip);

            var $tbl = $('<div class="vas289-tbl"></div>');
            var $thead = $('<div class="vas289-trow vas289-thead"></div>').css('grid-template-columns', TABLE_TEMPLATE);
            $thead.html([
                { l: label('VAS_289_ColQuotation', 'Quotation'), right: false },
                { l: label('VAS_289_ColLines', 'Lines'), right: true },
                { l: label('VAS_289_ColPendingQty', 'Pending qty'), right: true },
                { l: label('VAS_289_ColValidTill', 'Valid till'), right: false },
                { l: label('VAS_289_ColStatus', 'Status'), right: false }
            ].map(function (c) {
                return '<span class="vas289-cell' + (c.right ? ' right' : '') + '" title="' + escapeHtml(c.l) + '">' + escapeHtml(c.l) + '</span>';
            }).join(''));

            $tbody = $('<div class="vas289-tbody"></div>');
            $tbl.append($thead, $tbody);

            var $foot = $('<div class="vas289-wfoot"></div>');
            $helper = $('<span class="vas289-helper"></span>');
            var $pager = $('<div class="vas289-pager"></div>');
            $prevBtn = $('<button type="button" class="vas289-pbtn" aria-label="' + escapeHtml(label('VAS_289_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas289-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas289-pbtn" aria-label="' + escapeHtml(label('VAS_289_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $tbl, $foot);
            $root.append($shell);

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $tbody.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-qid]') : null;
                if (!row) { return; }
                var qid = Number(row.getAttribute('data-qid'));
                var convertible = row.getAttribute('data-convertible') === '1';
                if (convertible) { openWizard(qid); }
                else { openQuotationRecord(qid); }
            });
        }

        function skeletonRows() {
            var tpl = TABLE_TEMPLATE, rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas289-trow" style="grid-template-columns:' + tpl + ';cursor:default">' +
                    '<span class="vas289-cell"><span class="vas289-skel-cell" style="width:75%"></span></span>' +
                    '<span class="vas289-cell right"><span class="vas289-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                    '<span class="vas289-cell right"><span class="vas289-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas289-cell"><span class="vas289-skel-cell" style="width:55%"></span></span>' +
                    '<span class="vas289-cell"><span class="vas289-skel-cell" style="width:65%"></span></span>' +
                '</div>';
            }
            return rows;
        }

        function loadOpenQuotations(page) {
            listState = 'loading';
            renderWidget();

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetOpenQuotations',
                type: 'GET', dataType: 'json', cache: false,
                data: { page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        docsState.page = page;
                        docsState.total = Number(parsed.Total || 0);
                        docsState.readyCount = Number(parsed.ReadyCount || 0);
                        docsState.rows = parsed.Rows || [];
                    }
                    renderWidget();
                },
                error: function () { listState = 'error'; renderWidget(); }
            });
        }

        function renderWidget() {
            if (listState === 'loading') {
                $chip.hide();
                $tbody.html(skeletonRows());
                $helper.text(''); $pageTxt.text('');
                $prevBtn.prop('disabled', true); $nextBtn.prop('disabled', true);
                return;
            }
            if (listState === 'error') {
                $chip.hide();
                $tbody.html('<div class="vas289-empty">' + escapeHtml(label('VAS_289_ErrorState', 'Ranking unavailable')) + '</div>');
                VASSONet.retry($root, $self);
                $helper.text(''); $pageTxt.text('');
                $prevBtn.prop('disabled', true); $nextBtn.prop('disabled', true);
                return;
            }

            $chip.show();
            var chipText = docsState.readyCount + ' ' + label('VAS_289_ReadySuffix', 'ready');
            $chip.removeClass('vas289-chip-ok vas289-chip-neutral')
                .addClass(docsState.readyCount > 0 ? 'vas289-chip-ok' : 'vas289-chip-neutral')
                .attr('title', chipText).text(chipText);

            if (docsState.total <= 0) {
                $tbody.html('<div class="vas289-empty">' + escapeHtml(label('VAS_289_ZeroState', 'No open quotations. Sales orders can also be raised directly.')) + '</div>');
                $helper.text(''); $pageTxt.text('');
                $prevBtn.prop('disabled', true); $nextBtn.prop('disabled', true);
                return;
            }

            var tpl = TABLE_TEMPLATE;
            $tbody.html(docsState.rows.map(function (row) {
                return '<button type="button" class="vas289-trow" style="grid-template-columns:' + tpl + '" data-qid="' + row.QuotationId + '" data-convertible="' + (row.IsConvertible ? '1' : '0') + '">' +
                    '<span class="vas289-cell" style="min-width:0" title="' + escapeHtml(row.DocumentNo + ' · ' + row.BusinessPartner) + '">' +
                        '<span class="vas289-cell vas289-c-prim" style="display:block">' + escapeHtml(row.DocumentNo) + '</span>' +
                        '<span class="vas289-sub" style="display:block">' + escapeHtml(row.BusinessPartner) + '</span>' +
                    '</span>' +
                    cellHtml(formatNumDisplay(row.LineCount), 'vas289-c-dark', 'right') +
                    cellHtml(formatNumDisplay(row.PendingQty), 'vas289-c-dark', 'right') +
                    cellHtml(formatDateShort(row.ValidTillDate), 'vas289-c-std') +
                    '<span class="vas289-cell" title="' + escapeHtml(row.StatusLabel) + '"><span class="vas289-chip ' + chipClass(row.StatusChipClass) + '">' + escapeHtml(row.StatusLabel) + '</span></span>' +
                '</button>';
            }).join(''));

            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var start = docsState.page * docsState.size;
            var helperText = label('VAS_289_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_289_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_289_RankedBy', 'earliest expiry first');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((docsState.page + 1) + ' ' + label('VAS_289_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', docsState.page === 0);
            $nextBtn.prop('disabled', docsState.page >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            loadOpenQuotations(next);
        }

        /* Awaiting-Approval (IP) rows never enter the wizard - navigate to the
           existing Sales Quotation record window instead (same helper VAS_276 uses). */
        function openQuotationRecord(quotationId) {
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', quotationId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        /* ============================================================
         * Modal shell (shared across every wizard step)
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas289-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas289-modal"></div>');
            $mHead = $('<div class="vas289-mhead"></div>');
            var $htxt = $('<div class="vas289-htxt"></div>');
            $mBack = $('<button type="button" class="vas289-xbtn" aria-label="' + escapeHtml(label('VAS_289_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas289-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas289-xbtn" aria-label="' + escapeHtml(label('VAS_289_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas289-mbody"></div>');
            $mFoot = $('<div class="vas289-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBack.on('click', backModal);
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas289-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitAllTables(); }
            });
        }

        function paintChrome(cfg) {
            $mBack.prop('hidden', !cfgStack.length);
            $modal.removeClass('sm md vas289-fixedh').addClass(cfg.size || '');
            // QA 8.39: table steps use the full modal height so rows-per-page is measured from
            // the real available space (and grows/shrinks with the window), not from content.
            if (cfg.fixedHeight) { $modal.addClass('vas289-fixedh'); }
            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
            $mBody.removeClass('vas289-scrollable');
        }

        /* cfg.render() is never allowed to fail silently: a thrown error here would
           otherwise leave paintChrome's already-updated title/subtitle mismatched
           against the previous screen's stale, still-visible body/footer (and any
           now-dangling event handlers bound to it) - exactly the "chrome changed but
           nothing else happened" symptom that gave no diagnostic trail. */
        function renderScreenSafely(cfg) {
            try {
                cfg.render();
            } catch (e) {
                if (window.console && console.error) { console.error('VAS_289 render failed for step ' + cfg.step, e); }
                showLoadError();
            }
        }

        function showScreen(cfg, push) {
            if (push && currentCfg) { cfgStack.push(currentCfg); }
            else if (!push) { cfgStack = []; }
            currentCfg = cfg;
            paintChrome(cfg);
            renderScreenSafely(cfg);
            $mask.addClass('is-open');
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function backModal() {
            var prev = cfgStack.pop();
            if (!prev) { closeModal(); return; }
            currentCfg = prev;
            paintChrome(prev);
            renderScreenSafely(prev);
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function closeModal() {
            $mask.removeClass('is-open');
            cfgStack = []; currentCfg = null;
            wizard = null; lineSelState = null; lineReviewState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || ''); $mSub.text('');
            $mBody.html('<div class="vas289-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas289-mstate">' + escapeHtml(label('VAS_289_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }
            if (currentCfg && currentCfg.step === 2) { fitLineSelTable(); }
            if (currentCfg && currentCfg.step === 4) { fitLineReviewTable(); }
        }

        /* QA 8.39: rows per page = how many rows really fit in the rows area (the flex:1
           region between the table header and the pager, inside a fixed-height modal), so
           the "x–y of n" range always equals the visible rows. Re-run on window resize. */
        function fitTableGeneric(id, sizeRef, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var rowsEl = el.querySelector('.vas289-mbody-rows');
            var row = rowsEl ? rowsEl.querySelector('.vas289-mrow') : null;
            if (!rowsEl || !row) { return; }
            var avail = rowsEl.clientHeight;
            if (avail < 20) { return; }
            var rowHeight = row.getBoundingClientRect().height || 34;
            var n = Math.floor((avail + 0.5) / rowHeight);
            n = Math.max(MODAL_MIN_ROWS, Math.min(MODAL_MAX_ROWS, n));
            if (n !== sizeRef.size) { sizeRef.size = n; onResize(); }
        }

        function fitLineSelTable() {
            if (!wizard || !lineSelState) { return; }
            fitTableGeneric('vas289-linesel-tbl', lineSelState, function () {
                var pages = Math.max(1, Math.ceil(wizard.lines.length / lineSelState.size));
                linePage2 = Math.min(linePage2, pages - 1);
                drawLineSelTable();
            });
        }

        function fitLineReviewTable() {
            if (!wizard || !lineReviewState) { return; }
            fitTableGeneric('vas289-linerev-tbl', lineReviewState, function () {
                var pages = Math.max(1, Math.ceil(wizard.selectedLines().length / lineReviewState.size));
                lineReviewPage = Math.min(lineReviewPage, pages - 1);
                drawLineReviewTable();
            });
        }

        /* ============================================================
         * Wizard entry: fetch the quotation + its pending lines, build the
         * live `wizard` state object every step reads from, open Step 2.
         * ============================================================ */
        function openWizard(quotationId) {
            showLoading('…');

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetQuotationLines',
                type: 'GET', dataType: 'json', cache: false,
                data: { quotationId: quotationId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Quotation) { showLoadError(); return; }

                    wizard = {
                        quotationId: quotationId,
                        quotation: parsed.Quotation,
                        lines: (parsed.Lines || []).map(function (l) {
                            return {
                                lineId: l.LineId,
                                productId: l.ProductId,
                                productName: l.ProductName,
                                isStocked: l.IsStocked,
                                asiId: l.AttributeSetInstanceId || 0,
                                attributeText: cleanAttr(l.AttributeText),
                                uomId: l.UomId,
                                uomName: l.UomName,
                                // QA 8.42/8.73: quantities are in the line UOM, at its StdPrecision.
                                precision: Number(l.UomPrecision) || 0,
                                enteredPerBase: Number(l.EnteredPerBase) || 1,
                                quotedQty: Number(l.QuotedQty) || 0,
                                alreadyOrderedQty: Number(l.AlreadyOrderedQty) || 0,
                                pendingQty: Number(l.PendingQty) || 0,
                                rate: roundTo(Number(l.Rate) || 0, 2),
                                taxId: l.TaxId || 0,
                                // QA 8.67: follows the Details step's Date Promised until edited on the lines step.
                                datePromised: null,
                                datePromisedEdited: false,
                                description: l.Description || '',
                                printDescription: l.PrintDescription || '',
                                warehouseId: l.WarehouseId,
                                freeStock: l.FreeStock === null || l.FreeStock === undefined ? null : Number(l.FreeStock),
                                selected: false,
                                qtyToOrder: Number(l.PendingQty) || 0
                            };
                        }),
                        selectedLines: function () { return this.lines.filter(function (l) { return l.selected; }); },
                        formDefaults: null,
                        formOptions: null,
                        header: null,           // user-edited Step 3 field values
                        descOpen: {}             // line id -> bool, Step 4 description-panel toggle
                    };

                    linePage2 = 0;
                    showScreen(buildStep2Cfg(), false);
                },
                error: function () { showLoadError(); }
            });
        }

        /* ============================================================
         * Step 2 - line selection
         * ============================================================ */
        function buildStep2Cfg() {
            var q = wizard.quotation;
            return {
                step: 2,
                title: q.DocumentNo,
                subtitle: [q.BusinessPartner, label('VAS_289_ValidTillLabel', 'valid till') + ' ' + formatDateShort(q.ValidTillDate), label('VAS_289_Step2SubtitleSuffix', 'select the lines to raise a sales order against')].filter(function (p) { return p; }).join(' · '),
                size: '',
                fixedHeight: true,
                render: renderStep2
            };
        }

        function renderStep2() {
            var q = wizard.quotation;
            // QA 8.46: Customer / Segment / Lines summary cards removed from the pop-up.
            var statsHtml = '<div class="vas289-mstats">' +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), q.DocumentNo) +
                statTile(label('VAS_289_StatQuotedQty', 'Quoted qty'), formatNumDisplay(q.QuotedQty)) +
                statTile(label('VAS_289_StatAlreadyOrdered', 'Already ordered'), formatNumDisplay(q.AlreadyOrderedQty)) +
                statTile(label('VAS_289_StatPendingQty', 'Pending qty'), formatNumDisplay(q.PendingQty)) +
                statTile(label('VAS_289_StatStatus', 'Status'), q.StatusLabel) +
            '</div>';

            lineSelState = lineSelState || { size: MODAL_INITIAL_ROWS };
            $mBody.html(statsHtml + '<div class="vas289-mtwrap"><div class="vas289-mtbl" id="vas289-linesel-tbl"></div></div>');
            drawLineSelTable();
            renderStep2Footer();
        }

        var SEL_COLS = [
            { key: 'chk', label: '', w: .3 },
            { key: 'product', label: label('VAS_289_ColProduct', 'Product'), w: 1.7 },
            { key: 'uom', label: label('VAS_289_ColUom', 'UoM'), w: .55 },
            { key: 'pending', label: label('VAS_289_ColPendingQty', 'Pending qty'), w: .8, align: 'right' },
            { key: 'qty', label: label('VAS_289_ColQtyToOrder', 'Qty to order'), w: .95 },
            // QA 8.41: "Ship from" column renamed to "Warehouse".
            { key: 'wh', label: label('VAS_289_FieldWarehouse', 'Warehouse'), w: 1.1 },
            // QA 8.74: free stock is the base-UOM stock quantity.
            { key: 'stock', label: label('VAS_289_ColStockQtyBaseUom', 'Stock Qty (In Base UOM)'), w: .95, align: 'right' }
        ];

        /* QA 8.41b: the per-line warehouse list is the Sales Order window's list (warehouses of
           the order's organization). A line whose quotation warehouse is not in it falls back
           to the quotation header warehouse, else the first listed warehouse. */
        function normalizeLineWarehouses() {
            var list = wizard.formOptionsWarehouses || [];
            var inList = function (id) { return list.some(function (w) { return w.Id === id; }); };
            var headerWh = wizard.quotation.WarehouseId;
            wizard.lines.forEach(function (line) {
                if (inList(line.warehouseId)) { return; }
                line.warehouseId = inList(headerWh) ? headerWh : (list.length ? list[0].Id : 0);
                line.freeStockStale = true;
            });
        }

        function warehouseOptionsHtml(selectedId) {
            var options = (wizard.formOptionsWarehouses || []).slice();
            return options.map(function (w) {
                return '<option value="' + w.Id + '"' + (w.Id === selectedId ? ' selected' : '') + '>' + escapeHtml(w.Name) + '</option>';
            }).join('');
        }

        function ensureWarehouseOptionsLoaded(cb) {
            if (wizard.formOptionsWarehouses) { cb(); return; }
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetOrderFormDefaults',
                type: 'GET', dataType: 'json', cache: false,
                data: { quotationId: wizard.quotationId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (!wizard) { return; }
                    if (!parsed.Error) {
                        wizard.formDefaults = parsed.Defaults;
                        wizard.formOptions = parsed.Options;
                        wizard.formOptionsWarehouses = (parsed.Options && parsed.Options.Warehouses) || [];
                    } else {
                        wizard.formOptionsWarehouses = [];
                    }
                    normalizeLineWarehouses();
                    refreshStaleFreeStock();
                    cb();
                },
                error: function () { if (wizard) { wizard.formOptionsWarehouses = []; cb(); } }
            });
        }

        function refreshStaleFreeStock() {
            wizard.lines.forEach(function (line) {
                if (!line.freeStockStale) { return; }
                line.freeStockStale = false;
                fetchLineFreeStock(line, function () {
                    if (currentCfg && currentCfg.step === 2) { drawLineSelTable(); renderStep2Footer(); }
                });
            });
        }

        function drawLineSelTable() {
            var el = document.getElementById('vas289-linesel-tbl');
            if (!el || !wizard) { return; }

            if (!wizard.formOptionsWarehouses) {
                el.innerHTML = '<div class="vas289-mstate">…</div>';
                ensureWarehouseOptionsLoaded(drawLineSelTable);
                return;
            }

            var tpl = SEL_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var size = lineSelState.size;
            var pages = Math.max(1, Math.ceil(wizard.lines.length / size));
            if (linePage2 > pages - 1) { linePage2 = pages - 1; }
            var start = linePage2 * size;
            var slice = wizard.lines.slice(start, start + size);

            var head = '<div class="vas289-mrow vas289-mhead-row" style="grid-template-columns:' + tpl + '">' +
                SEL_COLS.map(function (c) { return '<span class="vas289-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line, i) {
                var idx = start + i;
                var stockCell;
                if (!line.isStocked) { stockCell = cellHtml('', 'vas289-c-std', 'right'); }
                else {
                    stockCell = cellHtml(formatNumDisplay(line.freeStock || 0), isShort(line) ? 'vas289-c-short' : 'vas289-c-ok', 'right');
                }
                return '<div class="vas289-mrow vas289-mrow-pick" data-line-idx="' + idx + '" style="grid-template-columns:' + tpl + '">' +
                    '<span class="vas289-cell center"><input type="checkbox" class="vas289-chk" data-line-idx="' + idx + '"' + (line.selected ? ' checked' : '') + ' aria-label="' + escapeHtml(line.productName) + '"></span>' +
                    cellHtml(line.productName + (line.attributeText ? ' · ' + line.attributeText : ''), 'vas289-c-prim') +
                    cellHtml(line.uomName, 'vas289-c-std') +
                    cellHtml(formatQty(line.pendingQty, line.precision), 'vas289-c-dark', 'right') +
                    '<span class="vas289-cell"><input type="number" class="vas289-qty-input" data-line-idx="' + idx + '" min="0" max="' + line.pendingQty + '" step="' + qtyStep(line.precision) + '" value="' + roundTo(line.qtyToOrder, line.precision) + '"></span>' +
                    '<span class="vas289-cell"><select class="vas289-wh-select" data-line-idx="' + idx + '">' + warehouseOptionsHtml(line.warehouseId) + '</select></span>' +
                    stockCell +
                '</div>';
            }).join('');

            var showingLabel = label('VAS_289_Showing', 'Showing') + ' ' + (wizard.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_289_Of', 'of') + ' ' + wizard.lines.length;
            var foot = '<div class="vas289-mtfoot"><span class="vas289-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas289-pager">' +
                        '<button type="button" class="vas289-pbtn" data-linesel-dir="-1"' + (linePage2 === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_289_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas289-ptxt">' + (linePage2 + 1) + ' ' + escapeHtml(label('VAS_289_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas289-pbtn" data-linesel-dir="1"' + (linePage2 >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_289_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas289-mbody-rows">' + body + '</div>' + foot;
            wireLineSelRowEvents(el);
        }

        function wireLineSelRowEvents(el) {
            el.querySelectorAll('.vas289-mrow-pick').forEach(function (rowEl) {
                rowEl.addEventListener('click', function (event) {
                    if (event.target.closest('input,select')) { return; }
                    var idx = Number(rowEl.getAttribute('data-line-idx'));
                    toggleLineSelected(idx);
                });
            });
            el.querySelectorAll('.vas289-chk').forEach(function (chk) {
                chk.addEventListener('click', function (event) { event.stopPropagation(); toggleLineSelected(Number(chk.getAttribute('data-line-idx'))); });
            });
            el.querySelectorAll('.vas289-qty-input').forEach(function (inp) {
                var precisionOf = function () { return wizard.lines[Number(inp.getAttribute('data-line-idx'))].precision; };
                inp.addEventListener('click', function (event) { event.stopPropagation(); });
                // QA 8.42: a whole-number UOM (precision 0) never accepts a decimal separator,
                // and no UOM accepts more decimals than its StdPrecision.
                inp.addEventListener('keydown', function (event) {
                    if (precisionOf() === 0 && (event.key === '.' || event.key === ',' || event.key === 'Decimal')) { event.preventDefault(); }
                    if (event.key === '-' || event.key === 'e' || event.key === 'E') { event.preventDefault(); }
                });
                inp.addEventListener('input', function () {
                    var p = precisionOf();
                    var v = String(inp.value || '');
                    var dot = v.indexOf('.');
                    if (dot >= 0 && v.length - dot - 1 > p) { inp.value = p === 0 ? v.substring(0, dot) : v.substring(0, dot + 1 + p); }
                });
                inp.addEventListener('change', function () {
                    var idx = Number(inp.getAttribute('data-line-idx'));
                    var line = wizard.lines[idx];
                    var val = roundTo(Number(inp.value) || 0, line.precision);
                    val = Math.max(0, Math.min(line.pendingQty, val));
                    line.qtyToOrder = val;
                    inp.value = val;
                    if (val > 0) { line.selected = true; }
                    renderStep2Footer();
                    refreshLineSelRowVisual(idx);
                });
            });
            el.querySelectorAll('.vas289-wh-select').forEach(function (sel) {
                sel.addEventListener('click', function (event) { event.stopPropagation(); });
                sel.addEventListener('change', function () {
                    var idx = Number(sel.getAttribute('data-line-idx'));
                    var line = wizard.lines[idx];
                    line.warehouseId = Number(sel.value) || 0;
                    fetchLineFreeStock(line, function () { refreshLineSelRowVisual(idx); renderStep2Footer(); });
                });
            });
            el.querySelectorAll('[data-linesel-dir]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var pages = Math.max(1, Math.ceil(wizard.lines.length / lineSelState.size));
                    linePage2 = Math.min(pages - 1, Math.max(0, linePage2 + Number(btn.getAttribute('data-linesel-dir'))));
                    drawLineSelTable();
                });
            });
        }

        function toggleLineSelected(idx) {
            var line = wizard.lines[idx];
            line.selected = !line.selected;
            if (line.selected && line.qtyToOrder <= 0) { line.qtyToOrder = line.pendingQty; }
            drawLineSelTable();
            renderStep2Footer();
        }

        function refreshLineSelRowVisual(idx) {
            // Cheap targeted refresh: re-render just the row's checkbox state stays via DOM already;
            // stock cell / qty stay accurate on next full draw. A full redraw keeps this simple and
            // correct without per-cell patching, and only happens on warehouse change / qty commit
            // (not per keystroke), matching the "don't re-render on every keystroke" rule.
            drawLineSelTable();
        }

        function fetchLineFreeStock(line, cb) {
            if (!line.isStocked) { cb(); return; }
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetFreeStock',
                type: 'GET', dataType: 'json', cache: false,
                data: { productId: line.productId, warehouseId: line.warehouseId, attributeSetInstanceId: line.asiId || 0 },
                success: function (res) {
                    var parsed = parseResponse(res);
                    line.freeStock = parsed.FreeStock === undefined ? line.freeStock : Number(parsed.FreeStock);
                    cb();
                },
                error: function () { cb(); }
            });
        }

        function renderStep2Footer() {
            var selected = wizard.selectedLines();
            var noteHtml, cls, continueEnabled;

            if (selected.length === 0) {
                noteHtml = escapeHtml(label('VAS_289_NoLinesSelected', 'No lines selected'));
                cls = 'vas289-foot-note';
                continueEnabled = false;
            } else {
                var warehouses = {};
                selected.forEach(function (l) { warehouses[l.warehouseId] = true; });
                var whCount = Object.keys(warehouses).length;
                var whName = ((wizard.formOptionsWarehouses || []).filter(function (w) { return w.Id === selected[0].warehouseId; })[0] || {}).Name || '';
                var value = selected.reduce(function (s, l) { return s + (l.qtyToOrder * l.rate); }, 0);

                if (whCount > 1) {
                    noteHtml = escapeHtml(selected.length + ' ' + label('VAS_289_LinesSelectedSuffix', 'lines selected') + ' · ' + whCount + ' ' + label('VAS_289_MultiWarehouseWarning', 'different warehouses — one sales order ships from a single warehouse'));
                    cls = 'vas289-warnnote';
                    continueEnabled = false;
                } else {
                    var shortCount = selected.filter(isShort).length;
                    if (shortCount > 0) {
                        noteHtml = escapeHtml(selected.length + ' ' + label('VAS_289_LinesSelectedSuffix', 'lines selected') + ' · ' + escapeHtml(whName) + ' · ' + formatINR(value) + ' · ' + shortCount + ' ' + label('VAS_289_ShortStockWarning', 'lines short of free stock — will backorder'));
                        cls = 'vas289-warnnote';
                    } else {
                        noteHtml = escapeHtml(selected.length + ' ' + label('VAS_289_LinesSelectedSuffix', 'lines selected') + ' · ' + escapeHtml(whName) + ' · ' + formatINR(value) + ' · ' + label('VAS_289_StockAvailable', 'stock available'));
                        cls = 'vas289-oknote';
                    }
                    continueEnabled = true;
                }
            }

            $mFoot.html('<span class="' + cls + '">' + noteHtml + '</span>' +
                '<span><button type="button" class="vas289-btn vas289-btn-primary" id="vas289-step2-continue"' + (continueEnabled ? '' : ' disabled') + '>' + escapeHtml(label('VAS_289_Continue', 'Continue')) + '</button></span>');
            $mFoot.find('#vas289-step2-continue').on('click', function () {
                if (!continueEnabled) { return; }
                var wh = selected[0].warehouseId;
                wizard.orderWarehouseId = wh;
                goToStep3();
            });
        }

        /* ============================================================
         * Step 3 - order details
         * ============================================================ */
        function goToStep3() {
            // wizard.formDefaults can already be set here even on a "first" Continue click:
            // Step 2's own line table calls ensureWarehouseOptionsLoaded() to populate the
            // "Ship From" dropdowns, which fetches this same GetOrderFormDefaults endpoint and
            // sets formDefaults/formOptions as a side effect - without ever touching
            // wizard.header. Always guarantee wizard.header is initialized here too, or
            // renderStep3() crashes on wizard.header being null.
            if (wizard.formDefaults) {
                if (!wizard.header) { wizard.header = buildInitialHeader(wizard.formDefaults); }
                else if (wizard.orderWarehouseId) { wizard.header.warehouseId = wizard.orderWarehouseId; }
                showScreen(buildStep3Cfg(), true);
                return;
            }
            showLoading(wizard.quotation.DocumentNo);
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetOrderFormDefaults',
                type: 'GET', dataType: 'json', cache: false,
                data: { quotationId: wizard.quotationId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Defaults) { showLoadError(); return; }
                    wizard.formDefaults = parsed.Defaults;
                    wizard.formOptions = parsed.Options;
                    wizard.formOptionsWarehouses = (parsed.Options && parsed.Options.Warehouses) || [];
                    if (!wizard.header) { wizard.header = buildInitialHeader(parsed.Defaults); }
                    showScreen(buildStep3Cfg(), true);
                },
                error: function () { showLoadError(); }
            });
        }

        function buildInitialHeader(defaults) {
            var o = wizard.formOptions || {};
            var payMethods = o.PaymentMethods || [];
            var payMethodId = defaults.PaymentMethodId || 0;
            if (payMethods.length && !payMethods.some(function (m) { return m.Id === payMethodId; })) { payMethodId = payMethods[0].Id; }
            var h = {
                docTypeId: defaults.DocTypeTargetId,
                docStatus: 'DR',
                dateOrdered: defaults.DateOrdered,
                poReference: (defaults.POReference || '').substring(0, 20),
                datePromised: defaults.DatePromised,
                priorityRule: defaults.PriorityRule || '',
                bpartnerId: defaults.BusinessPartnerId,
                bpartnerName: defaults.BusinessPartnerName || '',
                locationId: defaults.LocationId,
                contactId: defaults.ContactId,
                salesRepId: defaults.SalesRepId || 0,
                paymentTermId: defaults.PaymentTermId,
                // QA 8.57: never blank - quotation's / customer's method, else the first one.
                paymentMethodId: payMethodId,
                warehouseId: wizard.orderWarehouseId || defaults.WarehouseId,
                deliveryRule: defaults.DeliveryRule || '',
                deliveryViaRule: defaults.DeliveryViaRule || '',
                priceListId: defaults.PriceListId,
                currencyId: defaults.CurrencyId,
                conversionTypeId: defaults.ConversionTypeId,
                // QA 8.61: Incoterm is blank by default.
                incoTermId: 0,
                description: (defaults.Description || '').substring(0, 255)
            };
            if (h.dateOrdered && h.datePromised && h.datePromised < h.dateOrdered) { h.datePromised = h.dateOrdered; }
            syncCurrencyFromPriceList(h);
            return h;
        }

        /* QA 8.59: Currency is read-only and always the selected price list's currency. */
        function syncCurrencyFromPriceList(h) {
            var pl = ((wizard.formOptions && wizard.formOptions.PriceLists) || []).filter(function (p) { return p.Id === h.priceListId; })[0];
            if (pl && pl.CurrencyId) { h.currencyId = pl.CurrencyId; }
        }

        function buildStep3Cfg() {
            return {
                step: 3,
                title: label('VAS_289_Step3Title', 'New Sales Order · Details'),
                subtitle: label('VAS_289_Page1Of2', 'Page 1 of 2') + ' · ' + wizard.quotation.BusinessPartner + ' · ' + label('VAS_289_FromQuotationPrefix', 'from') + ' ' + wizard.quotation.DocumentNo,
                size: 'md',
                render: renderStep3
            };
        }

        function step3Stats() {
            var selected = wizard.selectedLines();
            var orderQty = 0, orderValue = 0, shortCount = 0;
            selected.forEach(function (l) {
                orderQty += Number(l.qtyToOrder) || 0;
                orderValue += (Number(l.qtyToOrder) || 0) * (Number(l.rate) || 0);
                if (isShort(l)) { shortCount++; }
            });
            var stockCover = shortCount > 0
                ? (shortCount + ' ' + label('VAS_289_LinesShortSuffix', 'line' + (shortCount > 1 ? 's' : '') + ' short of stock'))
                : label('VAS_289_AllLinesCovered', 'All lines covered');

            // QA 8.46: Customer / Segment / Lines summary cards removed from the pop-up.
            return '<div class="vas289-mstats">' +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), wizard.quotation.DocumentNo) +
                statTile(label('VAS_289_StatOrderQty', 'Order qty'), formatNumDisplay(orderQty)) +
                statTile(label('VAS_289_StatOrderValue', 'Order value'), formatINR(orderValue)) +
                statTile(label('VAS_289_StatStockCover', 'Stock cover'), stockCover) +
            '</div>';
        }

        /* placeholder: undefined/null = no blank option; a string (also '') = a blank first option.
           blankIfUnset: add the blank option only while the current value is not in the list, so
           a mandatory field is never shown silently pre-picked with a value it does not hold. */
        function selectHtml(cssClass, name, options, valueKey, labelKey, selectedValue, placeholder, blankIfUnset) {
            var found = false;
            var opts = (options || []).map(function (o) {
                var val = o[valueKey];
                var isSel = String(val) === String(selectedValue);
                if (isSel) { found = true; }
                return '<option value="' + escapeHtml(val) + '"' + (isSel ? ' selected' : '') + '>' + escapeHtml(o[labelKey]) + '</option>';
            }).join('');
            var blank = (placeholder !== undefined && placeholder !== null) || (blankIfUnset && !found);
            return '<select class="vas289-fctl ' + cssClass + '" data-field="' + name + '">' +
                (blank ? '<option value=""' + (found ? '' : ' selected') + '>' + escapeHtml(placeholder || '') + '</option>' : '') + opts + '</select>';
        }

        function fieldBlock(labelText, controlHtml, required, fieldName) {
            return '<div class="vas289-field"' + (fieldName ? ' data-field-wrap="' + fieldName + '"' : '') + '><label>' + escapeHtml(labelText) + (required ? ' <span class="vas289-req">*</span>' : '') + '</label>' + controlHtml + '</div>';
        }

        var NUMERIC_FIELDS = ['docTypeId', 'locationId', 'contactId', 'salesRepId', 'paymentTermId', 'paymentMethodId', 'warehouseId', 'priceListId', 'currencyId', 'conversionTypeId', 'incoTermId'];

        function currencyCode(currencyId) {
            var c = ((wizard.formOptions && wizard.formOptions.Currencies) || []).filter(function (x) { return x.Id === currencyId; })[0];
            return c ? c.Code : '';
        }

        function datePromisedError() {
            return label('VAS_289_DatePromisedBeforeOrdered', 'Error: Date Promised should be greater than or equal to Date Ordered');
        }

        function renderStep3() {
            var d = wizard.formDefaults, o = wizard.formOptions, h = wizard.header;

            // QA 8.53: Sales Rep is an editable dropdown of sales representatives; the quotation's
            // own rep stays selectable even when it is no longer in that list.
            var salesReps = (o.SalesReps || []).slice();
            if (h.salesRepId > 0 && h.salesRepId === d.SalesRepId && d.SalesRepName && !salesReps.some(function (r) { return r.Id === h.salesRepId; })) {
                salesReps.unshift({ Id: d.SalesRepId, Name: d.SalesRepName });
            }

            // QA 8.64: mandatory fields carry the "*" marker and are validated before continuing.
            // QA 8.45: tighter form spacing (CSS). QA 8.46: Customer/Segment/Lines cards removed.
            var body =
                step3Stats() +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupDocument', 'Document')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldDocType', 'Target Document Type'), selectHtml('', 'docTypeId', o.DocTypes, 'Id', 'Name', h.docTypeId, null, true), true, 'docTypeId') +
                    fieldBlock(label('VAS_289_FieldDocStatus', 'Document Status'), selectHtml('', 'docStatus', o.DocStatuses, 'Code', 'Name', h.docStatus), false, 'docStatus') +
                    fieldBlock(label('VAS_289_FieldPOReference', 'Order Reference'), '<input type="text" maxlength="20" class="vas289-fctl" data-field="poReference" value="' + escapeHtml(h.poReference) + '">') +
                    fieldBlock(label('VAS_289_DateOrdered', 'Date Ordered'), '<input type="date" max="9999-12-31" class="vas289-fctl" data-field="dateOrdered" value="' + escapeHtml(h.dateOrdered) + '">', true, 'dateOrdered') +
                    fieldBlock(label('VAS_289_FieldDatePromised', 'Date Promised'), '<input type="date" max="9999-12-31" class="vas289-fctl" data-field="datePromised" value="' + escapeHtml(h.datePromised) + '">', true, 'datePromised') +
                    fieldBlock(label('VAS_289_FieldPriority', 'Priority'), selectHtml('', 'priorityRule', o.PriorityRules, 'Code', 'Name', h.priorityRule, null, true), true, 'priorityRule') +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupBPartner', 'Customer And Payment')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldBusinessPartner', 'Customer'),
                        '<input type="text" class="vas289-fctl" data-cust-input list="vas289-cust-list" autocomplete="off" placeholder="' + escapeHtml(label('VAS_289_SearchCustomer', 'Search customer')) + '" title="' + escapeHtml(h.bpartnerName) + '" value="' + escapeHtml(h.bpartnerName) + '">' +
                        '<datalist id="vas289-cust-list"></datalist>', true, 'bpartnerId') +
                    fieldBlock(label('VAS_289_Location', 'Location'), selectHtml('', 'locationId', o.Locations, 'Id', 'Name', h.locationId, null, true), true, 'locationId') +
                    fieldBlock(label('VAS_289_FieldContact', 'Customer Contact'), selectHtml('', 'contactId', o.Contacts, 'Id', 'Name', h.contactId, '')) +
                    fieldBlock(label('VAS_289_FieldSalesRep', 'Sales Rep'), selectHtml('', 'salesRepId', salesReps, 'Id', 'Name', h.salesRepId, null, true), true, 'salesRepId') +
                    fieldBlock(label('VAS_289_FieldPaymentTerm', 'Payment Term'), selectHtml('', 'paymentTermId', o.PaymentTerms, 'Id', 'Name', h.paymentTermId, null, true), true, 'paymentTermId') +
                    (o.PaymentMethods && o.PaymentMethods.length ? fieldBlock(label('VAS_289_FieldPaymentMethod', 'Payment Method'), selectHtml('', 'paymentMethodId', o.PaymentMethods, 'Id', 'Name', h.paymentMethodId), true, 'paymentMethodId') : '') +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupShipping', 'Delivery And Pricing')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldWarehouse', 'Warehouse'), selectHtml('', 'warehouseId', o.Warehouses, 'Id', 'Name', h.warehouseId, null, true), true, 'warehouseId') +
                    fieldBlock(label('VAS_289_FieldShippingRule', 'Shipping Rule'), selectHtml('', 'deliveryRule', o.DeliveryRules, 'Code', 'Name', h.deliveryRule, null, true), true, 'deliveryRule') +
                    fieldBlock(label('VAS_289_FieldShippingMethod', 'Shipping Method'), selectHtml('', 'deliveryViaRule', o.DeliveryViaRules, 'Code', 'Name', h.deliveryViaRule, null, true), true, 'deliveryViaRule') +
                    fieldBlock(label('VAS_289_FieldPriceList', 'Price List'), selectHtml('', 'priceListId', o.PriceLists, 'Id', 'Name', h.priceListId, null, true), true, 'priceListId') +
                    fieldBlock(label('VAS_289_FieldCurrency', 'Currency'), '<div class="vas289-fctl vas289-readonly" data-currency-display>' + escapeHtml(currencyCode(h.currencyId)) + '&nbsp;</div>', true, 'currencyId') +
                    fieldBlock(label('VAS_289_FieldRateType', 'Currency Rate Type'), selectHtml('', 'conversionTypeId', o.ConversionTypes, 'Id', 'Name', h.conversionTypeId)) +
                    fieldBlock(label('VAS_289_FieldIncoTerm', 'Inco Term'), selectHtml('', 'incoTermId', o.IncoTerms, 'Id', 'Name', h.incoTermId, '')) +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupDescription', 'Description')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    '<div class="vas289-field vas289-field-span-all"><label>' + escapeHtml(label('VAS_289_FieldDescription', 'Description')) + '</label><input type="text" maxlength="255" class="vas289-fctl" data-field="description" value="' + escapeHtml(h.description) + '"></div>' +
                '</div>';

            $mBody.addClass('vas289-scrollable').html(body);
            $mBody.find('[data-field]').on('change', function () {
                var $f = $(this);
                var name = $f.attr('data-field');
                var prev = h[name];
                h[name] = NUMERIC_FIELDS.indexOf(name) >= 0 ? (Number($f.val()) || 0) : $f.val();
                $f.closest('.vas289-field').removeClass('vas289-invalid');

                // QA 8.49: Date Promised may not be before Date Ordered.
                if ((name === 'dateOrdered' || name === 'datePromised') && h.dateOrdered && h.datePromised && h.datePromised < h.dateOrdered) {
                    infoMsg(datePromisedError());
                    h[name] = prev;
                    $f.val(prev || '');
                    return;
                }
                if (name === 'priceListId') {
                    syncCurrencyFromPriceList(h);
                    $mBody.find('[data-currency-display]').html(escapeHtml(currencyCode(h.currencyId)) + '&nbsp;');
                }
            });
            wireCustomerSearch();

            renderStep3Footer();
        }

        /* QA 8.54: customer search (as-you-type list from the customers the Sales Order window
           offers); choosing a customer reloads its locations/contacts and auto-fills Price List,
           Payment Term, Payment Method and Sales Rep from the customer's configuration. */
        function wireCustomerSearch() {
            var $inp = $mBody.find('[data-cust-input]');
            var $list = $mBody.find('#vas289-cust-list');
            var results = [];
            var timer = null, seq = 0;

            function optionLabel(c) { return c.Name + (c.Code ? ' (' + c.Code + ')' : ''); }

            $inp.on('input', function () {
                clearTimeout(timer);
                var text = String($inp.val() || '').trim();
                if (text.length < 2) { return; }
                timer = setTimeout(function () {
                    var mySeq = ++seq;
                    VASSONet.ajax({
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'SearchCustomers',
                        type: 'GET', dataType: 'json', cache: false,
                        data: { searchText: text },
                        success: function (res) {
                            if (mySeq !== seq) { return; }
                            var parsed = parseResponse(res);
                            results = $.isArray(parsed) ? parsed : [];
                            $list.html(results.map(function (c) { return '<option value="' + escapeHtml(optionLabel(c)) + '"></option>'; }).join(''));
                        }
                    });
                }, 300);
            });

            $inp.on('change', function () {
                var h = wizard.header;
                var text = String($inp.val() || '').trim();
                var match = results.filter(function (c) { return optionLabel(c) === text; })[0] ||
                    (results.filter(function (c) { return c.Name === text; }).length === 1 ? results.filter(function (c) { return c.Name === text; })[0] : null);
                if (!match) { $inp.val(h.bpartnerName); return; }
                $inp.closest('.vas289-field').removeClass('vas289-invalid');
                if (match.Id === h.bpartnerId) { $inp.val(h.bpartnerName); return; }
                applyCustomer(match);
            });
        }

        function applyCustomer(customer) {
            var h = wizard.header, o = wizard.formOptions;
            h.bpartnerId = customer.Id;
            h.bpartnerName = customer.Name;
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetBPartnerOptions',
                type: 'GET', dataType: 'json', cache: false,
                data: { bpartnerId: customer.Id },
                success: function (res) {
                    if (!wizard || wizard.header !== h) { return; }
                    var p = parseResponse(res);
                    var inList = function (list, id) { return (list || []).some(function (x) { return x.Id === id; }); };
                    o.Locations = p.Locations || [];
                    o.Contacts = p.Contacts || [];
                    h.locationId = o.Locations.length ? o.Locations[0].Id : 0;
                    h.contactId = 0;
                    if (p.PriceListId > 0 && inList(o.PriceLists, p.PriceListId)) { h.priceListId = p.PriceListId; syncCurrencyFromPriceList(h); }
                    if (p.PaymentTermId > 0 && inList(o.PaymentTerms, p.PaymentTermId)) { h.paymentTermId = p.PaymentTermId; }
                    if (p.PaymentMethodId > 0 && inList(o.PaymentMethods, p.PaymentMethodId)) { h.paymentMethodId = p.PaymentMethodId; }
                    if (p.SalesRepId > 0 && inList(o.SalesReps, p.SalesRepId)) { h.salesRepId = p.SalesRepId; }
                    if (currentCfg && currentCfg.step === 3) {
                        var scrollTop = $mBody.scrollTop();
                        renderStep3();
                        $mBody.scrollTop(scrollTop);
                    }
                },
                error: function () { showLoadError(); }
            });
        }

        /* QA 8.64: names of the mandatory fields that are still empty. */
        function step3MissingFields() {
            var h = wizard.header, o = wizard.formOptions || {};
            var checks = [
                ['docTypeId', h.docTypeId > 0, label('VAS_289_FieldDocType', 'Target Document Type')],
                ['dateOrdered', !!h.dateOrdered, label('VAS_289_DateOrdered', 'Date Ordered')],
                ['datePromised', !!h.datePromised, label('VAS_289_FieldDatePromised', 'Date Promised')],
                ['priorityRule', !!h.priorityRule, label('VAS_289_FieldPriority', 'Priority')],
                ['bpartnerId', h.bpartnerId > 0, label('VAS_289_FieldBusinessPartner', 'Customer')],
                ['locationId', h.locationId > 0, label('VAS_289_Location', 'Location')],
                ['salesRepId', h.salesRepId > 0, label('VAS_289_FieldSalesRep', 'Sales Rep')],
                ['paymentTermId', h.paymentTermId > 0, label('VAS_289_FieldPaymentTerm', 'Payment Term')],
                ['paymentMethodId', !(o.PaymentMethods && o.PaymentMethods.length) || h.paymentMethodId > 0, label('VAS_289_FieldPaymentMethod', 'Payment Method')],
                ['warehouseId', h.warehouseId > 0, label('VAS_289_FieldWarehouse', 'Warehouse')],
                ['deliveryRule', !!h.deliveryRule, label('VAS_289_FieldShippingRule', 'Shipping Rule')],
                ['deliveryViaRule', !!h.deliveryViaRule, label('VAS_289_FieldShippingMethod', 'Shipping Method')],
                ['priceListId', h.priceListId > 0, label('VAS_289_FieldPriceList', 'Price List')],
                ['currencyId', h.currencyId > 0, label('VAS_289_FieldCurrency', 'Currency')]
            ];
            return checks.filter(function (c) { return !c[1]; });
        }

        function validateStep3() {
            var missing = step3MissingFields();
            $mBody.find('.vas289-invalid').removeClass('vas289-invalid');
            if (missing.length) {
                missing.forEach(function (m) { $mBody.find('[data-field-wrap="' + m[0] + '"]').addClass('vas289-invalid'); });
                infoMsg(label('VAS_289_FillMandatory', 'Please fill the mandatory fields:') + ' ' + missing.map(function (m) { return m[2]; }).join(', '));
                return false;
            }
            var h = wizard.header;
            if (h.datePromised < h.dateOrdered) {
                $mBody.find('[data-field-wrap="datePromised"]').addClass('vas289-invalid');
                infoMsg(datePromisedError());
                return false;
            }
            return true;
        }

        function renderStep3Footer() {
            $mFoot.html('<span class="vas289-foot-note"></span>' +
                '<span><button type="button" class="vas289-btn" id="vas289-step3-back">' + escapeHtml(label('VAS_289_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas289-btn vas289-btn-primary" id="vas289-step3-continue">' + escapeHtml(label('VAS_289_ContinueToLines', 'Continue to lines')) + '</button></span>');
            $mFoot.find('#vas289-step3-back').on('click', backModal);
            $mFoot.find('#vas289-step3-continue').on('click', function () {
                if (!validateStep3()) { return; }
                // QA 8.67: lines take exactly the Date Promised chosen here unless edited on the lines step
                // (or no longer valid against a changed Date Ordered).
                var h = wizard.header;
                wizard.selectedLines().forEach(function (l) {
                    if (!l.datePromisedEdited || !l.datePromised || l.datePromised < h.dateOrdered) {
                        l.datePromised = h.datePromised;
                        l.datePromisedEdited = false;
                    }
                });
                showScreen(buildStep4Cfg(), true);
            });
        }

        /* ============================================================
         * Step 4 - lines review + create
         * ============================================================ */
        function buildStep4Cfg() {
            var n = wizard.selectedLines().length;
            return {
                step: 4,
                title: label('VAS_289_Step4Title', 'New Sales Order · Lines'),
                subtitle: label('VAS_289_Page2Of2', 'Page 2 of 2') + ' · ' + wizard.quotation.BusinessPartner + ' · ' + n + ' ' + label('VAS_289_LineWordSuffix', n === 1 ? 'line' : 'lines'),
                size: 'md',
                fixedHeight: true,
                render: renderStep4
            };
        }

        function renderStep4() {
            var wh = ((wizard.formOptions && wizard.formOptions.Warehouses) || []).filter(function (w) { return w.Id === wizard.header.warehouseId; })[0];
            wh = (wh || {}).Name || '';

            // QA 8.46: no Lines summary card.
            var statsHtml = '<div class="vas289-mstats">' +
                statTile(label('VAS_289_FieldWarehouse', 'Warehouse'), wh) +
                statTile(label('VAS_289_FieldDatePromised', 'Date Promised'), formatDateFull(wizard.header.datePromised)) +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), wizard.quotation.DocumentNo) +
            '</div>';

            lineReviewState = lineReviewState || { size: MODAL_INITIAL_ROWS };
            $mBody.html(statsHtml + '<div class="vas289-mtwrap"><div class="vas289-mtbl" id="vas289-linerev-tbl"></div></div>' + '<div class="vas289-totline" id="vas289-totline"></div>');
            drawLineReviewTable();
            renderTotals();
            renderStep4Footer();
        }

        var REV_COLS = [
            { key: 'product', label: label('VAS_289_ColProduct', 'Product'), w: 1.6 },
            { key: 'uom', label: label('VAS_289_ColUom', 'UoM'), w: .5 },
            { key: 'qty', label: label('VAS_289_ColQtyToOrder', 'Qty to order'), w: .8, align: 'right' },
            { key: 'rate', label: label('VAS_289_ColRate', 'Rate'), w: .8, align: 'right' },
            { key: 'tax', label: label('VAS_289_ColTax', 'Tax'), w: 1 },
            { key: 'promised', label: label('VAS_289_FieldDatePromised', 'Date Promised'), w: 1 },
            { key: 'stock', label: label('VAS_289_ColStockQtyBaseUom', 'Stock Qty (In Base UOM)'), w: .95, align: 'right' },
            { key: 'desc', label: '', w: .4 }
        ];

        function drawLineReviewTable() {
            var el = document.getElementById('vas289-linerev-tbl');
            if (!el || !wizard) { return; }

            var taxes = (wizard.formOptions && wizard.formOptions.Taxes) || [];
            var selected = wizard.selectedLines();
            var tpl = REV_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var size = lineReviewState.size;
            var pages = Math.max(1, Math.ceil(selected.length / size));
            if (lineReviewPage > pages - 1) { lineReviewPage = pages - 1; }
            var start = lineReviewPage * size;
            var slice = selected.slice(start, start + size);

            var head = '<div class="vas289-mrow vas289-mhead-row" style="grid-template-columns:' + tpl + '">' +
                REV_COLS.map(function (c) { return '<span class="vas289-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var idx = wizard.lines.indexOf(line);
                var stockCell = line.isStocked ? cellHtml(formatNumDisplay(line.freeStock || 0), isShort(line) ? 'vas289-c-short' : 'vas289-c-ok', 'right') : cellHtml('', 'vas289-c-std', 'right');
                // QA 8.66: the SO tax list; a quotation tax outside it stays blank until chosen.
                var taxKnown = taxes.some(function (t) { return t.Id === line.taxId; });
                var taxOptions = (taxKnown ? '' : '<option value="" selected></option>') +
                    taxes.map(function (t) { return '<option value="' + t.Id + '"' + (t.Id === line.taxId ? ' selected' : '') + '>' + escapeHtml(t.Name) + '</option>'; }).join('');
                // QA 8.69: Description / Print Description max 255 characters.
                var descRow = wizard.descOpen[idx] ? (
                    '<div class="vas289-linedesc" style="grid-column:1/-1">' +
                        '<div class="vas289-field"><label>' + escapeHtml(label('VAS_289_GroupDescription', 'Description')) + '</label><input type="text" maxlength="255" class="vas289-fctl vas289-line-desc" data-line-idx="' + idx + '" value="' + escapeHtml(line.description) + '"></div>' +
                        '<div class="vas289-field"><label>' + escapeHtml(label('VAS_289_FieldPrintDescription', 'Print Description')) + '</label><input type="text" maxlength="255" class="vas289-fctl vas289-line-printdesc" data-line-idx="' + idx + '" value="' + escapeHtml(line.printDescription) + '"></div>' +
                    '</div>'
                ) : '';
                return '<div class="vas289-mrow" style="grid-template-columns:' + tpl + '">' +
                    cellHtml(line.productName + (line.attributeText ? ' · ' + line.attributeText : ''), 'vas289-c-prim') +
                    cellHtml(line.uomName, 'vas289-c-std') +
                    cellHtml(formatQty(line.qtyToOrder, line.precision), 'vas289-c-dark', 'right') +
                    // QA 8.65/8.75: editable rate, two decimals.
                    '<span class="vas289-cell"><input type="number" min="0" step="0.01" class="vas289-qty-input vas289-line-rate" data-line-idx="' + idx + '" value="' + formatRate(line.rate) + '"></span>' +
                    '<span class="vas289-cell"><select class="vas289-fctl vas289-line-tax" data-line-idx="' + idx + '">' + taxOptions + '</select></span>' +
                    '<span class="vas289-cell"><input type="date" max="9999-12-31" min="' + escapeHtml(wizard.header.dateOrdered || '') + '" class="vas289-fctl vas289-line-promised" data-line-idx="' + idx + '" value="' + escapeHtml(line.datePromised || wizard.header.datePromised) + '"></span>' +
                    stockCell +
                    '<span class="vas289-cell center"><button type="button" class="vas289-iconbtn vas289-line-desc-toggle" data-line-idx="' + idx + '" title="' + escapeHtml(label('VAS_289_FieldDescription', 'Description')) + '">' + icon('desc') + '</button></span>' +
                '</div>' + descRow;
            }).join('');

            var showingLabel = label('VAS_289_Showing', 'Showing') + ' ' + (selected.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_289_Of', 'of') + ' ' + selected.length;
            var foot = '<div class="vas289-mtfoot"><span class="vas289-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas289-pager">' +
                        '<button type="button" class="vas289-pbtn" data-linerev-dir="-1"' + (lineReviewPage === 0 ? ' disabled' : '') + '>' + icon('prev') + '</button>' +
                        '<span class="vas289-ptxt">' + (lineReviewPage + 1) + ' ' + escapeHtml(label('VAS_289_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas289-pbtn" data-linerev-dir="1"' + (lineReviewPage >= pages - 1 ? ' disabled' : '') + '>' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas289-mbody-rows">' + body + '</div>' + foot;
            wireLineReviewEvents(el);
        }

        function wireLineReviewEvents(el) {
            el.querySelectorAll('.vas289-line-tax').forEach(function (sel) {
                sel.addEventListener('change', function () {
                    wizard.lines[Number(sel.getAttribute('data-line-idx'))].taxId = Number(sel.value) || 0;
                    renderTotals();
                });
            });
            el.querySelectorAll('.vas289-line-rate').forEach(function (inp) {
                inp.addEventListener('keydown', function (event) {
                    if (event.key === '-' || event.key === 'e' || event.key === 'E') { event.preventDefault(); }
                });
                inp.addEventListener('change', function () {
                    var line = wizard.lines[Number(inp.getAttribute('data-line-idx'))];
                    line.rate = Math.max(0, roundTo(Number(inp.value) || 0, 2));
                    inp.value = formatRate(line.rate);
                    renderTotals();
                });
            });
            el.querySelectorAll('.vas289-line-promised').forEach(function (inp) {
                inp.addEventListener('change', function () {
                    var line = wizard.lines[Number(inp.getAttribute('data-line-idx'))];
                    // QA 8.68: same rule as the Details step - never before Date Ordered.
                    if (inp.value && wizard.header.dateOrdered && inp.value < wizard.header.dateOrdered) {
                        infoMsg(datePromisedError());
                        inp.value = line.datePromised || wizard.header.datePromised;
                        return;
                    }
                    line.datePromised = inp.value;
                    line.datePromisedEdited = true;
                });
            });
            el.querySelectorAll('.vas289-line-desc-toggle').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = Number(btn.getAttribute('data-line-idx'));
                    wizard.descOpen[idx] = !wizard.descOpen[idx];
                    drawLineReviewTable();
                });
            });
            el.querySelectorAll('.vas289-line-desc').forEach(function (inp) {
                inp.addEventListener('change', function () { wizard.lines[Number(inp.getAttribute('data-line-idx'))].description = inp.value; });
            });
            el.querySelectorAll('.vas289-line-printdesc').forEach(function (inp) {
                inp.addEventListener('change', function () { wizard.lines[Number(inp.getAttribute('data-line-idx'))].printDescription = inp.value; });
            });
            el.querySelectorAll('[data-linerev-dir]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var pages = Math.max(1, Math.ceil(wizard.selectedLines().length / lineReviewState.size));
                    lineReviewPage = Math.min(pages - 1, Math.max(0, lineReviewPage + Number(btn.getAttribute('data-linerev-dir'))));
                    drawLineReviewTable();
                });
            });
        }

        function renderTotals() {
            var taxes = (wizard.formOptions && wizard.formOptions.Taxes) || [];
            var selected = wizard.selectedLines();
            var subtotal = 0, tax = 0;
            selected.forEach(function (l) {
                var lineNet = roundTo(l.qtyToOrder * l.rate, 2);
                subtotal += lineNet;
                // QA 8.70: total tax of the order = sum of line net x the line tax's rate. Display
                // estimate only - the document model stays authoritative for the persisted amounts.
                var t = taxes.filter(function (x) { return x.Id === l.taxId; })[0];
                if (t) { tax += roundTo(lineNet * (Number(t.Rate) || 0) / 100, 2); }
            });
            var $t = document.getElementById('vas289-totline');
            if (!$t) { return; }
            $t.innerHTML =
                '<span>' + escapeHtml(label('VAS_289_TotalsSubtotal', 'Subtotal')) + '</span><b>' + escapeHtml(formatAmount(subtotal)) + '</b>' +
                '<span>' + escapeHtml(label('VAS_289_TotalsTax', 'Tax')) + '</span><b>' + escapeHtml(formatAmount(tax)) + '</b>' +
                '<span>' + escapeHtml(label('VAS_289_TotalsOrderTotal', 'Order total')) + '</span><b>' + escapeHtml(formatAmount(subtotal + tax)) + '</b>';
        }

        function formatAmount(value) {
            var code = currencyCode(wizard.header.currencyId);
            var text = (Number(value) || 0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            return code ? code + ' ' + text : text;
        }

        function renderStep4Footer() {
            $mFoot.html('<span class="vas289-foot-note" id="vas289-step4-note"></span>' +
                '<span><button type="button" class="vas289-btn" id="vas289-step4-back">' + escapeHtml(label('VAS_289_BackToDetails', 'Back to details')) + '</button> ' +
                '<button type="button" class="vas289-btn vas289-btn-primary" id="vas289-step4-create">' + escapeHtml(label('VAS_289_CreateSalesOrder', 'Create SO')) + '</button></span>');
            $mFoot.find('#vas289-step4-back').on('click', backModal);
            $mFoot.find('#vas289-step4-create').on('click', submitCreate);
        }

        /* ============================================================
         * Step 5 - create
         * ============================================================ */
        function submitCreate() {
            var $btn = $mFoot.find('#vas289-step4-create');
            var selected = wizard.selectedLines();

            // QA 8.68: every line's Date Promised must be on/after Date Ordered.
            var badDate = selected.some(function (l) { return l.datePromised && l.datePromised < wizard.header.dateOrdered; });
            if (badDate || wizard.header.datePromised < wizard.header.dateOrdered) { infoMsg(datePromisedError()); return; }

            $btn.prop('disabled', true);
            var payload = {
                QuotationId: wizard.quotationId,
                BusinessPartnerId: wizard.header.bpartnerId,
                BusinessPartnerName: wizard.header.bpartnerName,
                LocationId: wizard.header.locationId,
                ContactId: wizard.header.contactId,
                SalesRepId: wizard.header.salesRepId,
                WarehouseId: wizard.header.warehouseId,
                PriorityRule: wizard.header.priorityRule,
                DatePromised: wizard.header.datePromised,
                POReference: wizard.header.poReference,
                PaymentTermId: wizard.header.paymentTermId,
                PaymentMethodId: wizard.header.paymentMethodId,
                DeliveryRule: wizard.header.deliveryRule,
                DeliveryViaRule: wizard.header.deliveryViaRule,
                PriceListId: wizard.header.priceListId,
                CurrencyId: wizard.header.currencyId,
                ConversionTypeId: wizard.header.conversionTypeId,
                IncoTermId: wizard.header.incoTermId,
                Description: wizard.header.description,
                DocTypeId: wizard.header.docTypeId,
                DateOrdered: wizard.header.dateOrdered,
                DocStatus: wizard.header.docStatus || 'DR',
                Lines: selected.map(function (l) {
                    return {
                        QuotationLineId: l.lineId,
                        Qty: roundTo(l.qtyToOrder, l.precision),
                        Rate: roundTo(l.rate, 2),
                        TaxId: l.taxId,
                        DatePromised: l.datePromised || wizard.header.datePromised,
                        Description: l.description || '',
                        PrintDescription: l.printDescription || ''
                    };
                })
            };

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'CreateSalesOrder',
                type: 'POST', dataType: 'json', cache: false,
                data: { requestJson: JSON.stringify(payload) },
                success: function (res) {
                    var parsed = parseResponse(res);
                    $btn.prop('disabled', false);
                    if (parsed.Error) {
                        var noteText = parsed.Error;
                        if (parsed.Conflicts && parsed.Conflicts.length) {
                            noteText += ' (' + parsed.Conflicts.length + ' ' + label('VAS_289_ConflictLinesAffected', 'line' + (parsed.Conflicts.length > 1 ? 's' : '') + ' affected') + ')';
                            // Re-sync the affected lines' pending quantity so the user sees the real current state.
                            parsed.Conflicts.forEach(function (c) {
                                var line = wizard.lines.filter(function (l) { return l.lineId === c.QuotationLineId; })[0];
                                if (line) { line.pendingQty = c.CurrentPendingQty; line.qtyToOrder = Math.min(line.qtyToOrder, c.CurrentPendingQty); }
                            });
                        }
                        var $note = $('#vas289-step4-note');
                        $note.removeClass('vas289-foot-note').addClass('vas289-warnnote').text(noteText);
                        return;
                    }

                    closeModal();
                    // QA 8.72: success message with the new document number; QA 8.71: when "Completed"
                    // was chosen but the document workflow refused, the order stays Drafted and the
                    // reason is shown.
                    var msg = label('VAS_289_SOCreatedDocNo', 'Sales Order created successfully with document number:') + ' ' + (parsed.SalesOrderNumber || '');
                    if (parsed.CompleteError) {
                        msg += '. ' + label('VAS_289_SOCompleteFailed', 'It was saved as Drafted because it could not be completed:') + ' ' + parsed.CompleteError;
                    }
                    infoMsg(msg);
                    loadOpenQuotations(0);
                },
                error: function () {
                    $btn.prop('disabled', false);
                    var $note = $('#vas289-step4-note');
                    $note.removeClass('vas289-foot-note').addClass('vas289-warnnote').text(label('VAS_289_LoadError', 'Search is unavailable right now. Try again in a moment.'));
                }
            });
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadOpenQuotations(0);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas289-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadOpenQuotations(docsState.page); };
    };

    VAS.VAS_289_OpenSalesQuotationsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_289_OpenSalesQuotationsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_289_OpenSalesQuotationsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_289_OpenSalesQuotationsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_289_OpenSalesQuotationsWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
