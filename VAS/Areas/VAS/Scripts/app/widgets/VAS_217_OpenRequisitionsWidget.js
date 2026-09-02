/**
 * VAS_217_OpenRequisitionsWidget
 * Purchase Order Dashboard — Widget 15: Open Requisitions (3x3 Grid Widget)
 *
 * Summary:
 *   Lists approved purchase requisitions (DocStatus='CO') that contain remaining pending
 *   quantities to be converted into a Purchase Order. Provides a 3-step conversion flow:
 *     - Step 1: Select one requisition from the grid.
 *     - Step 2: Select pending lines, clamp order quantities, and enforce a single vendor.
 *     - Step 3: Fill PO details (Page 1) and line overrides (Page 2), then transactionally
 *               create the Purchase Order linked to the source requisition lines.
 *
 * Message Table:
 *   # | Fallback Text                                    | Message Key
 *  ---+--------------------------------------------------+---------------------------------------
 *   1 | Open Requisitions                                | VAS_OpenRequisitions
 *   2 | Approved and ready to convert into a PO          | VAS_ApprovedAndReadySub
 *   3 | lines ready                                      | VAS_LinesReady
 *   4 | Requisition                                      | VAS_Requisition
 *   5 | Lines                                            | VAS_Lines
 *   6 | Pending qty                                      | VAS_PendingQty
 *   7 | Needed by                                        | VAS_NeededBy
 *   8 | Status                                           | VAS_Status
 *   9 | Ready to PO                                      | VAS_ReadyToPO
 *  10 | Partly ordered                                   | VAS_PartlyOrdered
 *  11 | Showing                                          | VAS_Showing
 *  12 | of                                               | VAS_Of
 *  13 | select a requisition to raise a PO               | VAS_SelectReqToRaisePO
 *  14 | Req qty                                          | VAS_ReqQty
 *  15 | Already ordered                                  | VAS_AlreadyOrdered
 *  16 | Product                                          | VAS_Product
 *  17 | Attribute                                        | VAS_Attribute
 *  18 | UoM                                              | VAS_UoM
 *  19 | Qty to order                                     | VAS_QtyToOrder
 *  20 | Vendor                                           | VAS_Vendor
 *  21 | Rate                                             | VAS_Rate
 *  22 | Amount                                           | VAS_Amount
 *  23 | Tax                                              | VAS_Tax
 *  24 | Date promised                                    | VAS_DatePromised
 *  25 | Actions                                          | VAS_Actions
 *  26 | Subtotal                                         | VAS_Subtotal
 *  27 | Order total                                      | VAS_OrderTotal
 *  28 | Description                                      | VAS_Description
 *  29 | Print description                                | VAS_PrintDescription
 *  30 | Close                                            | VAS_Close
 *  31 | Back                                             | VAS_Back
 *  32 | Continue                                         | VAS_Continue
 *  33 | Cancel                                           | VAS_Cancel
 *  34 | Continue to lines                                | VAS_ContinueToLines
 *  35 | Back to details                                  | VAS_BackToDetails
 *  36 | Create PO                                        | VAS_CreatePO
 *  37 | No lines selected                                | VAS_NoLinesSelected
 *  38 | line selected                                    | VAS_LineSelected
 *  39 | lines selected                                   | VAS_LinesSelected
 *  40 | different vendors — one PO needs a single vendor | VAS_MultiVendorWarning
 *  41 | Purchase order created for                       | VAS_POCreatedFor
 *  42 | Target document type                             | VAS_TargetDocType
 *  43 | Order reference                                  | VAS_OrderReference
 *  44 | PO date                                          | VAS_PODate
 *  45 | Priority                                         | VAS_Priority
 *  46 | Vendor location                                  | VAS_VendorLocation
 *  47 | Vendor contact                                   | VAS_VendorContact
 *  48 | Payment term                                     | VAS_PaymentTerm
 *  49 | Payment method                                   | VAS_PaymentMethod
 *  50 | Warehouse                                        | VAS_Warehouse
 *  51 | Price list                                       | VAS_PriceList
 *  52 | Currency                                         | VAS_Currency
 *  53 | Currency rate type                               | VAS_CurrencyRateType
 *  54 | Incoterm                                         | VAS_Incoterm
 *  55 | Document                                         | VAS_Document
 *  56 | Vendor and payment                               | VAS_VendorAndPayment
 *  57 | Delivery and pricing                             | VAS_DeliveryAndPricing
 *  58 | Tax and description                              | VAS_TaxAndDescription
 *  59 | Source requisition                               | VAS_SourceRequisition
 *  60 | Order qty                                        | VAS_OrderQty
 *  61 | Order value                                      | VAS_OrderValue
 *  62 | New Purchase Order · Details                     | VAS_NewPODetailsTitle
 *  63 | New Purchase Order · Lines                       | VAS_NewPOLinesTitle
 *  64 | Page 1 of 2                                      | VAS_Page1Of2
 *  65 | Page 2 of 2                                      | VAS_Page2Of2
 *  66 | from                                             | VAS_From
 *  67 | Line text                                        | VAS_LineText
 *  68 | Text printed on the vendor copy                  | VAS_TextPrintedOnVendorCopy
 *  69 | Previous                                         | VAS_Previous
 *  70 | Next                                             | VAS_Next
 *  71 | Loading...                                       | VAS_Loading
 *  72 | No open requisitions found                       | VAS_NoOpenReqsFound
 *  73 | Failed to load open requisitions                 | VAS_FailedToLoadOpenReqs
 *  74 | Retry                                            | VAS_Retry
 *  75 | Standard specification                           | VAS_StandardSpecification
 *  76 | Purchase against                                 | VAS_PurchaseAgainst
 *  77 | Server error creating Purchase Order             | VAS_ServerTimeoutError
 *  78 | select the lines to raise a PO against           | VAS_SelectLinesToRaisePO
 *  79 | set quantity and vendor per line                 | VAS_SetQtyAndVendorPerLine
 *  80 | use the icons to add line description or print text | VAS_UseIconsToEditLineText
 *  81 | Standard                                         | VAS_Standard
 *  82 | Preferred Vendor                                 | VAS_PreferredVendor
 *  83 | Select line                                      | VAS_SelectLine
 *  84 | Low                                              | VAS_PriorityLow
 *  85 | Normal                                           | VAS_PriorityNormal
 *  86 | High                                             | VAS_PriorityHigh
 *  87 | Urgent                                           | VAS_PriorityUrgent
 *  88 | required · line details on the next page         | VAS_RequiredLineDetailsOnNextPage
 *  89 | Default Location                                 | VAS_DefaultLocation
 *  90 | No Contact                                       | VAS_NoContact
 *  91 | Error creating Purchase Order                    | VAS_ErrorCreatingPO
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .vis-widget-body, body')[0] || document.documentElement;
        var write = function () {
            var w = container.clientWidth || window.innerWidth;
            if (w > 0) {
                document.documentElement.style.setProperty('--dash-inline-size', w + 'px');
            }
        };

        if (!window.__vasDashInlineSizeObserver && typeof ResizeObserver !== 'undefined') {
            window.__vasDashInlineSizeObserver = new ResizeObserver(write);
            window.__vasDashInlineSizeObserver.observe(container);
            window.addEventListener('resize', write);
        }
        write();
    }

    VAS.VAS_217_OpenRequisitionsWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-217-orw-container"></div>');
        var $root = $('<div class="vas-217-orw-root"></div>');
        var $header, $statPill, $tblContainer, $tblHead, $tblBody, $footer, $helperText, $pagerContainer, $prevBtn, $nextBtn, $pageText, $busy;
        var widgetObserver = null;

        var requisitionsData = [];
        var totalPendingLinesCount = 0;
        var curSymbol = "₹";
        var curIso = "INR";

        var currentPage = 0; // 0-indexed

        /* ---- Adaptive row count (pattern proven on VAS_161 / VAS_165) ----------------------
           The card's height is fixed by the dashboard grid (3x3), so the number of rows that
           genuinely fit can be MEASURED. The source design asks for "six rows a page", which
           only holds at the size the mock was drawn at: at the real 3x3 tile the sixth row was
           clipped by .vas-217-orw-tbody's overflow while the pager still honestly claimed
           "Showing 1-6 of N".
           DEFAULT_PAGE_ROWS is therefore a SEED for the very first paint only - it guarantees
           there is a rendered row to measure. From then on pageSize is whatever actually fits
           at the current widget size, zoom level and screen resolution. */
        var DEFAULT_PAGE_ROWS = 6;
        var pageSize = DEFAULT_PAGE_ROWS;
        /* Height of one rendered data row, in px. Cached because it only moves when the widget
           is resized - the row font-size is driven by --widget-inline-size, so a WIDTH change
           shifts it just as a height change does. Cleared by the ResizeObserver. */
        var measuredRowHeight = 0;
        /* Re-entrancy guard: the refit repaint mutates the DOM the ResizeObserver watches. */
        var refittingRows = false;
        var totalPages = 1;

        // Modal Stack & PO Conversion State
        var modalStack = [];
        var currentModalCfg = null;
        var $modalHost = null;

        var activeRequisition = null;
        var activeReqLines = [];
        var selectedVendorId = 0;
        var selectedVendorName = "";
        var poHeaderValues = {};
        var poLinesState = [];
        var formLookups = null;
        var isSubmitting = false;

        // SVG Icons
        var ICON_DESC = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M8 13h8M8 17h5"/></svg>';
        var ICON_PRINT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><path d="M6 14h12v8H6z"/></svg>';

        function lbl(key, fallback) {
            if (window.VIS && VIS.Msg && VIS.Msg.getMsg) {
                var msg = VIS.Msg.getMsg(key);
                // VIS.Msg.getMsg returns "[KEY]" when the AD_Message row is missing;
                // that must fall through to the English fallback, not render as-is.
                if (msg && msg !== key && msg !== '[' + key + ']' && msg.charAt(0) !== '['
                    && msg.indexOf('**') === -1) {
                    return msg;
                }
            }
            return fallback;
        }

        function esc(s) {
            return String(s == null ? '' : s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function fmtMoney(v) {
            var val = Number(v || 0);
            var sym = curSymbol || '₹';
            if (curIso === 'INR' || sym === '₹') {
                if (Math.abs(val) >= 1e7) {
                    return sym + ' ' + (val / 1e7).toFixed(2) + ' Cr';
                }
                if (Math.abs(val) >= 1e5) {
                    return sym + ' ' + (val / 1e5).toFixed(2) + ' L';
                }
                return sym + ' ' + Math.round(val).toLocaleString('en-IN');
            } else {
                if (Math.abs(val) >= 1e6) {
                    return sym + ' ' + (val / 1e6).toFixed(2) + ' M';
                }
                if (Math.abs(val) >= 1e3) {
                    return sym + ' ' + (val / 1e3).toFixed(1) + ' k';
                }
                return sym + ' ' + Math.round(val).toLocaleString();
            }
        }

        function num(v) {
            var val = Number(v || 0);
            return (curIso === 'INR' || curSymbol === '₹')
                ? Math.round(val).toLocaleString('en-IN')
                : Math.round(val).toLocaleString();
        }

        function fIso(d) {
            var dt = d || new Date();
            var m = dt.getMonth() + 1;
            var day = dt.getDate();
            return dt.getFullYear() + '-' + (m < 10 ? '0' : '') + m + '-' + (day < 10 ? '0' : '') + day;
        }

        function isoDisp(v) {
            if (!v || v.indexOf('-') < 0) return v || '';
            var q = v.split('-');
            var MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            var mIdx = parseInt(q[1], 10) - 1;
            return q[2] + ' ' + (MONTHS[mIdx] || q[1]) + ' ' + q[0];
        }

        function toast(msg) {
            var $t = $('#vas_217_toast');
            if (!$t.length) {
                $t = $('<div id="vas_217_toast" class="vas-217-orw-toast"></div>');
                $('body').append($t);
            }
            $t.text(msg).addClass('show');
            clearTimeout($t.data('timer'));
            $t.data('timer', setTimeout(function () {
                $t.removeClass('show');
            }, 3200));
        }

        this.Initalize = function () {
            createWidgetUI();
            setupResizeObserver();
            loadOpenRequisitions();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined' || !$wrapper[0]) return;
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }

                    /* The row height follows the widget's size (font-size comes from
                       --widget-inline-size and the available height changes directly), so the
                       cached measurement is dropped and the page repainted at the new fit.
                       The guard is required because that repaint mutates the observed subtree. */
                    if (refittingRows) { return; }
                    if (!requisitionsData || requisitionsData.length === 0) { return; }

                    refittingRows = true;
                    try {
                        measuredRowHeight = 0;
                        renderGridPage();
                    } finally {
                        refittingRows = false;
                    }
                });
                widgetObserver.observe($wrapper[0]);
            } catch (e) { }
        }

        function createWidgetUI() {
            $root.empty();

            // 1. Header with Title, Subtitle and Success Pill
            $header = $('<div class="vas-217-orw-head"></div>');
            var $headTxt = $('<div class="vas-217-orw-head-txt">' +
                '<p class="vas-217-orw-title" title="' + esc(lbl('VAS_OpenRequisitions', 'Open Requisitions')) + '">' + esc(lbl('VAS_OpenRequisitions', 'Open Requisitions')) + '</p>' +
                '<p class="vas-217-orw-sub" title="' + esc(lbl('VAS_ApprovedAndReadySub', 'Approved and ready to convert into a PO')) + '">' + esc(lbl('VAS_ApprovedAndReadySub', 'Approved and ready to convert into a PO')) + '</p>' +
                '</div>');
            $statPill = $('<span class="vas-217-orw-hpill vas-217-orw-hpill-success" id="vas_217_pill">0 ' + esc(lbl('VAS_LinesReady', 'lines ready')) + '</span>');
            $header.append($headTxt).append($statPill);

            // 2. Table Grid with 5 Columns: Requisition | Lines | Pending qty | Needed by | Status (NO Department)
            $tblContainer = $('<div class="vas-217-orw-tbl"></div>');
            var R_COLS = 'minmax(0, 1.6fr) minmax(0, 0.6fr) minmax(0, 0.9fr) minmax(0, 0.9fr) minmax(0, 1fr)';
            $tblHead = $('<div class="vas-217-orw-trow vas-217-orw-thead" style="grid-template-columns:' + R_COLS + ';">' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Requisition', 'Requisition')) + '">' + esc(lbl('VAS_Requisition', 'Requisition')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_Lines', 'Lines')) + '">' + esc(lbl('VAS_Lines', 'Lines')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_PendingQty', 'Pending qty')) + '">' + esc(lbl('VAS_PendingQty', 'Pending qty')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_NeededBy', 'Needed by')) + '">' + esc(lbl('VAS_NeededBy', 'Needed by')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Status', 'Status')) + '">' + esc(lbl('VAS_Status', 'Status')) + '</span>' +
                '</div>');
            $tblBody = $('<div class="vas-217-orw-tbody"></div>');
            $tblContainer.append($tblHead).append($tblBody);

            // 3. Footer with Helper Text and Pager
            $footer = $('<div class="vas-217-orw-wfoot"></div>');
            $helperText = $('<span class="vas-217-orw-helper"></span>');
            $pagerContainer = $('<div class="vas-217-orw-pager"></div>');

            $prevBtn = $('<button type="button" class="vas-217-orw-pbtn" aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '" disabled>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>');
            $pageText = $('<span class="vas-217-orw-ptxt">1 ' + lbl('VAS_Of', 'of') + ' 1</span>');
            $nextBtn = $('<button type="button" class="vas-217-orw-pbtn" aria-label="' + esc(lbl('VAS_Next', 'Next')) + '" disabled>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>');

            $pagerContainer.append($prevBtn).append($pageText).append($nextBtn);
            $footer.append($helperText).append($pagerContainer);

            // 4. Busy Indicator
            $busy = $('<div class="vas-217-orw-busy vas-217-orw-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            $root.append($header).append($tblContainer).append($footer).append($busy);
            $wrapper.append($root);

            // Pager Click Events
            $prevBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage > 0) {
                    currentPage--;
                    renderGridPage();
                }
            });

            $nextBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage < totalPages - 1) {
                    currentPage++;
                    renderGridPage();
                }
            });

            // Requisition Row Click -> Opens Conversion Step 2
            $tblBody.on('click', '.vas-217-orw-trow.pickable', function (e) {
                e.stopPropagation();
                var reqId = parseInt($(this).data('req-id'), 10);
                if (reqId > 0) {
                    var reqObj = null;
                    for (var i = 0; i < requisitionsData.length; i++) {
                        if (requisitionsData[i].requisitionId === reqId) {
                            reqObj = requisitionsData[i];
                            break;
                        }
                    }
                    if (reqObj) {
                        openStep2LinesModal(reqObj);
                    }
                }
            });
        }

        function showBusy(show) {
            if ($busy && $busy[0]) {
                $busy.toggleClass('vas-217-orw-hidden', !show);
            }
        }

        function loadOpenRequisitions() {
            showBusy(true);
            renderSkeletonRows();

            var url = VIS.Application.contextUrl + 'VAS_217_OpenRequisitionsWidget/GetOpenRequisitions';
            $.ajax({
                url: url,
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    requisitionsData = data.rows || [];
                    var summary = data.summary || {};
                    totalPendingLinesCount = summary.totalPendingLines || 0;

                    $statPill.text(totalPendingLinesCount + ' ' + lbl('VAS_LinesReady', 'lines ready'));
                    currentPage = 0;
                    renderGridPage();
                },
                error: function () {
                    requisitionsData = [];
                    renderErrorState();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { }
            }
            return data || {};
        }

        function renderSkeletonRows() {
            $tblBody.empty();
            var R_COLS = 'minmax(0, 1.6fr) minmax(0, 0.6fr) minmax(0, 0.9fr) minmax(0, 0.9fr) minmax(0, 1fr)';
            var html = '';
            // Seeded from pageSize so the placeholder never paints more rows than fit.
            for (var i = 0; i < pageSize; i++) {
                html += '<div class="vas-217-orw-trow" style="grid-template-columns:' + R_COLS + ';">' +
                    '<span class="vas-217-orw-cell"><span style="display:block;width:70%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '<span class="vas-217-orw-cell right"><span style="display:inline-block;width:40%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '<span class="vas-217-orw-cell right"><span style="display:inline-block;width:50%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '<span class="vas-217-orw-cell"><span style="display:block;width:60%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '<span class="vas-217-orw-cell"><span style="display:block;width:65%;height:1em;background:#EAF1F7;border-radius:4px;"></span></span>' +
                    '</div>';
            }
            $tblBody.html(html);
            $helperText.text(lbl('VAS_Loading', 'Loading...'));
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevBtn.prop('disabled', true);
            $nextBtn.prop('disabled', true);
        }

        function renderErrorState() {
            $tblBody.empty();
            var errHtml = $('<div class="vas-217-orw-empty-box">' +
                '<p class="vas-217-orw-empty-msg">' + esc(lbl('VAS_FailedToLoadOpenReqs', 'Failed to load open requisitions')) + '</p>' +
                '<button type="button" class="vas-217-orw-retry-btn">' + esc(lbl('VAS_Retry', 'Retry')) + '</button>' +
                '</div>');
            errHtml.find('.vas-217-orw-retry-btn').on('click', function () {
                loadOpenRequisitions();
            });
            $tblBody.append(errHtml);
            $helperText.text('0 ' + lbl('VAS_Of', 'of') + ' 0');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevBtn.prop('disabled', true);
            $nextBtn.prop('disabled', true);
        }

        /* How many requisition rows actually fit the card at its current size.

           The CARD's height is imposed from outside (the 3x3 dashboard track), so it can be
           measured - the opposite of a content-sized dialog, where measuring the container
           would be circular because its height comes from the rows.

           Returns the CURRENT pageSize whenever it cannot measure (widget not laid out yet,
           hidden tab, no real row on screen) so an unmeasurable moment never changes the page. */
        function rowsThatFit() {
            var el = $tblBody && $tblBody[0];
            if (!el) { return pageSize; }

            var available = el.clientHeight;
            if (!available) { return pageSize; }

            if (!measuredRowHeight) {
                /* Probe a REAL data row: skeleton rows carry .vas-217-orw-trow too but never
                   .pickable, and the empty / error boxes are not rows at all. */
                var probe = el.querySelector('.vas-217-orw-trow.pickable');
                if (probe) {
                    var h = probe.getBoundingClientRect().height;
                    if (h > 0) { measuredRowHeight = h; }
                }
            }

            if (!measuredRowHeight) { return pageSize; }

            /* Half a pixel of slack absorbs sub-pixel row heights, which would otherwise round
               a row that does fit down to one that does not. Never below one row. */
            return Math.max(1, Math.floor((available + 0.5) / measuredRowHeight));
        }

        function renderGridPage(isRefit) {
            $tblBody.empty();
            var count = requisitionsData.length;

            if (count === 0) {
                var emptyHtml = '<div class="vas-217-orw-empty-box">' +
                    '<p class="vas-217-orw-empty-msg">' + esc(lbl('VAS_NoOpenReqsFound', 'No open requisitions found')) + '</p>' +
                    '</div>';
                $tblBody.html(emptyHtml);
                $helperText.text('0 ' + lbl('VAS_Of', 'of') + ' 0');
                $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            totalPages = Math.max(1, Math.ceil(count / pageSize));
            if (currentPage > totalPages - 1) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;

            var startIdx = currentPage * pageSize;
            var endIdx = Math.min(count, startIdx + pageSize);
            var pageSlice = requisitionsData.slice(startIdx, endIdx);

            var R_COLS = 'minmax(0, 1.6fr) minmax(0, 0.6fr) minmax(0, 0.9fr) minmax(0, 0.9fr) minmax(0, 1fr)';
            var rowsHtml = '';

            for (var i = 0; i < pageSlice.length; i++) {
                var r = pageSlice[i];
                var reqNo = r.requisitionNumber || ('REQ #' + r.requisitionId);
                var needShort = r.neededByShort || r.neededBy || '';
                var needDisplay = r.neededByDisplay || r.neededBy || '';

                rowsHtml += '<div class="vas-217-orw-trow pickable" data-req-id="' + r.requisitionId + '" style="grid-template-columns:' + R_COLS + ';">' +
                    '<span class="vas-217-orw-cell c-link" title="' + esc(reqNo) + '">' + esc(reqNo) + '</span>' +
                    '<span class="vas-217-orw-cell right c-dark" title="' + r.lineCount + '">' + r.lineCount + '</span>' +
                    '<span class="vas-217-orw-cell right c-dark" title="' + num(r.pendingQty) + '">' + num(r.pendingQty) + '</span>' +
                    '<span class="vas-217-orw-cell c-std" title="' + esc(needDisplay) + '">' + esc(needShort) + '</span>' +
                    '<span class="vas-217-orw-cell" title="' + esc(r.status) + '"><span class="vas-217-orw-chip ' + esc(r.statusChip) + '">' + esc(r.status) + '</span></span>' +
                    '</div>';
            }

            $tblBody.html(rowsHtml);

            /* Real rows are on screen now, so one can be measured. If the number that fits is
               not the number just rendered, adopt it and repaint once. isRefit stops the second
               pass from measuring again, so this can never loop. */
            if (!isRefit) {
                var fit = rowsThatFit();
                if (fit !== pageSize) {
                    pageSize = fit;
                    var refitPages = Math.max(1, Math.ceil(count / pageSize));
                    if (currentPage > refitPages - 1) { currentPage = refitPages - 1; }
                    if (currentPage < 0) { currentPage = 0; }
                    renderGridPage(true);
                    return;
                }
            }

            // Update footer helper and pager
            var helperString = lbl('VAS_Showing', 'Showing') + ' ' + (startIdx + 1) + '–' + endIdx + ' ' +
                lbl('VAS_Of', 'of') + ' ' + count + ' · ' + lbl('VAS_SelectReqToRaisePO', 'select a requisition to raise a PO');
            $helperText.text(helperString);
            $pageText.text((currentPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);

            $prevBtn.prop('disabled', currentPage === 0);
            $nextBtn.prop('disabled', currentPage >= totalPages - 1);
        }

        /* ============================================================
           MODAL ENGINE (Panel Foundation Standard)
           ============================================================ */

        function getModalHost() {
            if (!$modalHost || !$modalHost[0] || !document.body.contains($modalHost[0])) {
                $('#vas_217_modal_mask').remove();
                $modalHost = $('<div class="vas-217-orw-mask" id="vas_217_modal_mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-217-orw-modal" id="vas_217_modal_box">' +
                    '  <div class="vas-217-orw-modal-header">' +
                    '    <div class="vas-217-orw-mhead-left">' +
                    '      <button type="button" class="vas-217-orw-xbtn" id="vas_217_mBack" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" hidden>' +
                    '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                    '      </button>' +
                    '      <div class="vas-217-orw-htxt">' +
                    '        <h2 id="vas_217_mTitle"></h2>' +
                    '        <div class="vas-217-orw-msub" id="vas_217_mSub"></div>' +
                    '      </div>' +
                    '    </div>' +
                    '    <div class="vas-217-orw-hact">' +
                    '      <button type="button" class="vas-217-orw-xbtn" id="vas_217_mClose" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                    '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                    '      </button>' +
                    '    </div>' +
                    '  </div>' +
                    '  <div class="vas-217-orw-modal-body" id="vas_217_mBody"></div>' +
                    '  <div class="vas-217-orw-modal-foot" id="vas_217_mFoot"></div>' +
                    '</div>' +
                    '</div>');

                $('body').append($modalHost);

                $modalHost.find('#vas_217_mClose').on('click', function () {
                    closeModal();
                });

                $modalHost.find('#vas_217_mBack').on('click', function () {
                    popModal();
                });

                $modalHost.on('click', function (e) {
                    if (e.target === $modalHost[0]) {
                        closeModal();
                    }
                });

                $(document).off('keydown.vas_217_esc').on('keydown.vas_217_esc', function (e) {
                    if (e.key === 'Escape' && $modalHost.hasClass('open')) {
                        closeModal();
                    }
                });
            }
            return $modalHost;
        }

        function openModal(cfg, isBack) {
            var $host = getModalHost();
            if (!isBack) {
                if (cfg.isChild && currentModalCfg) {
                    modalStack.push(currentModalCfg);
                } else if (!cfg.isChild) {
                    modalStack = [];
                }
                currentModalCfg = cfg;
            } else {
                currentModalCfg = cfg;
            }

            var $backBtn = $host.find('#vas_217_mBack');
            if (modalStack.length > 0) {
                $backBtn.removeAttr('hidden').show();
            } else {
                $backBtn.attr('hidden', 'hidden').hide();
            }

            var $box = $host.find('#vas_217_modal_box');
            $box.removeClass('vas-217-orw-modal-sm vas-217-orw-modal-md');
            if (cfg.size === 'sm') $box.addClass('vas-217-orw-modal-sm');
            if (cfg.size === 'md') $box.addClass('vas-217-orw-modal-md');

            var $mBody = $host.find('#vas_217_mBody');
            $mBody.removeClass('compact overflowing');
            if (cfg.bodyClass) {
                $mBody.addClass(cfg.bodyClass);
            }

            $host.find('#vas_217_mTitle').text(cfg.title || '');
            $host.find('#vas_217_mSub').text(cfg.subtitle || '');

            $mBody.empty();
            if (typeof cfg.body === 'function') {
                cfg.body($mBody);
            } else if (cfg.body) {
                $mBody.html(cfg.body);
            }

            var $mFoot = $host.find('#vas_217_mFoot');
            $mFoot.empty();
            if (typeof cfg.foot === 'function') {
                cfg.foot($mFoot);
            } else if (cfg.foot) {
                $mFoot.html(cfg.foot);
            } else {
                var $defFoot = $('<span class="vas-217-orw-foot-note"></span>' +
                    '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-close">' + esc(lbl('VAS_Close', 'Close')) + '</button>');
                $defFoot.filter('.vas-217-orw-btn-close').on('click', closeModal);
                $mFoot.append($defFoot);
            }

            $host.addClass('open');

            if (cfg.afterRender) {
                cfg.afterRender($host);
            }
        }

        function popModal() {
            var prevCfg = modalStack.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            if (typeof prevCfg.reopen === 'function') {
                prevCfg.reopen();
            } else {
                openModal(prevCfg, true);
            }
        }

        function closeModal() {
            if ($modalHost) {
                $modalHost.removeClass('open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        /* ============================================================
           STEP 2: PICK PENDING LINES MODAL
           ============================================================ */

        function openStep2LinesModal(req) {
            activeRequisition = req;
            var reqId = req.requisitionId;
            var reqNo = req.requisitionNumber || ('REQ #' + reqId);
            var needDisplay = req.neededByDisplay || req.neededBy || '';

            var title = reqNo;
            var subtitle = (needDisplay ? 'needed by ' + needDisplay + ' · ' : '') + lbl('VAS_SelectLinesToRaisePO', 'select the lines to raise a PO against');

            var summaryStripHtml = '<div class="vas-217-orw-posum">' +
                '  <div><div class="l">' + esc(lbl('VAS_Requisition', 'Requisition')) + '</div><div class="v">' + esc(reqNo) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_Lines', 'Lines')) + '</div><div class="v">' + req.lineCount + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_ReqQty', 'Req qty')) + '</div><div class="v">' + num(req.requisitionQty) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_AlreadyOrdered', 'Already ordered')) + '</div><div class="v">' + (req.alreadyOrderedQty > 0 ? num(req.alreadyOrderedQty) : '—') + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_PendingQty', 'Pending qty')) + '</div><div class="v">' + num(req.pendingQty) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_NeededBy', 'Needed by')) + '</div><div class="v">' + esc(needDisplay) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_Status', 'Status')) + '</div><div class="v">' + esc(req.status) + '</div></div>' +
                '</div>' +
                '<div class="vas-217-orw-msec">' + esc(lbl('VAS_RequisitionLines', 'Requisition lines · select one or many')) + '</div>' +
                '<div class="vas-217-orw-mtwrap" id="vas_217_lines_wrap"></div>';

            openModal({
                isChild: false,
                title: title,
                subtitle: subtitle,
                body: summaryStripHtml,
                reopen: function () {
                    openStep2LinesModal(activeRequisition);
                },
                foot: function ($foot) {
                    $foot.html('<span class="vas-217-orw-foot-note" id="vas_217_step2_note">' + esc(lbl('VAS_NoLinesSelected', 'No lines selected')) + '</span>' +
                        '<span>' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-cancel">' + esc(lbl('VAS_Cancel', 'Cancel')) + '</button> ' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-primary" id="vas_217_btn_continue" disabled>' + esc(lbl('VAS_Continue', 'Continue')) + '</button>' +
                        '</span>');

                    $foot.find('.vas-217-orw-btn-cancel').on('click', closeModal);
                    $foot.find('#vas_217_btn_continue').on('click', function () {
                        proceedToStep3Details();
                    });
                },
                afterRender: function ($host) {
                    loadRequisitionLines(reqId, $host.find('#vas_217_lines_wrap'));
                }
            });
        }

        function loadRequisitionLines(reqId, $container) {
            $container.html('<div class="vas-217-orw-empty-box"><p class="vas-217-orw-empty-msg">' + esc(lbl('VAS_Loading', 'Loading...')) + '</p></div>');

            // Load lookups and lines together
            var lookupsUrl = VIS.Application.contextUrl + 'VAS_217_OpenRequisitionsWidget/GetFormLookups';
            var linesUrl = VIS.Application.contextUrl + 'VAS_217_OpenRequisitionsWidget/GetRequisitionLines';

            $.when(
                $.ajax({ url: lookupsUrl, type: 'GET', data: { requisitionId: reqId }, cache: false }),
                $.ajax({ url: linesUrl, type: 'GET', data: { requisitionId: reqId }, cache: false })
            ).done(function (lookupsRes, linesRes) {
                formLookups = parseResponse(lookupsRes[0]) || {};
                var linesData = parseResponse(linesRes[0]);
                activeReqLines = linesData.lines || [];

                // Initialize line state flags
                for (var i = 0; i < activeReqLines.length; i++) {
                    var l = activeReqLines[i];
                    l.selected = l.selected || false;
                    l.orderQty = l.orderQty || l.pendingQty;
                    l.rate = l.rate || l.requisitionRate || 0;
                    l.vendorId = l.vendorId || 0;
                    l.vendorName = l.vendorName || "";
                }

                renderPagedStep2LinesTable($container);
                syncStep2FootNote();
            }).fail(function () {
                $container.html('<div class="vas-217-orw-empty-box"><p class="vas-217-orw-empty-msg">' + esc(lbl('VAS_FailedToLoadOpenReqs', 'Failed to load requisition lines.')) + '</p></div>');
            });
        }

        function renderPagedStep2LinesTable($container) {
            $container.empty();
            var lPage = 0;
            var lPageSize = 10;
            var lTotalPages = Math.max(1, Math.ceil(activeReqLines.length / lPageSize));

            var vendors = (formLookups && formLookups.vendors) || [];

            var LINE_COLS = 'minmax(0, 0.28fr) minmax(0, 1.6fr) minmax(0, 1.25fr) minmax(0, 0.5fr) minmax(0, 0.75fr) minmax(0, 0.9fr) minmax(0, 0.75fr) minmax(0, 0.9fr) minmax(0, 1.6fr) minmax(0, 0.75fr)';

            var $tableWrap = $('<div class="vas-217-orw-mtbl"></div>');
            var $tableHead = $('<div class="vas-217-orw-mrow vas-217-orw-mhead" style="grid-template-columns:' + LINE_COLS + ';">' +
                '<span class="vas-217-orw-cell"></span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Product', 'Product')) + '">' + esc(lbl('VAS_Product', 'Product')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Attribute', 'Attribute')) + '">' + esc(lbl('VAS_Attribute', 'Attribute')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_UoM', 'UoM')) + '">' + esc(lbl('VAS_UoM', 'UoM')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_ReqQty', 'Req qty')) + '">' + esc(lbl('VAS_ReqQty', 'Req qty')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_AlreadyOrdered', 'Already ordered')) + '">' + esc(lbl('VAS_AlreadyOrdered', 'Already ordered')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_PendingQty', 'Pending')) + '">' + esc(lbl('VAS_PendingQty', 'Pending')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_QtyToOrder', 'Qty to order')) + '">' + esc(lbl('VAS_QtyToOrder', 'Qty to order')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Vendor', 'Vendor')) + '">' + esc(lbl('VAS_Vendor', 'Vendor')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_Rate', 'Rate')) + '">' + esc(lbl('VAS_Rate', 'Rate')) + '</span>' +
                '</div>');

            var $tableBody = $('<div class="vas-217-orw-mbody"></div>');
            var $tableFoot = $('<div class="vas-217-orw-mtfoot"></div>');

            $tableWrap.append($tableHead).append($tableBody).append($tableFoot);
            $container.append($tableWrap);

            function drawStep2Page() {
                $tableBody.empty();
                if (activeReqLines.length === 0) {
                    $tableBody.html('<div class="vas-217-orw-empty-box"><p class="vas-217-orw-empty-msg">' + esc(lbl('VAS_NoOpenReqsFound', 'No pending lines found')) + '</p></div>');
                    $tableFoot.html('<span class="vas-217-orw-helper">' + esc(lbl('VAS_Showing', 'Showing') + ' 0 ' + lbl('VAS_Of', 'of') + ' 0') + '</span>');
                    return;
                }

                lTotalPages = Math.max(1, Math.ceil(activeReqLines.length / lPageSize));
                if (lPage > lTotalPages - 1) lPage = lTotalPages - 1;
                if (lPage < 0) lPage = 0;

                var sIdx = lPage * lPageSize;
                var eIdx = Math.min(activeReqLines.length, sIdx + lPageSize);
                var slice = activeReqLines.slice(sIdx, eIdx);

                for (var i = 0; i < slice.length; i++) {
                    var l = slice[i];
                    var globalIdx = sIdx + i;

                    var vendorOpts = '';
                    if (vendors.length > 0) {
                        vendorOpts = vendors.map(function (v) {
                            var isSelected = (v.id === l.vendorId) || (v.name === l.vendorName);
                            return '<option value="' + v.id + '"' + (isSelected ? ' selected' : '') + '>' + esc(v.name) + '</option>';
                        }).join('');
                    } else {
                        vendorOpts = '<option value="' + (l.vendorId || 0) + '">' + esc(l.vendorName || lbl('VAS_PreferredVendor', 'Preferred Vendor')) + '</option>';
                    }

                    var rowHtml = '<div class="vas-217-orw-mrow pick' + (l.selected ? ' sel' : '') + '" data-idx="' + globalIdx + '" style="grid-template-columns:' + LINE_COLS + ';">' +
                        '<span class="vas-217-orw-cell"><input type="checkbox" class="vas-217-orw-chk chk-line" data-idx="' + globalIdx + '"' + (l.selected ? ' checked' : '') + ' aria-label="' + esc(lbl('VAS_SelectLine', 'Select line')) + '"></span>' +
                        '<span class="vas-217-orw-cell c-dark" title="' + esc(l.productName) + '">' + esc(l.productName) + '</span>' +
                        '<span class="vas-217-orw-cell c-std" title="' + esc(l.attributeDescription || lbl('VAS_Standard', 'Standard')) + '">' + esc(l.attributeDescription || lbl('VAS_Standard', 'Standard')) + '</span>' +
                        '<span class="vas-217-orw-cell c-std" title="' + esc(l.uomName) + '">' + esc(l.uomName) + '</span>' +
                        '<span class="vas-217-orw-cell right c-std" title="' + num(l.requestedQty) + '">' + num(l.requestedQty) + '</span>' +
                        '<span class="vas-217-orw-cell right ' + (l.alreadyOrderedQty > 0 ? 'c-dark' : 'c-std') + '" title="' + (l.alreadyOrderedQty > 0 ? num(l.alreadyOrderedQty) : '—') + '">' + (l.alreadyOrderedQty > 0 ? num(l.alreadyOrderedQty) : '—') + '</span>' +
                        '<span class="vas-217-orw-cell right c-prim" title="' + num(l.pendingQty) + '">' + num(l.pendingQty) + '</span>' +
                        '<span class="vas-217-orw-cell"><input class="vas-217-orw-rowin in-qty" type="number" min="1" max="' + l.pendingQty + '" value="' + (l.orderQty || l.pendingQty) + '" data-idx="' + globalIdx + '" aria-label="' + esc(lbl('VAS_QtyToOrder', 'Qty to order')) + '"></span>' +
                        '<span class="vas-217-orw-cell"><select class="vas-217-orw-rowsel sel-vend" data-idx="' + globalIdx + '" aria-label="' + esc(lbl('VAS_Vendor', 'Vendor')) + '">' + vendorOpts + '</select></span>' +
                        '<span class="vas-217-orw-cell right c-std" title="' + esc(fmtMoney(l.rate)) + '">' + esc(fmtMoney(l.rate)) + '</span>' +
                        '</div>';

                    $tableBody.append(rowHtml);
                }

                var footHelper = lbl('VAS_Showing', 'Showing') + ' ' + (sIdx + 1) + '–' + eIdx + ' ' +
                    lbl('VAS_Of', 'of') + ' ' + activeReqLines.length + ' · ' + lbl('VAS_SetQtyAndVendorPerLine', 'set quantity and vendor per line');

                var pagerHtml = '<span class="vas-217-orw-helper">' + esc(footHelper) + '</span>';
                if (lTotalPages > 1) {
                    pagerHtml += '<span class="vas-217-orw-pager">' +
                        '<button type="button" class="vas-217-orw-pbtn vas-217-m-prev"' + (lPage === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                        '<span class="vas-217-orw-ptxt">' + (lPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + lTotalPages + '</span>' +
                        '<button type="button" class="vas-217-orw-pbtn vas-217-m-next"' + (lPage >= lTotalPages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>';
                }

                $tableFoot.html(pagerHtml);

                $tableFoot.find('.vas-217-m-prev').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage > 0) { lPage--; drawStep2Page(); }
                });

                $tableFoot.find('.vas-217-m-next').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage < lTotalPages - 1) { lPage++; drawStep2Page(); }
                });
            }

            drawStep2Page();

            // Event Listeners for inputs, checkboxes, selects
            $tableBody.off('click.rowpick').on('click.rowpick', '.vas-217-orw-mrow.pick', function (e) {
                if ($(e.target).closest('input, select, button').length) return;
                var idx = parseInt($(this).data('idx'), 10);
                if (activeReqLines[idx]) {
                    activeReqLines[idx].selected = !activeReqLines[idx].selected;
                    $(this).toggleClass('sel', activeReqLines[idx].selected);
                    $(this).find('.chk-line').prop('checked', activeReqLines[idx].selected);
                    syncStep2FootNote();
                }
            });

            $tableBody.off('change.chkline').on('change.chkline', '.chk-line', function (e) {
                var idx = parseInt($(this).data('idx'), 10);
                if (activeReqLines[idx]) {
                    activeReqLines[idx].selected = $(this).prop('checked');
                    $(this).closest('.vas-217-orw-mrow').toggleClass('sel', activeReqLines[idx].selected);
                    syncStep2FootNote();
                }
            });

            $tableBody.off('change.qtyin').on('change.qtyin', '.in-qty', function (e) {
                var idx = parseInt($(this).data('idx'), 10);
                if (activeReqLines[idx]) {
                    var maxVal = activeReqLines[idx].pendingQty;
                    var val = Math.max(1, Math.min(maxVal, parseFloat($(this).val()) || 1));
                    activeReqLines[idx].orderQty = val;
                    $(this).val(val);
                    syncStep2FootNote();
                }
            });

            $tableBody.off('change.selvend').on('change.selvend', '.sel-vend', function (e) {
                var idx = parseInt($(this).data('idx'), 10);
                if (activeReqLines[idx]) {
                    activeReqLines[idx].vendorId = parseInt($(this).val(), 10) || 0;
                    activeReqLines[idx].vendorName = $(this).find('option:selected').text();
                    syncStep2FootNote();
                }
            });
        }

        function getSelectedStep2Lines() {
            return activeReqLines.filter(function (l) { return l.selected; });
        }

        function syncStep2FootNote() {
            var sel = getSelectedStep2Lines();
            var $note = $('#vas_217_step2_note');
            var $btn = $('#vas_217_btn_continue');
            if (!$note.length || !$btn.length) return;

            if (!sel.length) {
                $note.attr('class', 'vas-217-orw-foot-note').text(lbl('VAS_NoLinesSelected', 'No lines selected'));
                $btn.prop('disabled', true);
                return;
            }

            var vendorMap = {};
            for (var i = 0; i < sel.length; i++) {
                var vName = sel[i].vendorName || ('Vendor_' + sel[i].vendorId);
                vendorMap[vName] = sel[i].vendorId;
            }
            var distinctVendors = Object.keys(vendorMap);

            if (distinctVendors.length > 1) {
                $note.attr('class', 'vas-217-orw-warnnote').text(
                    sel.length + ' ' + lbl('VAS_LinesSelected', 'lines selected') + ' · ' +
                    distinctVendors.length + ' ' + lbl('VAS_MultiVendorWarning', 'different vendors — one PO needs a single vendor')
                );
                $btn.prop('disabled', true);
                return;
            }

            selectedVendorName = distinctVendors[0] || "";
            selectedVendorId = vendorMap[selectedVendorName] || 0;

            var totalVal = sel.reduce(function (acc, l) {
                return acc + ((l.orderQty || l.pendingQty) * (l.rate || 0));
            }, 0);

            $note.attr('class', 'vas-217-orw-foot-note').text(
                sel.length + ' ' + (sel.length > 1 ? lbl('VAS_LinesSelected', 'lines selected') : lbl('VAS_LineSelected', 'line selected')) +
                ' · ' + selectedVendorName + ' · ' + fmtMoney(totalVal)
            );
            $btn.prop('disabled', false);
        }

        /* ============================================================
           STEP 3: PO DETAILS (PAGE 1 OF 2) & PO LINES (PAGE 2 OF 2)
           ============================================================ */

        function proceedToStep3Details() {
            var sel = getSelectedStep2Lines();
            if (!sel.length) return;

            // Initialize PO Lines State from selected lines
            poLinesState = sel.map(function (l) {
                return {
                    requisitionLineId: l.requisitionLineId,
                    lineNo: l.lineNo,
                    productId: l.productId,
                    productName: l.productName,
                    productCode: l.productCode,
                    attributeSetInstanceId: l.attributeSetInstanceId,
                    attributeDescription: l.attributeDescription || lbl('VAS_StandardSpecification', 'Standard specification'),
                    uomId: l.uomId,
                    uomName: l.uomName,
                    orderQty: l.orderQty || l.pendingQty,
                    rate: l.rate || 0,
                    taxId: poHeaderValues.taxId || (formLookups.taxes && formLookups.taxes[0] ? formLookups.taxes[0].id : 0),
                    taxRate: (formLookups.taxes && formLookups.taxes[0] ? formLookups.taxes[0].rate : 0.18),
                    datePromised: poHeaderValues.datePromised || (activeRequisition.neededBy || fIso()),
                    description: l.description || (l.productName + ' — ' + (l.attributeDescription || lbl('VAS_Standard', 'Standard'))),
                    printDescription: l.printDescription || ''
                };
            });

            // Initialize default header values if not yet set
            if (!poHeaderValues.initialized) {
                poHeaderValues.initialized = true;
                poHeaderValues.docTypeId = (formLookups.docTypes && formLookups.docTypes[0]) ? formLookups.docTypes[0].id : 0;
                poHeaderValues.orderReference = 'REF/' + new Date().getFullYear().toString().slice(-2) + '/' + (Math.floor(100 + Math.random() * 800));
                poHeaderValues.dateOrdered = fIso();
                poHeaderValues.datePromised = activeRequisition.neededBy || fIso();
                poHeaderValues.priority = '5'; // Normal
                poHeaderValues.warehouseId = activeRequisition.warehouseId || (formLookups.warehouses && formLookups.warehouses[0] ? formLookups.warehouses[0].id : 0);
                poHeaderValues.priceListId = activeRequisition.priceListId || (formLookups.priceLists && formLookups.priceLists[0] ? formLookups.priceLists[0].id : 0);
                poHeaderValues.currencyId = activeRequisition.currencyId || (formLookups.currencies && formLookups.currencies[0] ? formLookups.currencies[0].id : 0);
                poHeaderValues.conversionTypeId = (formLookups.conversionTypes && formLookups.conversionTypes[0]) ? formLookups.conversionTypes[0].id : 0;
                poHeaderValues.incotermId = activeRequisition.incotermId || (formLookups.incoterms && formLookups.incoterms[0] ? formLookups.incoterms[0].id : 0);
                poHeaderValues.paymentTermId = (formLookups.paymentTerms && formLookups.paymentTerms[0]) ? formLookups.paymentTerms[0].id : 0;
                poHeaderValues.paymentMethod = (formLookups.paymentMethods && formLookups.paymentMethods[0]) ? formLookups.paymentMethods[0].id : '';
                poHeaderValues.taxId = (formLookups.taxes && formLookups.taxes[0]) ? formLookups.taxes[0].id : 0;
                poHeaderValues.description = lbl('VAS_PurchaseAgainst', 'Purchase against') + ' ' + activeRequisition.requisitionNumber;
            }

            openStep3DetailsPage();
        }

        function calculatePoTotals() {
            var subtotal = 0;
            var taxTotal = 0;
            var totalQty = 0;

            for (var i = 0; i < poLinesState.length; i++) {
                var l = poLinesState[i];
                var amt = l.orderQty * l.rate;
                subtotal += amt;
                taxTotal += amt * (l.taxRate || 0);
                totalQty += l.orderQty;
            }

            return {
                subtotal: subtotal,
                taxTotal: taxTotal,
                grandTotal: subtotal + taxTotal,
                totalQty: totalQty,
                lineCount: poLinesState.length
            };
        }

        function openStep3DetailsPage() {
            var totals = calculatePoTotals();
            var reqNo = activeRequisition.requisitionNumber;
            var star = '<span class="vas-217-orw-req-star">*</span>';

            var summaryStripHtml = '<div class="vas-217-orw-posum">' +
                '  <div><div class="l">' + esc(lbl('VAS_Vendor', 'Vendor')) + '</div><div class="v">' + esc(selectedVendorName) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_SourceRequisition', 'Source requisition')) + '</div><div class="v">' + esc(reqNo) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_Lines', 'Lines')) + '</div><div class="v">' + totals.lineCount + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_OrderQty', 'Order qty')) + '</div><div class="v">' + num(totals.totalQty) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_OrderValue', 'Order value')) + '</div><div class="v">' + fmtMoney(totals.subtotal) + '</div></div>' +
                '</div>';

            var docTypeOpts = (formLookups.docTypes || []).map(function (d) {
                return '<option value="' + d.id + '"' + (d.id === poHeaderValues.docTypeId ? ' selected' : '') + '>' + esc(d.name) + '</option>';
            }).join('');

            var whOpts = (formLookups.warehouses || []).map(function (w) {
                return '<option value="' + w.id + '"' + (w.id === poHeaderValues.warehouseId ? ' selected' : '') + '>' + esc(w.name) + '</option>';
            }).join('');

            var plOpts = (formLookups.priceLists || []).map(function (p) {
                return '<option value="' + p.id + '"' + (p.id === poHeaderValues.priceListId ? ' selected' : '') + '>' + esc(p.name) + '</option>';
            }).join('');

            var curOpts = (formLookups.currencies || []).map(function (c) {
                return '<option value="' + c.id + '"' + (c.id === poHeaderValues.currencyId ? ' selected' : '') + '>' + esc(c.code) + '</option>';
            }).join('');

            var convOpts = (formLookups.conversionTypes || []).map(function (c) {
                return '<option value="' + c.id + '"' + (c.id === poHeaderValues.conversionTypeId ? ' selected' : '') + '>' + esc(c.name) + '</option>';
            }).join('');

            var incoOpts = (formLookups.incoterms || []).map(function (i) {
                return '<option value="' + i.id + '"' + (i.id === poHeaderValues.incotermId ? ' selected' : '') + '>' + esc(i.name) + '</option>';
            }).join('');

            var termOpts = (formLookups.paymentTerms || []).map(function (t) {
                return '<option value="' + t.id + '"' + (t.id === poHeaderValues.paymentTermId ? ' selected' : '') + '>' + esc(t.name) + '</option>';
            }).join('');

            var methOpts = (formLookups.paymentMethods || []).map(function (m) {
                return '<option value="' + m.id + '"' + (m.id === poHeaderValues.paymentMethod ? ' selected' : '') + '>' + esc(m.name) + '</option>';
            }).join('');

            var taxOpts = (formLookups.taxes || []).map(function (t) {
                return '<option value="' + t.id + '" data-rate="' + t.rate + '"' + (t.id === poHeaderValues.taxId ? ' selected' : '') + '>' + esc(t.name) + '</option>';
            }).join('');

            var bodyHtml = summaryStripHtml +
                '<div class="vas-217-orw-formwrap">' +
                '  <div class="vas-217-orw-formsec">' + esc(lbl('VAS_Document', 'Document')) + '</div>' +
                '  <div class="vas-217-orw-form-grid">' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_TargetDocType', 'Target document type')) + star + '</label><select class="vas-217-orw-fctl" id="fDocType">' + docTypeOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_OrderReference', 'Order reference')) + '</label><input class="vas-217-orw-fctl" id="fOrderRef" value="' + esc(poHeaderValues.orderReference || '') + '"></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_PODate', 'PO date')) + star + '</label><input type="date" class="vas-217-orw-fctl" id="fPoDate" value="' + (poHeaderValues.dateOrdered || fIso()) + '"></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_DatePromised', 'Date promised')) + star + '</label><input type="date" class="vas-217-orw-fctl" id="fPromised" value="' + (poHeaderValues.datePromised || fIso()) + '"></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Priority', 'Priority')) + '</label><select class="vas-217-orw-fctl" id="fPriority">' +
                '      <option value="3"' + (poHeaderValues.priority === '3' ? ' selected' : '') + '>' + esc(lbl('VAS_PriorityLow', 'Low')) + '</option>' +
                '      <option value="5"' + (poHeaderValues.priority === '5' ? ' selected' : '') + '>' + esc(lbl('VAS_PriorityNormal', 'Normal')) + '</option>' +
                '      <option value="7"' + (poHeaderValues.priority === '7' ? ' selected' : '') + '>' + esc(lbl('VAS_PriorityHigh', 'High')) + '</option>' +
                '      <option value="1"' + (poHeaderValues.priority === '1' ? ' selected' : '') + '>' + esc(lbl('VAS_PriorityUrgent', 'Urgent')) + '</option>' +
                '    </select></div>' +
                '  </div>' +

                '  <div class="vas-217-orw-formsec">' + esc(lbl('VAS_VendorAndPayment', 'Vendor and payment')) + '</div>' +
                '  <div class="vas-217-orw-form-grid">' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Vendor', 'Vendor')) + star + '</label><input class="vas-217-orw-fctl" value="' + esc(selectedVendorName) + '" disabled></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_VendorLocation', 'Vendor location')) + star + '</label><select class="vas-217-orw-fctl" id="fVendLoc"></select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_VendorContact', 'Vendor contact')) + '</label><select class="vas-217-orw-fctl" id="fVendCon"></select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_PaymentTerm', 'Payment term')) + star + '</label><select class="vas-217-orw-fctl" id="fTerm">' + termOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_PaymentMethod', 'Payment method')) + '</label><select class="vas-217-orw-fctl" id="fMethod">' + methOpts + '</select></div>' +
                '  </div>' +

                '  <div class="vas-217-orw-formsec">' + esc(lbl('VAS_DeliveryAndPricing', 'Delivery and pricing')) + '</div>' +
                '  <div class="vas-217-orw-form-grid">' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Warehouse', 'Warehouse')) + star + '</label><select class="vas-217-orw-fctl" id="fWh">' + whOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_PriceList', 'Price list')) + '</label><select class="vas-217-orw-fctl" id="fPrice">' + plOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Currency', 'Currency')) + '</label><select class="vas-217-orw-fctl" id="fCur">' + curOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_CurrencyRateType', 'Currency rate type')) + '</label><select class="vas-217-orw-fctl" id="fRate">' + convOpts + '</select></div>' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Incoterm', 'Incoterm')) + '</label><select class="vas-217-orw-fctl" id="fInco">' + incoOpts + '</select></div>' +
                '  </div>' +

                '  <div class="vas-217-orw-formsec">' + esc(lbl('VAS_TaxAndDescription', 'Tax and description')) + '</div>' +
                '  <div class="vas-217-orw-form-grid">' +
                '    <div class="vas-217-orw-field"><label>' + esc(lbl('VAS_Tax', 'Tax')) + star + '</label><select class="vas-217-orw-fctl" id="fTax">' + taxOpts + '</select></div>' +
                '    <div class="vas-217-orw-field span-2"><label>' + ICON_DESC + esc(lbl('VAS_Description', 'Description')) + '</label><input class="vas-217-orw-fctl" id="fDesc" value="' + esc(poHeaderValues.description || '') + '"></div>' +
                '  </div>' +
                '</div>';

            openModal({
                isChild: true,
                size: 'md',
                bodyClass: 'compact',
                title: lbl('VAS_NewPODetailsTitle', 'New Purchase Order · Details'),
                subtitle: lbl('VAS_Page1Of2', 'Page 1 of 2') + ' · ' + selectedVendorName + ' · ' + lbl('VAS_From', 'from') + ' ' + reqNo,
                body: bodyHtml,
                reopen: function () {
                    openStep3DetailsPage();
                },
                foot: function ($foot) {
                    $foot.html('<span class="vas-217-orw-foot-note"><span class="vas-217-orw-req-star">*</span> ' + esc(lbl('VAS_RequiredLineDetailsOnNextPage', 'required · line details on the next page')) + '</span>' +
                        '<span>' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-back">' + esc(lbl('VAS_Back', 'Back')) + '</button> ' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-primary" id="vas_217_to_lines">' + esc(lbl('VAS_ContinueToLines', 'Continue to lines')) + '</button>' +
                        '</span>');

                    $foot.find('.vas-217-orw-btn-back').on('click', popModal);
                    $foot.find('#vas_217_to_lines').on('click', function () {
                        captureHeaderValues();
                        openStep3LinesPage();
                    });
                },
                afterRender: function ($host) {
                    loadVendorLocationsAndContacts(selectedVendorId, $host);
                    bindHeaderChangeEvents($host);
                }
            });
        }

        function loadVendorLocationsAndContacts(vendorId, $host) {
            var url = VIS.Application.contextUrl + 'VAS_217_OpenRequisitionsWidget/GetVendorLocationsAndContacts';
            $.ajax({
                url: url,
                type: 'GET',
                data: { vendorId: vendorId },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var locs = data.locations || [];
                    var cons = data.contacts || [];

                    var $locSelect = $host.find('#fVendLoc');
                    var locHtml = locs.map(function (l) {
                        return '<option value="' + l.id + '"' + (l.id === poHeaderValues.vendorLocationId ? ' selected' : '') + '>' + esc(l.name) + '</option>';
                    }).join('');
                    $locSelect.html(locHtml || '<option value="0">' + esc(lbl('VAS_DefaultLocation', 'Default Location')) + '</option>');

                    var $conSelect = $host.find('#fVendCon');
                    var conHtml = cons.map(function (c) {
                        return '<option value="' + c.id + '"' + (c.id === poHeaderValues.vendorContactId ? ' selected' : '') + '>' + esc(c.name + (c.phone ? ' · ' + c.phone : '')) + '</option>';
                    }).join('');
                    $conSelect.html(conHtml || '<option value="0">' + esc(lbl('VAS_NoContact', 'No Contact')) + '</option>');
                }
            });
        }

        function bindHeaderChangeEvents($host) {
            $host.find('#fTax').on('change', function () {
                var taxId = parseInt($(this).val(), 10) || 0;
                var rate = parseFloat($(this).find('option:selected').data('rate')) || 0.18;
                poHeaderValues.taxId = taxId;
                // Cascade default tax to all lines
                for (var i = 0; i < poLinesState.length; i++) {
                    poLinesState[i].taxId = taxId;
                    poLinesState[i].taxRate = rate;
                }
            });

            $host.find('#fPromised').on('change', function () {
                var pDate = $(this).val();
                poHeaderValues.datePromised = pDate;
                // Cascade promised date to all lines
                for (var i = 0; i < poLinesState.length; i++) {
                    poLinesState[i].datePromised = pDate;
                }
            });
        }

        function captureHeaderValues() {
            var $h = getModalHost();
            poHeaderValues.docTypeId = parseInt($h.find('#fDocType').val(), 10) || 0;
            poHeaderValues.orderReference = $h.find('#fOrderRef').val() || '';
            poHeaderValues.dateOrdered = $h.find('#fPoDate').val() || fIso();
            poHeaderValues.datePromised = $h.find('#fPromised').val() || fIso();
            poHeaderValues.priority = $h.find('#fPriority').val() || '5';
            poHeaderValues.vendorLocationId = parseInt($h.find('#fVendLoc').val(), 10) || 0;
            poHeaderValues.vendorContactId = parseInt($h.find('#fVendCon').val(), 10) || 0;
            poHeaderValues.paymentTermId = parseInt($h.find('#fTerm').val(), 10) || 0;
            poHeaderValues.paymentMethod = $h.find('#fMethod').val() || '';
            poHeaderValues.warehouseId = parseInt($h.find('#fWh').val(), 10) || 0;
            poHeaderValues.priceListId = parseInt($h.find('#fPrice').val(), 10) || 0;
            poHeaderValues.currencyId = parseInt($h.find('#fCur').val(), 10) || 0;
            poHeaderValues.conversionTypeId = parseInt($h.find('#fRate').val(), 10) || 0;
            poHeaderValues.incotermId = parseInt($h.find('#fInco').val(), 10) || 0;
            poHeaderValues.taxId = parseInt($h.find('#fTax').val(), 10) || 0;
            poHeaderValues.description = $h.find('#fDesc').val() || '';
        }

        function openStep3LinesPage() {
            var totals = calculatePoTotals();
            var whName = $('#fWh option:selected').text() || lbl('VAS_Warehouse', 'Warehouse');
            var termName = $('#fTerm option:selected').text() || lbl('VAS_PaymentTerm', 'Payment Term');
            var taxName = $('#fTax option:selected').text() || lbl('VAS_Tax', 'Tax');

            var recapStripHtml = '<div class="vas-217-orw-posum">' +
                '  <div><div class="l">' + esc(lbl('VAS_Vendor', 'Vendor')) + '</div><div class="v">' + esc(selectedVendorName) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_PODate', 'PO date')) + '</div><div class="v">' + esc(isoDisp(poHeaderValues.dateOrdered)) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_DatePromised', 'Date promised')) + '</div><div class="v">' + esc(isoDisp(poHeaderValues.datePromised)) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_Warehouse', 'Warehouse')) + '</div><div class="v">' + esc(whName) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_PaymentTerm', 'Payment term')) + '</div><div class="v">' + esc(termName) + '</div></div>' +
                '  <div><div class="l">' + esc(lbl('VAS_Tax', 'Tax')) + '</div><div class="v">' + esc(taxName) + '</div></div>' +
                '</div>' +
                '<div class="vas-217-orw-msec">' + esc(lbl('VAS_POLinesSection', 'Purchase order lines · attribute, UoM, tax, date and text can differ per line')) + '</div>' +
                '<div class="vas-217-orw-mtwrap" id="vas_217_polines_table_wrap"></div>' +
                '<div class="vas-217-orw-totline" id="vas_217_totline">' +
                '  <span>' + esc(lbl('VAS_Subtotal', 'Subtotal')) + ' <b>' + esc(fmtMoney(totals.subtotal)) + '</b></span>' +
                '  <span>' + esc(lbl('VAS_Tax', 'Tax')) + ' <b>' + esc(fmtMoney(totals.taxTotal)) + '</b></span>' +
                '  <span>' + esc(lbl('VAS_OrderTotal', 'Order total')) + ' <b>' + esc(fmtMoney(totals.grandTotal)) + '</b></span>' +
                '</div>' +
                '<div class="vas-217-orw-linedesc" id="vas_217_linedesc_box" hidden>' +
                '  <div class="ldhead">' + esc(lbl('VAS_LineText', 'Line text')) + ' · <b id="vas_217_ldName"></b></div>' +
                '  <div class="ldgrid">' +
                '    <div class="vas-217-orw-field"><label>' + ICON_DESC + esc(lbl('VAS_Description', 'Description')) + '</label><textarea class="vas-217-orw-fctl" id="vas_217_ldDesc" rows="2"></textarea></div>' +
                '    <div class="vas-217-orw-field"><label>' + ICON_PRINT + esc(lbl('VAS_PrintDescription', 'Print description')) + '</label><textarea class="vas-217-orw-fctl" id="vas_217_ldPrint" rows="2" placeholder="' + esc(lbl('VAS_TextPrintedOnVendorCopy', 'Text printed on the vendor copy')) + '"></textarea></div>' +
                '  </div>' +
                '</div>';

            openModal({
                isChild: true,
                bodyClass: 'compact',
                title: lbl('VAS_NewPOLinesTitle', 'New Purchase Order · Lines'),
                subtitle: lbl('VAS_Page2Of2', 'Page 2 of 2') + ' · ' + selectedVendorName + ' · ' + poLinesState.length + ' ' + (poLinesState.length > 1 ? lbl('VAS_LinesSelected', 'lines') : lbl('VAS_LineSelected', 'line')),
                body: recapStripHtml,
                reopen: function () {
                    openStep3LinesPage();
                },
                foot: function ($foot) {
                    $foot.html('<span class="vas-217-orw-foot-note" id="vas_217_step3_footnote">' + poLinesState.length + ' ' + (poLinesState.length > 1 ? esc(lbl('VAS_Lines', 'lines')) : esc(lbl('VAS_LineSelected', 'line'))) + ' · ' + num(totals.totalQty) + ' ' + esc(lbl('VAS_Qty', 'qty')) + ' · ' + esc(lbl('VAS_OrderTotal', 'order total')) + ' ' + fmtMoney(totals.grandTotal) + '</span>' +
                        '<span>' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-back">' + esc(lbl('VAS_BackToDetails', 'Back to details')) + '</button> ' +
                        '<button type="button" class="vas-217-orw-btn vas-217-orw-btn-primary" id="vas_217_btn_create_po">' + esc(lbl('VAS_CreatePO', 'Create PO')) + '</button>' +
                        '</span>');

                    $foot.find('.vas-217-orw-btn-back').on('click', popModal);
                    $foot.find('#vas_217_btn_create_po').on('click', function () {
                        submitCreatePurchaseOrder();
                    });
                },
                afterRender: function ($host) {
                    renderPagedStep3LinesTable($host.find('#vas_217_polines_table_wrap'), $host);
                }
            });
        }

        function renderPagedStep3LinesTable($container, $host) {
            $container.empty();
            var lPage = 0;
            var lPageSize = 10;
            var lTotalPages = Math.max(1, Math.ceil(poLinesState.length / lPageSize));

            var taxes = (formLookups && formLookups.taxes) || [];

            var PO_LINE_COLS = 'minmax(0, 1.45fr) minmax(0, 1.35fr) minmax(0, 0.75fr) minmax(0, 0.6fr) minmax(0, 0.7fr) minmax(0, 0.9fr) minmax(0, 0.85fr) minmax(0, 1.05fr) minmax(0, 0.6fr)';

            var $tableWrap = $('<div class="vas-217-orw-mtbl"></div>');
            var $tableHead = $('<div class="vas-217-orw-mrow vas-217-orw-mhead" style="grid-template-columns:' + PO_LINE_COLS + ';">' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Product', 'Product')) + '">' + esc(lbl('VAS_Product', 'Product')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Attribute', 'Attribute')) + '">' + esc(lbl('VAS_Attribute', 'Attribute')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_UoM', 'UoM')) + '">' + esc(lbl('VAS_UoM', 'UoM')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_Qty', 'Qty')) + '">' + esc(lbl('VAS_Qty', 'Qty')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_Rate', 'Rate')) + '">' + esc(lbl('VAS_Rate', 'Rate')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_Tax', 'Tax')) + '">' + esc(lbl('VAS_Tax', 'Tax')) + '</span>' +
                '<span class="vas-217-orw-cell right" title="' + esc(lbl('VAS_Amount', 'Amount')) + '">' + esc(lbl('VAS_Amount', 'Amount')) + '</span>' +
                '<span class="vas-217-orw-cell" title="' + esc(lbl('VAS_DatePromised', 'Date promised')) + '">' + esc(lbl('VAS_DatePromised', 'Date promised')) + '</span>' +
                '<span class="vas-217-orw-cell"></span>' +
                '</div>');

            var $tableBody = $('<div class="vas-217-orw-mbody"></div>');
            var $tableFoot = $('<div class="vas-217-orw-mtfoot"></div>');

            $tableWrap.append($tableHead).append($tableBody).append($tableFoot);
            $container.append($tableWrap);

            function drawStep3Page() {
                $tableBody.empty();
                lTotalPages = Math.max(1, Math.ceil(poLinesState.length / lPageSize));
                if (lPage > lTotalPages - 1) lPage = lTotalPages - 1;
                if (lPage < 0) lPage = 0;

                var sIdx = lPage * lPageSize;
                var eIdx = Math.min(poLinesState.length, sIdx + lPageSize);
                var slice = poLinesState.slice(sIdx, eIdx);

                for (var i = 0; i < slice.length; i++) {
                    var l = slice[i];
                    var globalIdx = sIdx + i;
                    var amt = l.orderQty * l.rate;

                    var taxOptionsHtml = taxes.map(function (t) {
                        return '<option value="' + t.id + '" data-rate="' + t.rate + '"' + (t.id === l.taxId ? ' selected' : '') + '>' + esc(t.name) + '</option>';
                    }).join('');

                    var rowHtml = '<div class="vas-217-orw-mrow" data-idx="' + globalIdx + '" style="grid-template-columns:' + PO_LINE_COLS + ';">' +
                        '<span class="vas-217-orw-cell c-dark" title="' + esc(l.productName) + '">' + esc(l.productName) + '</span>' +
                        '<span class="vas-217-orw-cell" title="' + esc(l.attributeDescription || lbl('VAS_Standard', 'Standard')) + '">' + esc(l.attributeDescription || lbl('VAS_Standard', 'Standard')) + '</span>' +
                        '<span class="vas-217-orw-cell" title="' + esc(l.uomName) + '">' + esc(l.uomName) + '</span>' +
                        '<span class="vas-217-orw-cell right c-dark" title="' + num(l.orderQty) + '">' + num(l.orderQty) + '</span>' +
                        '<span class="vas-217-orw-cell right c-std" title="' + esc(fmtMoney(l.rate)) + '">' + esc(fmtMoney(l.rate)) + '</span>' +
                        '<span class="vas-217-orw-cell"><select class="vas-217-orw-rowsel sel-ltax" data-idx="' + globalIdx + '" aria-label="' + esc(lbl('VAS_Tax', 'Tax')) + '">' + taxOptionsHtml + '</select></span>' +
                        '<span class="vas-217-orw-cell right c-emph" title="' + esc(fmtMoney(amt)) + '">' + esc(fmtMoney(amt)) + '</span>' +
                        '<span class="vas-217-orw-cell"><input class="vas-217-orw-rowin in-ldate" type="date" value="' + (l.datePromised || fIso()) + '" data-idx="' + globalIdx + '" aria-label="' + esc(lbl('VAS_DatePromised', 'Date promised')) + '"></span>' +
                        '<span class="vas-217-orw-cell vas-217-orw-actcell">' +
                        '  <button type="button" class="vas-217-orw-iconbtn sm btn-act-desc" data-idx="' + globalIdx + '" title="' + esc(lbl('VAS_Description', 'Description')) + '">' + ICON_DESC + '</button>' +
                        '  <button type="button" class="vas-217-orw-iconbtn sm btn-act-print" data-idx="' + globalIdx + '" title="' + esc(lbl('VAS_PrintDescription', 'Print description')) + '">' + ICON_PRINT + '</button>' +
                        '</span>' +
                        '</div>';

                    $tableBody.append(rowHtml);
                }

                var footHelper = lbl('VAS_Showing', 'Showing') + ' ' + (sIdx + 1) + '–' + eIdx + ' ' +
                    lbl('VAS_Of', 'of') + ' ' + poLinesState.length + ' · ' + lbl('VAS_UseIconsToEditLineText', 'use the icons to add line description or print text');

                var pagerHtml = '<span class="vas-217-orw-helper">' + esc(footHelper) + '</span>';
                if (lTotalPages > 1) {
                    pagerHtml += '<span class="vas-217-orw-pager">' +
                        '<button type="button" class="vas-217-orw-pbtn vas-217-m-prev"' + (lPage === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                        '<span class="vas-217-orw-ptxt">' + (lPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + lTotalPages + '</span>' +
                        '<button type="button" class="vas-217-orw-pbtn vas-217-m-next"' + (lPage >= lTotalPages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>';
                }

                $tableFoot.html(pagerHtml);

                $tableFoot.find('.vas-217-m-prev').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage > 0) { lPage--; drawStep3Page(); }
                });

                $tableFoot.find('.vas-217-m-next').on('click', function (e) {
                    e.stopPropagation();
                    if (lPage < lTotalPages - 1) { lPage++; drawStep3Page(); }
                });
            }

            drawStep3Page();

            // Line overrides
            $tableBody.off('change.ltax').on('change.ltax', '.sel-ltax', function () {
                var idx = parseInt($(this).data('idx'), 10);
                if (poLinesState[idx]) {
                    poLinesState[idx].taxId = parseInt($(this).val(), 10) || 0;
                    poLinesState[idx].taxRate = parseFloat($(this).find('option:selected').data('rate')) || 0.18;
                    refreshStep3Totals($host);
                }
            });

            $tableBody.off('change.ldate').on('change.ldate', '.in-ldate', function () {
                var idx = parseInt($(this).data('idx'), 10);
                if (poLinesState[idx]) {
                    poLinesState[idx].datePromised = $(this).val();
                }
            });

            // Action buttons -> Line Description Expander
            $tableBody.off('click.actdesc').on('click.actdesc', '.btn-act-desc', function (e) {
                e.stopPropagation();
                var idx = parseInt($(this).data('idx'), 10);
                showLineDescExpander(idx, false, $host);
            });

            $tableBody.off('click.actprint').on('click.actprint', '.btn-act-print', function (e) {
                e.stopPropagation();
                var idx = parseInt($(this).data('idx'), 10);
                showLineDescExpander(idx, true, $host);
            });
        }

        function refreshStep3Totals($host) {
            var totals = calculatePoTotals();
            var $totline = $host.find('#vas_217_totline');
            if ($totline.length) {
                $totline.html(
                    '<span>' + esc(lbl('VAS_Subtotal', 'Subtotal')) + ' <b>' + esc(fmtMoney(totals.subtotal)) + '</b></span>' +
                    '<span>' + esc(lbl('VAS_Tax', 'Tax')) + ' <b>' + esc(fmtMoney(totals.taxTotal)) + '</b></span>' +
                    '<span>' + esc(lbl('VAS_OrderTotal', 'Order total')) + ' <b>' + esc(fmtMoney(totals.grandTotal)) + '</b></span>'
                );
            }

            var $footNote = $host.find('#vas_217_step3_footnote');
            if ($footNote.length) {
                $footNote.text(poLinesState.length + ' ' + (poLinesState.length > 1 ? lbl('VAS_Lines', 'lines') : lbl('VAS_LineSelected', 'line')) + ' · ' + num(totals.totalQty) + ' ' + lbl('VAS_Qty', 'qty') + ' · ' + lbl('VAS_OrderTotal', 'order total') + ' ' + fmtMoney(totals.grandTotal));
            }
        }

        function showLineDescExpander(idx, focusPrint, $host) {
            var line = poLinesState[idx];
            if (!line) return;

            var $box = $host.find('#vas_217_linedesc_box');
            $box.removeAttr('hidden').show();

            $host.find('#vas_217_ldName').text(line.productName + ' · ' + (line.attributeDescription || lbl('VAS_Standard', 'Standard')));
            var $desc = $host.find('#vas_217_ldDesc');
            var $print = $host.find('#vas_217_ldPrint');

            $desc.val(line.description || '');
            $print.val(line.printDescription || '');

            $desc.off('input.ld').on('input.ld', function () {
                line.description = $(this).val();
            });

            $print.off('input.lp').on('input.lp', function () {
                line.printDescription = $(this).val();
            });

            if (focusPrint) {
                $print.focus();
            } else {
                $desc.focus();
            }
        }

        /* ============================================================
           TRANSACTIONAL CREATE PURCHASE ORDER SUBMISSION
           ============================================================ */

        function submitCreatePurchaseOrder() {
            if (isSubmitting) return;

            var $btn = $('#vas_217_btn_create_po');
            $btn.prop('disabled', true);
            isSubmitting = true;

            var totals = calculatePoTotals();
            var payloadLines = poLinesState.map(function (l) {
                return {
                    requisitionLineId: l.requisitionLineId,
                    lineNo: l.lineNo,
                    productId: l.productId,
                    attributeSetInstanceId: l.attributeSetInstanceId,
                    uomId: l.uomId,
                    qty: l.orderQty,
                    rate: l.rate,
                    taxId: l.taxId,
                    datePromised: l.datePromised,
                    description: l.description,
                    printDescription: l.printDescription
                };
            });

            var postData = {
                requisitionId: activeRequisition.requisitionId,
                vendorId: selectedVendorId,
                vendorLocationId: poHeaderValues.vendorLocationId || 0,
                vendorContactId: poHeaderValues.vendorContactId || 0,
                warehouseId: poHeaderValues.warehouseId || 0,
                docTypeId: poHeaderValues.docTypeId || 0,
                paymentTermId: poHeaderValues.paymentTermId || 0,
                paymentMethod: poHeaderValues.paymentMethod || '',
                priceListId: poHeaderValues.priceListId || 0,
                currencyId: poHeaderValues.currencyId || 0,
                conversionTypeId: poHeaderValues.conversionTypeId || 0,
                incotermId: poHeaderValues.incotermId || 0,
                orderReference: poHeaderValues.orderReference || '',
                dateOrdered: poHeaderValues.dateOrdered || fIso(),
                datePromised: poHeaderValues.datePromised || fIso(),
                priority: poHeaderValues.priority || '5',
                description: poHeaderValues.description || '',
                defaultTaxId: poHeaderValues.taxId || 0,
                linesJson: JSON.stringify(payloadLines)
            };

            var url = VIS.Application.contextUrl + 'VAS_217_OpenRequisitionsWidget/CreatePurchaseOrder';

            $.ajax({
                url: url,
                type: 'POST',
                data: postData,
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.success) {
                        closeModal();
                        loadOpenRequisitions();

                        // Confirmation toast
                        var lineCountStr = payloadLines.length + ' ' + (payloadLines.length > 1 ? lbl('VAS_Lines', 'lines') : lbl('VAS_LineSelected', 'line'));
                        toast(lbl('VAS_POCreatedFor', 'Purchase order created for') + ' ' + selectedVendorName + ' · ' + lineCountStr + ' · ' + fmtMoney(totals.grandTotal));
                    } else {
                        var errMsg = (data && data.error) ? data.error : (data && data.message ? data.message : lbl('VAS_ErrorCreatingPO', 'Error creating Purchase Order'));
                        alert(errMsg);
                    }
                },
                error: function (xhr, status, error) {
                    alert(lbl('VAS_ServerTimeoutError', 'Server error creating Purchase Order: ') + (error || status));
                },
                complete: function () {
                    isSubmitting = false;
                    $btn.prop('disabled', false);
                }
            });
        }

        /* ============================================================
           PO RECORD NAVIGATION
           ============================================================ */

        function openPurchaseOrderRecord(orderId) {
            if (!orderId) return;
            closeModal();

            var ZOOM_WINDOW_NAME = 'VAS_PurchaseOrder';
            var ZOOM_WINDOW_FALLBACK = 'Purchase Order';
            var ZOOM_TABLE = 'C_Order';

            var navigated = false;
            try {
                if ($self.listener && typeof $self.widgetFirevalueChanged === 'function') {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + orderId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "AD_Tab_ID": 1002398,
                        "ActionName": ZOOM_WINDOW_NAME,
                        "ActionType": "W"
                    });
                    navigated = true;
                }
            } catch (e) { }

            if (!navigated) {
                try {
                    if (window.VAS && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === 'function') {
                        VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", orderId, 0, ZOOM_WINDOW_NAME, ZOOM_WINDOW_FALLBACK);
                    } else if (window.VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(259, orderId);
                    } else if (window.VIS && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        var windowId = (VIS.context && VIS.context.getWindowId)
                            ? (VIS.context.getWindowId(ZOOM_WINDOW_NAME) || VIS.context.getWindowId(ZOOM_TABLE) || 181)
                            : 181;
                        var query = new VIS.Query();
                        query.addRestriction("C_Order_ID", VIS.Query.prototype.EQUAL, orderId);
                        VIS.viewManager.startWindow(windowId, query);
                    }
                } catch (e2) { }
            }
        }

        /* ============================================================
           WIDGET LIFECYCLE & CONTRACT METHODS
           ============================================================ */

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshWidget = function () {
            loadOpenRequisitions();
        };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            $(document).off('keydown.vas_217_esc');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.refreshWidget = function () {
        if (this.refreshWidget) {
            this.refreshWidget();
        }
    };

    VAS.VAS_217_OpenRequisitionsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

})(VAS, jQuery);
