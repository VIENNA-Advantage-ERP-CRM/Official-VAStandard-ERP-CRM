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
 *           KNOWN SIMPLIFICATIONS (disclosed, not silent): Business Partner and
 *           Sales Rep are shown read-only in Step 3 - the data model and API both
 *           accept a change (per the confirmed field mapping), but a full
 *           customer-search combobox is a separate, larger feature and out of
 *           scope for this build. Target Document Type, SO Date, Date Promised,
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
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var WIDGET_PAGE_SIZE = 6;
    var MODAL_MAX_ROWS = 10;
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
            return VIS.Msg.getMsg(key);
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
            var display = (value === null || value === undefined || value === '') ? '—' : value;
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

            $.ajax({
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
            $modal.removeClass('sm md').addClass(cfg.size || '');
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

        function fitTableGeneric(id, sizeRef, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas289-mhead-row');
            var foot = el.querySelector('.vas289-mtfoot');
            var row = el.querySelector('.vas289-mbody-rows .vas289-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 34;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
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

            $.ajax({
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
                                attributeText: l.AttributeText,
                                uomId: l.UomId,
                                uomName: l.UomName,
                                quotedQty: Number(l.QuotedQty) || 0,
                                alreadyOrderedQty: Number(l.AlreadyOrderedQty) || 0,
                                pendingQty: Number(l.PendingQty) || 0,
                                rate: Number(l.Rate) || 0,
                                taxId: l.TaxId || 0,
                                datePromised: l.DatePromised,
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
                render: renderStep2
            };
        }

        function renderStep2() {
            var q = wizard.quotation;
            var statsHtml = '<div class="vas289-mstats">' +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), q.DocumentNo) +
                statTile(label('VAS_289_StatBusinessPartner', 'Customer'), q.BusinessPartner) +
                statTile(label('VAS_289_StatBPGroup', 'Segment'), q.BusinessPartnerGroup || '—') +
                statTile(label('VAS_289_StatLines', 'Lines'), String(q.LineCount)) +
                statTile(label('VAS_289_StatQuotedQty', 'Quoted qty'), formatNumDisplay(q.QuotedQty)) +
                statTile(label('VAS_289_StatAlreadyOrdered', 'Already ordered'), formatNumDisplay(q.AlreadyOrderedQty)) +
                statTile(label('VAS_289_StatPendingQty', 'Pending qty'), formatNumDisplay(q.PendingQty)) +
                statTile(label('VAS_289_StatStatus', 'Status'), q.StatusLabel) +
            '</div>';

            lineSelState = lineSelState || { size: MODAL_MAX_ROWS };
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
            { key: 'wh', label: label('VAS_289_ColShipFrom', 'Ship from'), w: 1.1 },
            { key: 'stock', label: label('VAS_289_ColFreeStock', 'Free stock'), w: .85, align: 'right' }
        ];

        function warehouseOptionsHtml(selectedId) {
            var options = (wizard.formOptionsWarehouses || []).slice();
            return options.map(function (w) {
                return '<option value="' + w.Id + '"' + (w.Id === selectedId ? ' selected' : '') + '>' + escapeHtml(w.Name) + '</option>';
            }).join('');
        }

        function ensureWarehouseOptionsLoaded(cb) {
            if (wizard.formOptionsWarehouses) { cb(); return; }
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetOrderFormDefaults',
                type: 'GET', dataType: 'json', cache: false,
                data: { quotationId: wizard.quotationId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (!parsed.Error) {
                        wizard.formDefaults = parsed.Defaults;
                        wizard.formOptions = parsed.Options;
                        wizard.formOptionsWarehouses = (parsed.Options && parsed.Options.Warehouses) || [];
                    }
                    cb();
                },
                error: function () { cb(); }
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
                if (!line.isStocked) { stockCell = cellHtml('—', 'vas289-c-std', 'right'); }
                else {
                    var short = (line.freeStock || 0) < line.qtyToOrder;
                    stockCell = cellHtml(formatNumDisplay(line.freeStock || 0), short ? 'vas289-c-short' : 'vas289-c-ok', 'right');
                }
                return '<div class="vas289-mrow vas289-mrow-pick" data-line-idx="' + idx + '" style="grid-template-columns:' + tpl + '">' +
                    '<span class="vas289-cell center"><input type="checkbox" class="vas289-chk" data-line-idx="' + idx + '"' + (line.selected ? ' checked' : '') + ' aria-label="' + escapeHtml(line.productName) + '"></span>' +
                    cellHtml(line.productName + (line.attributeText ? ' · ' + line.attributeText : ''), 'vas289-c-prim') +
                    cellHtml(line.uomName, 'vas289-c-std') +
                    cellHtml(formatNumDisplay(line.pendingQty), 'vas289-c-dark', 'right') +
                    '<span class="vas289-cell"><input type="number" class="vas289-qty-input" data-line-idx="' + idx + '" min="0" max="' + line.pendingQty + '" step="any" value="' + line.qtyToOrder + '"></span>' +
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
                inp.addEventListener('click', function (event) { event.stopPropagation(); });
                inp.addEventListener('change', function () {
                    var idx = Number(inp.getAttribute('data-line-idx'));
                    var line = wizard.lines[idx];
                    var val = Number(inp.value) || 0;
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
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetFreeStock',
                type: 'GET', dataType: 'json', cache: false,
                data: { productId: line.productId, warehouseId: line.warehouseId, attributeSetInstanceId: 0 },
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
                var whName = (wizard.formOptionsWarehouses.filter(function (w) { return w.Id === selected[0].warehouseId; })[0] || {}).Name || '';
                var value = selected.reduce(function (s, l) { return s + (l.qtyToOrder * l.rate); }, 0);

                if (whCount > 1) {
                    noteHtml = escapeHtml(selected.length + ' ' + label('VAS_289_LinesSelectedSuffix', 'lines selected') + ' · ' + whCount + ' ' + label('VAS_289_MultiWarehouseWarning', 'different warehouses — one sales order ships from a single warehouse'));
                    cls = 'vas289-warnnote';
                    continueEnabled = false;
                } else {
                    var shortCount = selected.filter(function (l) { return l.isStocked && (l.freeStock || 0) < l.qtyToOrder; }).length;
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
                showScreen(buildStep3Cfg(), true);
                return;
            }
            showLoading(wizard.quotation.DocumentNo);
            $.ajax({
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
            return {
                docTypeId: defaults.DocTypeTargetId,
                dateOrdered: defaults.DateOrdered,
                poReference: defaults.POReference || '',
                datePromised: defaults.DatePromised,
                priorityRule: defaults.PriorityRule || '',
                locationId: defaults.LocationId,
                contactId: defaults.ContactId,
                paymentTermId: defaults.PaymentTermId,
                paymentMethodId: 0,
                warehouseId: wizard.orderWarehouseId || defaults.WarehouseId,
                deliveryRule: defaults.DeliveryRule || '',
                deliveryViaRule: defaults.DeliveryViaRule || '',
                priceListId: defaults.PriceListId,
                currencyId: defaults.CurrencyId,
                conversionTypeId: defaults.ConversionTypeId,
                incoTermId: defaults.IncoTermId,
                description: defaults.Description || ''
            };
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
                if (l.isStocked && (Number(l.freeStock) || 0) < (Number(l.qtyToOrder) || 0)) { shortCount++; }
            });
            var stockCover = shortCount > 0
                ? (shortCount + ' ' + label('VAS_289_LinesShortSuffix', 'line' + (shortCount > 1 ? 's' : '') + ' short of stock'))
                : label('VAS_289_AllLinesCovered', 'All lines covered');

            return '<div class="vas289-mstats">' +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), wizard.quotation.DocumentNo) +
                statTile(label('VAS_289_StatBusinessPartner', 'Customer'), wizard.quotation.BusinessPartner) +
                statTile(label('VAS_289_StatBPGroup', 'Segment'), wizard.quotation.BusinessPartnerGroup || '—') +
                statTile(label('VAS_289_StatLines', 'Lines'), String(selected.length)) +
                statTile(label('VAS_289_StatOrderQty', 'Order qty'), formatNumDisplay(orderQty)) +
                statTile(label('VAS_289_StatOrderValue', 'Order value'), formatINR(orderValue)) +
                statTile(label('VAS_289_StatStockCover', 'Stock cover'), stockCover) +
            '</div>';
        }

        function selectHtml(cssClass, name, options, valueKey, labelKey, selectedValue, placeholder) {
            var opts = (options || []).map(function (o) {
                var val = o[valueKey];
                var sel = String(val) === String(selectedValue) ? ' selected' : '';
                return '<option value="' + escapeHtml(val) + '"' + sel + '>' + escapeHtml(o[labelKey]) + '</option>';
            }).join('');
            return '<select class="vas289-fctl ' + cssClass + '" data-field="' + name + '">' +
                (placeholder ? '<option value="">' + escapeHtml(placeholder) + '</option>' : '') + opts + '</select>';
        }

        function fieldBlock(labelText, controlHtml, required) {
            return '<div class="vas289-field"><label>' + escapeHtml(labelText) + (required ? ' <span class="vas289-req">*</span>' : '') + '</label>' + controlHtml + '</div>';
        }

        function renderStep3() {
            var d = wizard.formDefaults, o = wizard.formOptions, h = wizard.header;
            var currencyOptsHtml = (o.Currencies || []).map(function (c) {
                return '<option value="' + c.Id + '"' + (c.Id === h.currencyId ? ' selected' : '') + '>' + escapeHtml(c.Code) + '</option>';
            }).join('');

            var body =
                step3Stats() +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupDocument', 'Document')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldDocType', 'Target Document Type'), selectHtml('', 'docTypeId', o.DocTypes, 'Id', 'Name', h.docTypeId), true) +
                    fieldBlock(label('VAS_289_FieldPOReference', 'Order Reference'), '<input type="text" class="vas289-fctl" data-field="poReference" value="' + escapeHtml(h.poReference) + '">') +
                    fieldBlock(label('VAS_289_FieldDateOrdered', 'SO Date'), '<input type="date" class="vas289-fctl" data-field="dateOrdered" value="' + escapeHtml(h.dateOrdered) + '">', true) +
                    fieldBlock(label('VAS_289_FieldDatePromised', 'Date Promised'), '<input type="date" class="vas289-fctl" data-field="datePromised" value="' + escapeHtml(h.datePromised) + '">', true) +
                    fieldBlock(label('VAS_289_FieldPriority', 'Priority'), selectHtml('', 'priorityRule', o.PriorityRules, 'Code', 'Name', h.priorityRule)) +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupBPartner', 'Customer And Payment')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldBusinessPartner', 'Customer'), '<div class="vas289-fctl vas289-readonly" title="' + escapeHtml(d.BusinessPartnerName) + '">' + escapeHtml(d.BusinessPartnerName) + '</div>', true) +
                    fieldBlock(label('VAS_289_FieldLocation', 'Ship-to Location'), selectHtml('', 'locationId', o.Locations, 'Id', 'Name', h.locationId), true) +
                    fieldBlock(label('VAS_289_FieldContact', 'Customer Contact'), selectHtml('', 'contactId', o.Contacts, 'Id', 'Name', h.contactId, '—')) +
                    fieldBlock(label('VAS_289_FieldSalesRep', 'Sales Rep'), '<div class="vas289-fctl vas289-readonly">' + escapeHtml(d.SalesRepName || '—') + '</div>') +
                    fieldBlock(label('VAS_289_FieldPaymentTerm', 'Payment Term'), selectHtml('', 'paymentTermId', o.PaymentTerms, 'Id', 'Name', h.paymentTermId), true) +
                    (o.PaymentMethods && o.PaymentMethods.length ? fieldBlock(label('VAS_289_FieldPaymentMethod', 'Payment Method'), selectHtml('', 'paymentMethodId', o.PaymentMethods, 'Id', 'Name', h.paymentMethodId, '—')) : '') +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupShipping', 'Delivery And Pricing')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    fieldBlock(label('VAS_289_FieldWarehouse', 'Warehouse'), selectHtml('', 'warehouseId', o.Warehouses, 'Id', 'Name', h.warehouseId), true) +
                    fieldBlock(label('VAS_289_FieldShippingRule', 'Shipping Rule'), selectHtml('', 'deliveryRule', o.DeliveryRules, 'Code', 'Name', h.deliveryRule)) +
                    fieldBlock(label('VAS_289_FieldShippingMethod', 'Shipping Method'), selectHtml('', 'deliveryViaRule', o.DeliveryViaRules, 'Code', 'Name', h.deliveryViaRule)) +
                    fieldBlock(label('VAS_289_FieldPriceList', 'Price List'), selectHtml('', 'priceListId', o.PriceLists, 'Id', 'Name', h.priceListId)) +
                    fieldBlock(label('VAS_289_FieldCurrency', 'Currency'), '<select class="vas289-fctl" data-field="currencyId">' + currencyOptsHtml + '</select>') +
                    fieldBlock(label('VAS_289_FieldRateType', 'Currency Rate Type'), selectHtml('', 'conversionTypeId', o.ConversionTypes, 'Id', 'Name', h.conversionTypeId)) +
                    fieldBlock(label('VAS_289_FieldIncoTerm', 'Inco Term'), selectHtml('', 'incoTermId', o.IncoTerms, 'Id', 'Name', h.incoTermId, '—')) +
                '</div>' +
                '<div class="vas289-formsec">' + escapeHtml(label('VAS_289_GroupDescription', 'Description')) + '</div>' +
                '<div class="vas289-form-grid">' +
                    '<div class="vas289-field vas289-field-span-all"><label>' + escapeHtml(label('VAS_289_FieldDescription', 'Description')) + '</label><input type="text" class="vas289-fctl" data-field="description" value="' + escapeHtml(h.description) + '"></div>' +
                '</div>';

            $mBody.addClass('vas289-scrollable').html(body);
            $mBody.find('[data-field]').on('change', function () {
                var $f = $(this);
                var name = $f.attr('data-field');
                h[name] = $f.val();
                if (name === 'docTypeId') { h.docTypeId = Number($f.val()) || 0; }
                if (name === 'locationId') { h.locationId = Number($f.val()) || 0; }
                if (name === 'contactId') { h.contactId = Number($f.val()) || 0; }
                if (name === 'paymentTermId') { h.paymentTermId = Number($f.val()) || 0; }
                if (name === 'paymentMethodId') { h.paymentMethodId = Number($f.val()) || 0; }
                if (name === 'warehouseId') { h.warehouseId = Number($f.val()) || 0; }
                if (name === 'priceListId') { h.priceListId = Number($f.val()) || 0; }
                if (name === 'currencyId') { h.currencyId = Number($f.val()) || 0; }
                if (name === 'conversionTypeId') { h.conversionTypeId = Number($f.val()) || 0; }
                if (name === 'incoTermId') { h.incoTermId = Number($f.val()) || 0; }
            });

            renderStep3Footer();
        }

        function step3Valid() {
            var h = wizard.header;
            return !!(h.docTypeId > 0 && h.dateOrdered && h.datePromised && h.locationId > 0 && h.paymentTermId > 0 && h.warehouseId > 0);
        }

        function renderStep3Footer() {
            var valid = step3Valid();
            $mFoot.html('<span class="vas289-foot-note"></span>' +
                '<span><button type="button" class="vas289-btn" id="vas289-step3-back">' + escapeHtml(label('VAS_289_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas289-btn vas289-btn-primary" id="vas289-step3-continue"' + (valid ? '' : ' disabled') + '>' + escapeHtml(label('VAS_289_ContinueToLines', 'Continue to lines')) + '</button></span>');
            $mFoot.find('#vas289-step3-back').on('click', backModal);
            $mFoot.find('#vas289-step3-continue').on('click', function () {
                if (!step3Valid()) { return; }
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
                render: renderStep4
            };
        }

        function renderStep4() {
            var selected = wizard.selectedLines();
            var wh = (wizard.formOptionsWarehouses.filter(function (w) { return w.Id === wizard.header.warehouseId; })[0] || {}).Name || '';

            var statsHtml = '<div class="vas289-mstats">' +
                statTile(label('VAS_289_ColLines', 'Lines'), String(selected.length)) +
                statTile(label('VAS_289_FieldWarehouse', 'Warehouse'), wh) +
                statTile(label('VAS_289_FieldDatePromised', 'Date Promised'), formatDateFull(wizard.header.datePromised)) +
                statTile(label('VAS_289_StatQuotation', 'Quotation'), wizard.quotation.DocumentNo) +
            '</div>';

            lineReviewState = lineReviewState || { size: MODAL_MAX_ROWS };
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
            { key: 'stock', label: label('VAS_289_ColFreeStock', 'Free stock'), w: .8, align: 'right' },
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
                var stockCell = line.isStocked ? cellHtml(formatNumDisplay(line.freeStock || 0), (line.freeStock || 0) < line.qtyToOrder ? 'vas289-c-short' : 'vas289-c-ok', 'right') : cellHtml('—', 'vas289-c-std', 'right');
                var taxOptions = taxes.map(function (t) { return '<option value="' + t.Id + '"' + (t.Id === line.taxId ? ' selected' : '') + '>' + escapeHtml(t.Name) + '</option>'; }).join('');
                var descRow = wizard.descOpen[idx] ? (
                    '<div class="vas289-linedesc" style="grid-column:1/-1">' +
                        '<div class="vas289-field"><label>' + escapeHtml(label('VAS_289_GroupDescription', 'Description')) + '</label><input type="text" class="vas289-fctl vas289-line-desc" data-line-idx="' + idx + '" value="' + escapeHtml(line.description) + '"></div>' +
                        '<div class="vas289-field"><label>' + escapeHtml(label('VAS_289_FieldPrintDescription', 'Print Description')) + '</label><input type="text" class="vas289-fctl vas289-line-printdesc" data-line-idx="' + idx + '" value="' + escapeHtml(line.printDescription) + '"></div>' +
                    '</div>'
                ) : '';
                return '<div class="vas289-mrow" style="grid-template-columns:' + tpl + '">' +
                    cellHtml(line.productName + (line.attributeText ? ' · ' + line.attributeText : ''), 'vas289-c-prim') +
                    cellHtml(line.uomName, 'vas289-c-std') +
                    cellHtml(formatNumDisplay(line.qtyToOrder), 'vas289-c-dark', 'right') +
                    cellHtml(formatINR(line.rate), 'vas289-c-std', 'right') +
                    '<span class="vas289-cell"><select class="vas289-fctl vas289-line-tax" data-line-idx="' + idx + '">' + taxOptions + '</select></span>' +
                    '<span class="vas289-cell"><input type="date" class="vas289-fctl vas289-line-promised" data-line-idx="' + idx + '" value="' + escapeHtml(line.datePromised || wizard.header.datePromised) + '"></span>' +
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
            el.querySelectorAll('.vas289-line-promised').forEach(function (inp) {
                inp.addEventListener('change', function () {
                    wizard.lines[Number(inp.getAttribute('data-line-idx'))].datePromised = inp.value;
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
                var lineNet = l.qtyToOrder * l.rate;
                subtotal += lineNet;
                var t = taxes.filter(function (x) { return x.Id === l.taxId; })[0];
                // Tax rate is not carried on the OptionItem shape - display-only estimate
                // recalculated client-side for responsiveness; the backend/document model
                // remains authoritative for the final persisted amounts.
            });
            var $t = document.getElementById('vas289-totline');
            if (!$t) { return; }
            $t.innerHTML =
                '<span>' + escapeHtml(label('VAS_289_TotalsSubtotal', 'Subtotal')) + '</span><b>' + escapeHtml(formatINR(subtotal)) + '</b>' +
                '<span>' + escapeHtml(label('VAS_289_TotalsOrderTotal', 'Order total')) + '</span><b>' + escapeHtml(formatINR(subtotal + tax)) + '</b>';
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
            $btn.prop('disabled', true);

            var selected = wizard.selectedLines();
            var payload = {
                QuotationId: wizard.quotationId,
                BusinessPartnerId: wizard.formDefaults.BusinessPartnerId,
                BusinessPartnerName: wizard.formDefaults.BusinessPartnerName,
                LocationId: wizard.header.locationId,
                ContactId: wizard.header.contactId,
                SalesRepId: wizard.formDefaults.SalesRepId,
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
                Lines: selected.map(function (l) {
                    return { QuotationLineId: l.lineId, Qty: l.qtyToOrder, TaxId: l.taxId, DatePromised: l.datePromised || wizard.header.datePromised };
                })
            };

            $.ajax({
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
                    toast(label('VAS_289_ToastPrefix', 'Sales order created for') + ' ' + parsed.BusinessPartnerName + ' · ' + parsed.LineCount + ' ' + label('VAS_289_LinesSuffix', 'lines') + ' · ' + formatINR(parsed.OrderTotal));
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
