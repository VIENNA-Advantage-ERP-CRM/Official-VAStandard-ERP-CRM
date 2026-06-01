/**
 * Invoices Widget
 * Purpose - Show invoices needing attention on home/finance dashboard.
 *
 * Data flow:
 *   - Table rows come from Invoices/GetAttentionInvoices (server-side paged, 5 per page): only
 *     Overdue / Due soon (unpaid pay schedules) and Sent (completed/closed, e-mail not yet sent)
 *     rows, deduped to one row per invoice and converted to the client's base currency.
 *   - The duplicate alert banner + Review dialog come from Invoices/GetDuplicates (independent).
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Invoices needing your attention                           | VIS_InvoicesNeedingAttention   | Invoices needing your attention
 *  2  | INVOICE                                                   | VIS_Invoice                    | INVOICE
 *  3  | CUSTOMER                                                  | VIS_Customer                   | CUSTOMER
 *  4  | DUE                                                       | VAS_Date                       | Date
 *  5  | STATUS                                                    | VIS_Status                     | STATUS
 *  6  | AMOUNT                                                    | VIS_Amount                     | AMOUNT
 *  6d | Overdue                                                   | VIS_Overdue                    | Overdue
 *  6e | Due soon                                                  | VIS_DueSoon                    | Due soon
 *  6f | Sent (category)                                           | VIS_Sent                       | Sent
 *  6f1| Email sent (Sent row, e-mail done)                        | VIS_EmailSent                  | Email sent
 *  6f2| Not sent (Sent row, e-mail pending)                       | VIS_EmailNotSent               | Not sent
 *  6g | Today                                                     | VIS_Today                      | Today
 *  6h | No data                                                   | VIS_NoData                     | No data
 *  6i | Previous / Next (pager)                                   | VIS_Previous / VIS_Next        | Previous / Next
 *  6j | Showing {from}–{to} of {total} (pager helper)             | VIS_Showing / VIS_Of           | Showing / of
 *  6a | + New invoice                                             | VIS_NewInvoice                 | New invoice
 *  6b | Review                                                    | VIS_Review                     | Review
 *  6c | {n} duplicate suspected based on Amount and Customer      | VIS_DuplicateSuspectedBased    | duplicate suspected based on Amount and Customer
 *  -  | Review-dialog keys (English fallbacks via lbl): VIS_DupBannerSub, VIS_DuplicateInvoiceReview,
 *     | VIS_DuplicateSets, VIS_SwitchTabsReview, VIS_MatchingField, VIS_DifferingField, VIS_PotentialDup,
 *     | VIS_DoubleBill, VIS_TripleBill, VIS_Newer, VIS_Newest, VIS_Original, VIS_Issued, VIS_Organization,
 *     | VIS_CustomerName, VIS_InvoiceDate, VIS_SalesOrderNo, VIS_DeliveryOrder, VIS_PaymentTerm,
 *     | VIS_DeliveryLocation, VIS_LineItems, VIS_NoLineItems, VIS_Product, VIS_AttributeSet, VIS_Charge,
 *     | VIS_Qty, VIS_UnitPrice, VIS_Amount, VIS_GrandTotal,
 *     | VIS_ReverseNewerInvoice, VIS_KeepBoth, VIS_Close, VIS_Confirm, VIS_Error, VIS_DupFooterNote
 *  7  | Duplicate suspected: ...                                  | VIS_DuplicateSuspected         | Duplicate suspected:
 *  8  | matches ... amount + customer                             | VIS_MatchesAmountCustomer      | matches {0} amount + customer
 *  9  | duplicate pairs suspected — ... customers affected        | VIS_DuplicatePairsSuspected    | duplicate pairs suspected
 * 10  | customers affected                                        | VIS_CustomersAffected          | customers affected
 * 11  | Same customer, same ... amount, issued ... days apart     | VIS_SameCustomerSameAmount     | Same customer, same {0} amount, issued {1} days apart
 * 12  | Same customer and amount ordered within 7 days            | VIS_SameCustomerWithin7Days    | Same customer and amount ordered within 7 days — review the list below
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN)        | VIS_StatusDraft etc.           | Draft / In Progress / Completed / ...
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.InvoicesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-inv-root">');

        var $tableBody;
        var $alertBanner;
        /* Busy/loading overlay shown while the attention rows are being fetched. */
        var $busy;
        /* Footer pager (server-side paging, 5 rows per page) — design.md "Widget Footer Pager":
           left result-range helper + right compact pager control. */
        var $footer;
        var $pageInfo;
        var $pageText;
        var $prevBtn;
        var $nextBtn;
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        /* Base-currency symbol from the backend; rendered before every amount. */
        var currencySymbol = '';
        /* Duplicate sets (grouped by customer) and the open review-dialog overlay. */
        var reviewSets = [];
        var $review = null;

        var INVOICES = [];
        /* Columns match the header order: invoice, customer, due, status, amount. */
        var COL_TMPL = '1fr 1.4fr minmax(60px,0.8fr) minmax(80px,1.1fr) minmax(70px,0.9fr)';

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* Escape DB-sourced text before inserting it as HTML (XSS-safe). */
        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Amount markup with the base-currency symbol placed before the number
           (sign, if any, precedes the symbol). */
        function formatAmount(value) {
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            var sym = currencySymbol ? '<span class="vas-inv-cur">' + escapeHtml(currencySymbol) + '</span>' : '';
            return sign + sym + num;
        }

        /* Attention-status chips (keyed by the backend attention_status text), per design.md §8.
           These are distinct from the document-status codes in STATUS_CONFIG below. */
        var ATTENTION_STATUS = {
            'Overdue': { key: 'VIS_Overdue', fallback: 'Overdue', bg: '#FAD7D7', color: '#8F2D2D' },
            'Due soon': { key: 'VIS_DueSoon', fallback: 'Due soon', bg: '#FCEFC7', color: '#9A6500' },
            'Sent': { key: 'VIS_Sent', fallback: 'Sent', bg: '#D9ECFF', color: '#0E5DA8' }
        };

        /* Email-state chips for the 'Sent' category: completed invoices now appear whether or not the
           invoice e-mail has gone out. 'Y' = already e-mailed (done, green); otherwise the e-mail is
           still pending (amber, needs action). */
        var SENT_EMAIL_STATUS = {
            'Y': { key: 'VIS_EmailSent', fallback: 'Email sent', bg: '#CCEFDD', color: '#0C5D38' },
            'N': { key: 'VIS_EmailNotSent', fallback: 'Not sent', bg: '#FCEFC7', color: '#9A6500' }
        };

        var STATUS_CONFIG = {
            DR: { label: lbl("VIS_StatusDraft", 'Draft'), bg: '#EDEDED', color: '#505050' },
            IP: { label: lbl("VIS_StatusInProgress", 'In Progress'), bg: '#FFF3CD', color: '#9A6500' },
            CO: { label: lbl("VIS_StatusCompleted", 'Completed'), bg: '#CCEFDD', color: '#0C5D38' },
            CL: { label: lbl("VIS_StatusClosed", 'Closed'), bg: '#DFF1FF', color: '#0E5DA8' },
            AP: { label: lbl("VIS_StatusApproved", 'Approved'), bg: '#CCEFDD', color: '#0C5D38' },
            NA: { label: lbl("VIS_StatusNotApproved", 'Not Approved'), bg: '#FFE8E8', color: '#C0392B' },
            WP: { label: lbl("VIS_StatusWaitingPayment", 'Waiting Payment'), bg: '#FFF3CD', color: '#9A6500' },
            WC: { label: lbl("VIS_StatusWaitingConfirm", 'Waiting Confirm'), bg: '#FFF3CD', color: '#9A6500' },
            RE: { label: lbl("VIS_StatusReversed", 'Reversed'), bg: '#FFE8E8', color: '#C0392B' },
            VO: { label: lbl("VIS_StatusVoided", 'Voided'), bg: '#FFE8E8', color: '#C0392B' },
            IN: { label: lbl("VIS_StatusInvalid", 'Invalid'), bg: '#FFE8E8', color: '#C0392B' }
        };

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            bindEvents();
            /* Two independent data sources: the attention list fills the table (with paging); the
               duplicate scan only drives the alert banner + Review dialog. */
            loadAttention();
            loadDuplicates();
        };

        /* Defensively unwrap the controller payload — actions return Json(JsonConvert.Serialize(...)),
           so the body can arrive double-encoded depending on the MVC pipeline. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* ── Load the attention rows (Overdue / Due soon / Sent), one page at a time ── */
        function loadAttention() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetAttentionInvoices',
                type: 'GET',
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        INVOICES = [];
                        totalPages = 0;
                        totalRecords = 0;
                        renderRows();
                        updatePager();
                        showBusy(false);
                        return;
                    }
                    /* Symbol from whichever endpoint answers first; both report the same base currency. */
                    currencySymbol = data.symbol || currencySymbol || '';
                    pageNo = Number(data.pageNo || pageNo);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);

                    var rows = data.rows || [];
                    INVOICES = $.map(rows, function (r) {
                        return {
                            id: r.documentNo,
                            cInvoiceId: r.cInvoiceId,
                            cBPartnerId: r.cBPartnerId,
                            customer: r.customerName,
                            due: r.dueDate,
                            daysDelta: r.daysDelta,
                            status: r.attentionStatus,
                            /* Y/N invoice e-mail state; drives the sent / not-sent chip on 'Sent' rows. */
                            emailSent: r.emailSent,
                            amount: parseFloat(r.amountBase) || 0
                        };
                    });
                    renderRows();
                    updatePager();
                    showBusy(false);
                },
                error: function () {
                    INVOICES = [];
                    totalPages = 0;
                    totalRecords = 0;
                    renderRows();
                    updatePager();
                    showBusy(false);
                }
            });
        }

        /* ── Load duplicate-suspected pairs for the alert banner + Review dialog ── */
        function loadDuplicates() {
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetDuplicates',
                type: 'GET',
                success: function (res) {
                    var data = parseResponse(res);
                    /* Backend returns { symbol, pairs[] }; tolerate a bare array too. */
                    if (!currencySymbol) { currencySymbol = (data && data.symbol) || ''; }
                    var pairs = (data && data.pairs) ? data.pairs : (Array.isArray(data) ? data : []);
                    if (pairs.length > 0) {
                        /* Group the pairs into duplicate sets (one per customer) for the
                           Review dialog, and headline the banner with the set count. */
                        reviewSets = buildSets(pairs);
                        var title = reviewSets.length + ' ' + lbl("VIS_DuplicateSuspectedBased", 'duplicate suspected based on Amount and Customer');
                        var sub = lbl("VIS_DupBannerSub", 'Same customer and amount — open Review to compare.');
                        $alertBanner.find('.vis-inv-dup-title').text(title);
                        $alertBanner.find('.vis-inv-dup-sub').text(sub);
                        $alertBanner.css('display', 'flex');
                    } else {
                        reviewSets = [];
                        $alertBanner.hide();
                    }
                },
                error: function () {
                    reviewSets = [];
                    $alertBanner.hide();
                }
            });
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div class="vas-inv-card">'
            );

            /* Header */
            var $header = $(
                '<div class="vas-inv-header">' +
                '<div class="vas-inv-header-left">' +
                '<div class="vas-inv-icon">' +
                '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#0083DA" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
                '<polyline points="14 2 14 8 20 8"/>' +
                '<line x1="16" y1="13" x2="8" y2="13"/>' +
                '<line x1="16" y1="17" x2="8" y2="17"/>' +
                '<polyline points="10 9 9 9 8 9"/>' +
                '</svg>' +
                '</div>' +
                '<span class="vas-inv-title">' + lbl("VIS_InvoicesNeedingAttention", 'Invoices needing your attention') + '</span>' +
                '</div>' +
                /* Right: open a new invoice on the same screen */
                '<button type="button" id="vis-inv-newbtn-' + $self.AD_UserHomeWidgetID + '" class="vas-inv-newbtn">' +
                '<span class="vas-inv-newbtn-plus">+</span> ' + lbl("VIS_NewInvoice", 'New invoice') +
                '</button>' +
                '</div>'
            );

            /* Duplicate alert banner */
            $alertBanner = $(
                '<div id="vis-inv-alert-' + $self.AD_UserHomeWidgetID + '" ' +
                'class="vas-inv-alert-banner" style="display:none;">' +
                '<svg class="vas-inv-alert-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#D78B10" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/>' +
                '<line x1="4" y1="22" x2="4" y2="15"/>' +
                '</svg>' +
                '<div class="vas-inv-alert-text">' +
                '<div class="vis-inv-dup-title vas-inv-dup-title"></div>' +
                '<div class="vis-inv-dup-sub vas-inv-dup-sub"></div>' +
                '</div>' +
                '<button type="button" id="vis-inv-review-' + $self.AD_UserHomeWidgetID + '" class="vas-inv-review-btn">' +
                lbl("VIS_Review", 'Review') +
                '</button>' +
                '</div>'
            );

            /* Table wrapper */
            var $tableWrap = $('<div class="vas-inv-table-wrap">');

            /* Table header row — frozen above the scrolling body (CSS sticky). */
            var $tableHead = $(
                '<div class="vas-inv-table-head" style="grid-template-columns:' + COL_TMPL + ';">' +
                '<span class="vas-inv-th">' + lbl("VIS_Invoice", 'INVOICE') + '</span>' +
                '<span class="vas-inv-th-customer">' + lbl("VIS_Customer", 'CUSTOMER') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VAS_Date", 'Date') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VIS_Status", 'STATUS') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VIS_Amount", 'AMOUNT') + '</span>' +
                '</div>'
            );

            /* Table body */
            $tableBody = $('<div id="vis-inv-tbody-' + $self.AD_UserHomeWidgetID + '">');
            renderRows();

            $tableWrap.append($tableHead).append($tableBody);

            /* Footer pager (design.md "Widget Footer Pager"): two zones — left result-range helper
               ("Showing X–Y of Z") and a right-aligned compact pager (prev · "X of N" · next) with
               rounded-square buttons. Hidden while there is only a single page. */
            var chevL = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            $footer = $(
                '<div class="vas-inv-footer" style="display:none;">' +
                '<span class="vas-inv-footer-info"></span>' +
                '<div class="vas-inv-pager">' +
                '<button type="button" class="vas-inv-page-btn vas-inv-prev" aria-label="' + escapeHtml(lbl("VIS_Previous", 'Previous')) + '">' + chevL + '</button>' +
                '<span class="vas-inv-page-text"></span>' +
                '<button type="button" class="vas-inv-page-btn vas-inv-next" aria-label="' + escapeHtml(lbl("VIS_Next", 'Next')) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );
            $pageInfo = $footer.find('.vas-inv-footer-info');
            $pageText = $footer.find('.vas-inv-page-text');
            $prevBtn = $footer.find('.vas-inv-prev');
            $nextBtn = $footer.find('.vas-inv-next');

            $card.append($header).append($alertBanner).append($tableWrap).append($footer);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes
               (vis-busyindicatorinnerwrap / vis_widgetloader). Hidden until a fetch is in flight. */
            $busy = $('<div class="vas-inv-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        /* Toggle the busy/loading overlay. */
        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        /* Human-readable due value: "Today" for the current day, "Mon DD" otherwise, em dash when no
           due date (e.g. the Sent rows, which have no pay schedule). */
        function formatDue(value) {
            if (!value) { return '—'; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return escapeHtml(value); }
            var now = new Date();
            if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
                return escapeHtml(lbl("VIS_Today", 'Today'));
            }
            return escapeHtml(d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short' }));
        }

        /* Chip text: Overdue rows append how many days past due (e.g. "Overdue 3d"); daysDelta is
           DueDate - today, so an overdue invoice is 0 (due today) or negative. The label text is
           computed here, never from the status filtering rule. */
        function statusLabel(inv) {
            var cfg = ATTENTION_STATUS[inv.status];
            var base = cfg ? lbl(cfg.key, cfg.fallback) : inv.status;
            if (inv.status === 'Overdue' && inv.daysDelta != null && inv.daysDelta !== '' && Number(inv.daysDelta) < 0) {
                return base + ' ' + Math.abs(Number(inv.daysDelta)) + 'd';
            }
            return base;
        }

        /* Resolve the status chip (label + colours) for a row. 'Sent' rows split on the e-mail state
           (sent vs not sent); every other row keeps its attention-status colours, with the Overdue day
           suffix supplied by statusLabel. */
        function statusChip(inv) {
            if (inv.status === 'Sent') {
                var es = SENT_EMAIL_STATUS[inv.emailSent === 'Y' ? 'Y' : 'N'];
                return { label: lbl(es.key, es.fallback), bg: es.bg, color: es.color };
            }
            var cfg = ATTENTION_STATUS[inv.status] || { bg: '#EDEDED', color: '#505050' };
            return { label: statusLabel(inv), bg: cfg.bg, color: cfg.color };
        }

        /* ── Render rows ── */
        function renderRows() {
            $tableBody.empty();

            if (!INVOICES || INVOICES.length === 0) {
                $tableBody.append('<div class="vas-inv-nodata">' + escapeHtml(lbl("VIS_NoData", 'No data')) + '</div>');
                return;
            }

            $.each(INVOICES, function (i, inv) {
                var chip = statusChip(inv);
                var isLast = (i === INVOICES.length - 1);
                var safeId = escapeHtml(inv.id);
                /* Escaped once; reused as both the visible text and the title tooltip. */
                var safeCustomer = escapeHtml(inv.customer);

                var $row = $(
                    '<div data-invid="' + safeId + '" ' +
                    'class="vas-inv-row' + (isLast ? '' : ' vas-inv-row-divided') + '" ' +
                    'style="grid-template-columns:' + COL_TMPL + ';">' +
                    '<span class="vas-inv-cell-id">' + safeId + '</span>' +
                    '<span class="vas-inv-cell-customer" title="' + safeCustomer + '">' + safeCustomer + '</span>' +
                    '<span class="vas-inv-cell-due">' + formatDue(inv.due) + '</span>' +
                    '<span>' +
                    '<span class="vas-inv-status-chip" style="' +
                    'background:' + chip.bg + ';color:' + chip.color + ';">' +
                    escapeHtml(chip.label) +
                    '</span>' +
                    '</span>' +
                    '<span class="vas-inv-cell-amount">' + formatAmount(inv.amount) + '</span>' +
                    '</div>'
                );

                $tableBody.append($row);
            });
        }

        /* ── Pager state ── */
        function updatePager() {
            /* Right pager: "X of N" between the arrows. */
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VIS_Of", 'of') + ' ' + totalPages) : '');
            }
            /* Left helper: "Showing X–Y of Z" result range. */
            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VIS_Showing", 'Showing') + ' ' + from + '–' + to + ' ' + lbl("VIS_Of", 'of') + ' ' + totalRecords);
                } else {
                    $pageInfo.text('');
                }
            }
            if ($prevBtn) {
                $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }
            if ($nextBtn) {
                $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }
            if ($footer) {
                $footer.css('display', totalPages > 1 ? 'flex' : 'none');
            }
        }

        /* ── Events ── */
        function bindEvents() {

            /* Review — open the duplicate-invoice review dialog */
            $root.on('click', '#vis-inv-review-' + $self.AD_UserHomeWidgetID, function () {
                openReview();
            });

            /* New invoice */
            $root.on('click', '#vis-inv-newbtn-' + $self.AD_UserHomeWidgetID, function () {
                onNewInvoice();
            });

            /* Pager — previous / next page of attention rows. */
            $root.on('click', '.vas-inv-prev', function () {
                if (pageNo <= 1) { return; }
                pageNo--;
                loadAttention();
            });

            $root.on('click', '.vas-inv-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages) { return; }
                pageNo++;
                loadAttention();
            });
        }

        /* The widget sits on the landing page of the same window, so open a new record in the
           first tab in-place (Scenario 1) instead of navigating away. The widget host registers
           itself through addChangeListener and reacts to this fired value to switch the tab into
           new-record mode; existing filters and widget state are left untouched. */
        function onNewInvoice() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        /* ── Duplicate review dialog ────────────────────────────────────────────────
           UI per the approved duplicate_invoice_review.html design. Tabs come from the duplicate
           sets (GetDuplicates); each tab's full header + line-level detail (organization, customer,
           sales/delivery order, payment term, delivery location, grand total, line items, and the
           green/red match dots) is fetched lazily from GetDuplicateReview when first shown and cached
           on the set. The overlay is appended to <body> so it is never clipped by the card overflow. */

        /* Group duplicate pairs into one set per duplicate key (business partner + currency + grand
           total), matching the backend duplicate definition. Each set carries the invoice IDs needed
           to fetch its line-level detail (GetDuplicateReview) plus a lazy detail cache. */
        function buildSets(pairs) {
            var byKey = {};
            var order = [];

            function addInvoice(set, id, no, date, status) {
                if (!id || set.idMap[id]) return;
                set.idMap[id] = true;
                set.invoices.push({ id: id, no: no, date: date, status: status });
                set.invoiceIds.push(id);
            }

            $.each(pairs, function (_, p) {
                var key = (p.cBPartnerId || 0) + '|' + (p.cCurrencyId || 0) + '|' + (parseFloat(p.amount) || 0);
                if (!byKey[key]) {
                    byKey[key] = {
                        customer: p.customer || '',
                        amount: parseFloat(p.amount) || 0,
                        cBPartnerId: p.cBPartnerId,
                        cCurrencyId: p.cCurrencyId,
                        /* Invoice-currency symbol for this set (the set shares one currency); used for
                           the legend-context amount instead of the base-currency symbol. */
                        curSymbol: p.curSymbol || '',
                        idMap: {}, invoices: [], invoiceIds: [],
                        detail: null, loaded: false
                    };
                    order.push(key);
                }
                var set = byKey[key];
                addInvoice(set, p.invoiceAId, p.invoiceA, p.dateA, p.docStatusA);
                addInvoice(set, p.invoiceBId, p.invoiceB, p.dateB, p.docStatusB);
            });

            return order.map(function (k) { return byKey[k]; });
        }

        /* Group the flat header+line detail rows into one object per invoice, preserving the
           backend order (newest invoice first). */
        function groupDetail(rows) {
            var byInv = {};
            var order = [];
            $.each(rows, function (_, r) {
                var id = r.cInvoiceId;
                if (!byInv[id]) {
                    byInv[id] = {
                        id: id, no: r.invoiceDocumentNo, panelRole: r.panelRole,
                        docStatus: r.docStatus, invoiceStatus: r.invoiceStatus, date: r.invoiceDate,
                        org: r.organizationName, customer: r.customerName,
                        salesOrderNo: r.salesOrderNo, deliveryOrderNo: r.deliveryOrderNo,
                        paymentTerm: r.paymentTermName, deliveryLocation: r.deliveryLocation,
                        grandTotal: r.grandTotal, currencyIso: r.currencyIso,
                        isSameCustomer: r.isSameCustomer, isSameGrandTotal: r.isSameGrandTotal,
                        isSameCurrency: r.isSameCurrency, isSamePaymentTerm: r.isSamePaymentTerm,
                        isSameInvoiceDate: r.isSameInvoiceDate,
                        lines: []
                    };
                    order.push(id);
                }
                if (r.cInvoiceLineId) {
                    byInv[id].lines.push({
                        lineNo: r.lineNo, product: r.productName, charge: r.chargeName,
                        attr: r.attributeSetInstance, qty: r.qtyEntered, unit: r.unitPrice,
                        net: r.lineNetAmount, match: r.lineMatchStatus
                    });
                }
            });
            return order.map(function (id) { return byInv[id]; });
        }

        /* Fetch (once) the line-level review detail for a set's invoice IDs, caching on the set. */
        function fetchDetail(set, cb) {
            if (set.loaded) { cb(true); return; }
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetDuplicateReview',
                type: 'GET',
                data: { invoiceIds: set.invoiceIds.join(',') },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { cb(false); return; }
                    if (data.symbol && !currencySymbol) { currencySymbol = data.symbol; }
                    set.detail = groupDetail(data.rows || []);
                    set.loaded = true;
                    cb(true);
                },
                error: function () { cb(false); }
            });
        }

        function billWord(n) {
            if (n === 2) return lbl("VIS_DoubleBill", 'double-bill');
            if (n === 3) return lbl("VIS_TripleBill", 'triple-bill');
            return n + '×-bill';
        }

        /* Plain-text amount with a currency symbol before the number. Pass the set's invoice-currency
           symbol for duplicate amounts; falls back to the base symbol when none is given. */
        function money(value, symbol) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = (Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            var sym = (symbol != null && symbol !== '') ? symbol : currencySymbol;
            return escapeHtml(sym) + num;
        }

        /* Status chip — reuses the row STATUS_CONFIG colours. */
        function statusPill(code) {
            var cfg = STATUS_CONFIG[code] || { label: code || '—', bg: '#EDEDED', color: '#505050' };
            return '<span class="vas-dup-pill" style="background:' + cfg.bg + ';color:' + cfg.color + ';">' + escapeHtml(cfg.label) + '</span>';
        }

        /* Field row with an optional match/diff dot: 'match' (green) or 'diff' (red). The value sits in
           an inner text span so it stays on one line and trims with an ellipsis, while the match/diff
           dot (::after on the value) stays visible beside it. titleText (the full, plain value) is set
           as a tooltip on the text span so the trimmed-off content is still readable on hover. */
        function fieldRow(label, valueHtml, state, titleText) {
            var cls = state === 'diff' ? ' is-diff' : (state === 'match' ? ' is-match' : '');
            var titleAttr = (titleText != null && titleText !== '') ? ' title="' + escapeHtml(titleText) + '"' : '';
            return '<div class="vas-dup-field">' +
                '<span class="vas-dup-field-label">' + label + '</span>' +
                '<span class="vas-dup-field-value' + cls + '"><span class="vas-dup-field-text"' + titleAttr + '>' + valueHtml + '</span></span>' +
                '</div>';
        }

        /* Amount with the invoice's own ISO code (a duplicate set shares one currency). Used for the
           card grand total; line-item amounts are shown as plain numbers (no currency). */
        function moneyIso(value, iso) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = (Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            return escapeHtml(num) + (iso ? ' ' + escapeHtml(iso) : '');
        }

        /* Plain number at the standard precision — no currency symbol/ISO (line-item amounts). */
        function numAmt(value) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            return escapeHtml((Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision }));
        }

        /* Quantity at its natural precision (no forced decimals). */
        function numQty(value) {
            return escapeHtml((Number(value) || 0).toLocaleString(window.navigator.language));
        }

        /* Map a backend 'Y'/'N' same-flag to a dot state. */
        function flagState(sameFlag) {
            return sameFlag === 'N' ? 'diff' : (sameFlag === 'Y' ? 'match' : undefined);
        }

        /* Line-item table for one invoice. Fixed column layout with horizontal scroll so the card
           width never fluctuates between tabs; long Product/Attribute/Charge values are trimmed with
           ellipsis and carry a title tooltip. Amounts are plain numbers (no currency). */
        function buildLineTable(inv) {
            if (!inv.lines || inv.lines.length === 0) {
                return '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VIS_NoLineItems", 'No line items.')) + '</div>';
            }
            var body = '';
            $.each(inv.lines, function (_, ln) {
                var product = ln.product || '—';
                var attr = ln.attr || '—';
                var charge = ln.charge || '—';
                var matchCls = ln.match === 'matching' ? ' is-matching' : (ln.match === 'differing' ? ' is-differing' : '');
                body += '<tr class="vas-dup-line' + matchCls + '">' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(product) + '">' + escapeHtml(product) + '</td>' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(attr) + '">' + escapeHtml(attr) + '</td>' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(charge) + '">' + escapeHtml(charge) + '</td>' +
                    '<td class="vas-dup-line-num">' + numQty(ln.qty) + '</td>' +
                    '<td class="vas-dup-line-num">' + numAmt(ln.unit) + '</td>' +
                    '<td class="vas-dup-line-num">' + numAmt(ln.net) + '</td>' +
                    '</tr>';
            });
            return '<div class="vas-dup-lines-scroll"><table class="vas-dup-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-dup-line-trunc">' + lbl("VIS_Product", 'Product') + '</th>' +
                '<th class="vas-dup-line-trunc">' + lbl("VIS_AttributeSet", 'Attribute Set') + '</th>' +
                '<th class="vas-dup-line-trunc">' + lbl("VIS_Charge", 'Charge') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VIS_Qty", 'Qty') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VIS_UnitPrice", 'Unit Price') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VIS_Amount", 'Amount') + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table></div>';
        }

        /* One invoice card built from the line-level review detail. */
        function buildDetailCard(inv, total, sameLocation) {
            var dash = '<span class="vas-dup-dash">—</span>';
            var role = inv.panelRole;
            var ageLabel = role === 'Newer' ? (total > 2 ? lbl("VIS_Newest", 'Newest') : lbl("VIS_Newer", 'Newer'))
                : (role === 'Original' ? lbl("VIS_Original", 'Original') : '');
            var dateLine = lbl("VIS_Issued", 'Issued') + ' ' + escapeHtml(inv.date || '—') + (ageLabel ? ' · ' + ageLabel : '');
            function val(v) { return v ? escapeHtml(v) : dash; }
            var totalState = inv.isSameGrandTotal === 'Y' ? ' is-match' : (inv.isSameGrandTotal === 'N' ? ' is-diff' : '');

            return '<div class="vas-dup-card">' +
                '<div class="vas-dup-card-head">' +
                '<div><div class="vas-dup-card-no">' + escapeHtml(inv.no || '—') + '</div>' +
                '<div class="vas-dup-card-date">' + dateLine + '</div></div>' +
                statusPill(inv.docStatus) +
                '</div>' +
                '<div class="vas-dup-fields">' +
                fieldRow(lbl("VIS_Organization", 'Organization'), val(inv.org), null, inv.org) +
                fieldRow(lbl("VIS_CustomerName", 'Customer Name'), val(inv.customer), flagState(inv.isSameCustomer), inv.customer) +
                fieldRow(lbl("VIS_InvoiceDate", 'Invoice Date'), val(inv.date), flagState(inv.isSameInvoiceDate), inv.date) +
                fieldRow(lbl("VIS_SalesOrderNo", 'Sales Order No.'), val(inv.salesOrderNo), null, inv.salesOrderNo) +
                fieldRow(lbl("VIS_DeliveryOrder", 'Delivery Order'), val(inv.deliveryOrderNo), null, inv.deliveryOrderNo) +
                fieldRow(lbl("VIS_PaymentTerm", 'Payment Term'), val(inv.paymentTerm), flagState(inv.isSamePaymentTerm), inv.paymentTerm) +
                fieldRow(lbl("VIS_DeliveryLocation", 'Delivery Location'), val(inv.deliveryLocation), sameLocation ? 'match' : 'diff', inv.deliveryLocation) +
                '</div>' +
                '<div class="vas-dup-lines-wrap">' +
                '<div class="vas-dup-lines-title">' + lbl("VIS_LineItems", 'Line items') + '</div>' +
                buildLineTable(inv) +
                '</div>' +
                '<div class="vas-dup-card-total"><span class="lbl">' + lbl("VIS_GrandTotal", 'Grand total') + '</span>' +
                '<span class="val' + totalState + '">' + moneyIso(inv.grandTotal, inv.currencyIso) + '</span></div>' +
                '</div>';
        }

        /* Build the card grid HTML for a loaded set. */
        function renderDetailCards(set) {
            var invs = set.detail || [];
            if (invs.length === 0) {
                return '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VIS_NoData", 'No data')) + '</div>';
            }
            /* Delivery location is "same" only when every compared invoice shares it. */
            var locs = {};
            $.each(invs, function (_, v) { locs[v.deliveryLocation || ''] = true; });
            var sameLocation = Object.keys(locs).length <= 1;
            var cards = '';
            $.each(invs, function (_, inv) { cards += buildDetailCard(inv, invs.length, sameLocation); });
            return cards;
        }

        /* Skeleton card mirroring the real card's structure/size, so the grid is already at its
           final shape and does not shift when the fetched detail replaces it. */
        function buildSkeletonCard() {
            var fields = '';
            for (var f = 0; f < 7; f++) {
                fields += '<div class="vas-dup-field"><span class="vas-dup-sk vas-dup-sk-label"></span><span class="vas-dup-sk vas-dup-sk-value"></span></div>';
            }
            return '<div class="vas-dup-card vas-dup-card-skeleton" aria-hidden="true">' +
                '<div class="vas-dup-card-head"><div><div class="vas-dup-sk vas-dup-sk-no"></div><div class="vas-dup-sk vas-dup-sk-date"></div></div><span class="vas-dup-sk vas-dup-sk-pill"></span></div>' +
                '<div class="vas-dup-fields">' + fields + '</div>' +
                '<div class="vas-dup-lines-wrap"><div class="vas-dup-lines-title">' + lbl("VIS_LineItems", 'Line items') + '</div><div class="vas-dup-sk vas-dup-sk-lines"></div></div>' +
                '<div class="vas-dup-card-total"><span class="lbl">' + lbl("VIS_GrandTotal", 'Grand total') + '</span><span class="vas-dup-sk vas-dup-sk-total"></span></div>' +
                '</div>';
        }

        /* One skeleton per invoice in the set (count is known before the fetch). */
        function buildSkeletonCards(count) {
            var n = count > 0 ? count : 2;
            var html = '';
            for (var i = 0; i < n; i++) { html += buildSkeletonCard(); }
            return html;
        }

        /* Panel shell — pre-filled with one skeleton card per invoice so the grid is already at its
           final shape; the line-level detail is fetched lazily on first show. */
        function buildPanel(i, count) {
            return '<div class="vas-dup-panel' + (i === 0 ? ' is-active' : '') + '" role="tabpanel" id="vas-dup-panel-' + i + '"' + (i === 0 ? '' : ' hidden') + '>' +
                '<div class="vas-dup-grid">' + buildSkeletonCards(count) + '</div></div>';
        }

        function buildReviewDialog(sets) {
            var boxSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/></svg>';
            var closeSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';
            var infoSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>';

            var tabsHtml = '', panelsHtml = '';
            $.each(sets, function (i, set) {
                tabsHtml += '<button class="vas-dup-tab" type="button" role="tab" aria-selected="' + (i === 0 ? 'true' : 'false') + '">' +
                    '<span class="vas-dup-tab-dot"></span> ' + escapeHtml(set.customer || '—') +
                    ' <span class="vas-dup-tab-badge">' + set.invoices.length + '</span></button>';
                panelsHtml += buildPanel(i, set.invoices.length);
            });

            var subtitle = sets.length + ' ' + lbl("VIS_DuplicateSets", 'duplicate sets') + ' · ' + lbl("VIS_SwitchTabsReview", 'switch tabs to review each');

            var overlay = $(
                '<div class="vas-dup-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-dup-dialog">' +
                '<div class="vas-dup-header">' +
                '<div class="vas-dup-header-left">' +
                '<span class="vas-dup-header-icon">' + boxSvg + '</span>' +
                '<div class="vas-dup-title-wrap">' +
                '<div class="vas-dup-title">' + lbl("VIS_DuplicateInvoiceReview", 'Duplicate Invoice Review') + '</div>' +
                '<div class="vas-dup-subtitle">' + subtitle + '</div>' +
                '</div></div>' +
                '<div class="vas-dup-header-right">' +
                '<button class="vas-dup-close" type="button" aria-label="' + escapeHtml(lbl("VIS_Close", 'Close')) + '">' + closeSvg + '</button>' +
                '</div></div>' +
                '<div class="vas-dup-tabs" role="tablist">' + tabsHtml + '</div>' +
                '<div class="vas-dup-legend">' +
                '<span class="vas-dup-legend-item"><span class="vas-dup-legend-dot match"></span> ' + lbl("VIS_MatchingField", 'Matching field') + '</span>' +
                '<span class="vas-dup-legend-item"><span class="vas-dup-legend-dot diff"></span> ' + lbl("VIS_DifferingField", 'Differing field') + '</span>' +
                '<span class="vas-dup-legend-context"></span>' +
                '</div>' +
                '<div class="vas-dup-body">' + panelsHtml + '</div>' +
                '<div class="vas-dup-footer">' +
                '<div class="vas-dup-footer-note">' + infoSvg + '<span></span></div>' +
                '<div class="vas-dup-actions">' +
                '<button class="vas-dup-btn danger" type="button" data-dup-act="void">' + lbl("VIS_ReverseNewerInvoice", 'Reverse newer invoice') + '</button>' +
                '<button class="vas-dup-btn primary" type="button" data-dup-act="keep">' + lbl("VIS_KeepBoth", 'Keep both') + '</button>' +
                '</div></div>' +
                /* Busy overlay shown while a footer action (Keep both / Reverse) is in flight; reuses
                   the core spinner classes. */
                '<div class="vas-dup-busy" style="visibility:hidden;"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div></div>'
            );

            var $tabs = overlay.find('.vas-dup-tab');
            var $panels = overlay.find('.vas-dup-panel');
            var $ctx = overlay.find('.vas-dup-legend-context');
            var $note = overlay.find('.vas-dup-footer-note span');
            var $subtitle = overlay.find('.vas-dup-subtitle');
            /* Index of the duplicate set whose tab is currently shown; the footer actions
               (Reverse newer / Keep both) always operate on this set. */
            var currentIdx = 0;

            function applyMeta(i) {
                var s = sets[i];
                if (!s) return;
                $ctx.html(escapeHtml(s.customer || '') + ' · <b>' + lbl("VIS_PotentialDup", 'potential') + ' ' + billWord(s.invoices.length) + ' ' + money(s.amount, s.curSymbol) + '</b>');
                $note.text(lbl("VIS_DupFooterNote", 'Header amounts match — review the invoices before deciding.'));
            }

            /* Lazily fetch + render a set's line-level detail the first time its tab is shown. */
            function ensurePanel(i) {
                var set = sets[i];
                var $panel = $panels.eq(i);
                if (!set || !$panel.length || $panel.attr('data-rendered') === '1') return;
                var $grid = $panel.find('.vas-dup-grid');
                if (set.loaded) {
                    $grid.html(renderDetailCards(set));
                    $panel.attr('data-rendered', '1');
                    return;
                }
                /* Skeleton cards are already showing (buildPanel) — leave them until the data lands. */
                fetchDetail(set, function (ok) {
                    /* The dialog may have been closed before the response arrived. */
                    if (!$review || $panel.closest('body').length === 0) return;
                    $grid.html(ok ? renderDetailCards(set) : '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VIS_NoData", 'No data')) + '</div>');
                    $panel.attr('data-rendered', '1');
                });
            }

            function selectTab(i) {
                currentIdx = i;
                $tabs.each(function (idx) { $(this).attr('aria-selected', idx === i ? 'true' : 'false'); });
                $panels.each(function (idx) {
                    var on = idx === i;
                    $(this).toggleClass('is-active', on);
                    if (on) { $(this).removeAttr('hidden'); } else { $(this).attr('hidden', ''); }
                });
                applyMeta(i);
                ensurePanel(i);
                var body = overlay.find('.vas-dup-body')[0];
                if (body) body.scrollTop = 0;
            }

            /* While a footer action is in flight: show the dialog spinner and disable both buttons
               (prevents double-submit). */
            function setBusy(busy) {
                var el = overlay.find('.vas-dup-busy')[0];
                if (el) { el.style.visibility = busy ? 'visible' : 'hidden'; }
                overlay.find('.vas-dup-btn').prop('disabled', !!busy);
            }

            /* Show a backend error message (raw text) using the core dialog. The ("", false, text)
               form renders the literal text rather than treating it as a message key. */
            function showError(data) {
                var txt = (data && data.error) ? data.error : lbl("VIS_Error", 'An error occurred.');
                VIS.ADialog.error("", false, txt, "");
            }

            /* Keep the header subtitle's set count in sync after a tab is removed. */
            function updateSubtitle() {
                if ($subtitle && $subtitle.length) {
                    $subtitle.text(sets.length + ' ' + lbl("VIS_DuplicateSets", 'duplicate sets') + ' · ' + lbl("VIS_SwitchTabsReview", 'switch tabs to review each'));
                }
            }

            /* After a successful action on the current set, drop just that tab + panel (and its data)
               and stay on an adjacent set; close the dialog only when no sets remain. The cached
               $tabs/$panels collections are re-queried so positional indexing stays aligned with sets[]. */
            function removeCurrentTab() {
                var i = currentIdx;
                sets.splice(i, 1);
                $tabs.eq(i).remove();
                $panels.eq(i).remove();
                $tabs = overlay.find('.vas-dup-tab');
                $panels = overlay.find('.vas-dup-panel');
                if (sets.length === 0) {
                    closeReview();
                    return;
                }
                updateSubtitle();
                selectTab(i < sets.length ? i : sets.length - 1);
            }

            /* The newer invoice of a set is the one the review detail tagged panelRole 'Newer'
               (Newest_Rank=1). Returns 0 until the set's detail has been fetched. */
            function newerInvoiceId(set) {
                var detail = set && set.detail;
                if (!detail) { return 0; }
                for (var k = 0; k < detail.length; k++) {
                    if (detail[k].panelRole === 'Newer') { return detail[k].id; }
                }
                return 0;
            }

            /* "Keep both" — flag every invoice in the set as not-a-duplicate; the set then drops
               out of GetDuplicates, so reloading clears the banner. */
            function doKeepBoth(set) {
                if (!set || !set.invoiceIds || set.invoiceIds.length === 0) { return; }
                setBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + 'Invoices/KeepBothInvoices',
                    type: 'POST',
                    data: { invoiceIds: set.invoiceIds.join(',') },
                    success: function (res) {
                        var data = parseResponse(res);
                        setBusy(false);
                        if (!data || data.error) { showError(data); return; }
                        /* Keep the dialog open — just drop this set's tab — and refresh the banner. */
                        removeCurrentTab();
                        loadDuplicates();
                    },
                    error: function () { setBusy(false); showError(null); }
                });
            }

            /* Post the Reverse-Correct request for the set's newer invoice, then refresh. */
            function reverseNewer(set) {
                var invId = newerInvoiceId(set);
                if (!invId) { showError(null); return; }
                setBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + 'Invoices/ReverseNewerInvoice',
                    type: 'POST',
                    data: { cInvoiceId: invId },
                    success: function (res) {
                        var data = parseResponse(res);
                        setBusy(false);
                        if (!data || data.error) { showError(data); return; }
                        /* Keep the dialog open — just drop this set's tab. The reversed invoice changes
                           status (RE), so refresh both the duplicate banner and the attention list. */
                        removeCurrentTab();
                        loadDuplicates();
                        loadAttention();
                    },
                    error: function () { setBusy(false); showError(null); }
                });
            }

            /* "Reverse newer invoice" — confirm first (a reversal is hard to undo), then run the
               document Reverse-Correct action. The set's line detail carries the 'Newer' marker, so
               fetch it first if the tab's detail has not loaded yet. */
            function doReverseNewer(set) {
                if (!set) { return; }
                VIS.ADialog.confirm("VIS_ReverseNewerInvoice", true, "", lbl("VIS_Confirm", 'Confirm'), function (ok) {
                    if (!ok) { return; }
                    if (set.loaded) { reverseNewer(set); return; }
                    setBusy(true);
                    fetchDetail(set, function (fetched) {
                        setBusy(false);
                        if (!fetched) { showError(null); return; }
                        reverseNewer(set);
                    });
                });
            }

            applyMeta(0);
            ensurePanel(0);

            overlay.on('click', '.vas-dup-tab', function () { selectTab($tabs.index(this)); });

            /* Footer actions operate on the currently selected duplicate set. */
            overlay.on('click', '[data-dup-act]', function () {
                var act = $(this).attr('data-dup-act');
                var set = sets[currentIdx];
                if (act === 'keep') { doKeepBoth(set); }
                else if (act === 'void') { doReverseNewer(set); }
            });

            /* X and backdrop dismiss the dialog. */
            overlay.on('click', '.vas-dup-close', function () { closeReview(); });
            overlay.on('click', function (e) { if (e.target === overlay[0]) closeReview(); });

            return overlay;
        }

        function openReview() {
            if (!reviewSets || reviewSets.length === 0) return;
            closeReview();
            $review = buildReviewDialog(reviewSets);
            $('body').append($review);
            /* Force a reflow so the open transition runs, then reveal. */
            if ($review[0]) { void $review[0].offsetHeight; }
            $review.addClass('is-open');
            $(document).on('keydown.vasInvDup', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) closeReview();
            });
        }

        function closeReview() {
            $(document).off('keydown.vasInvDup');
            if ($review) {
                $review.remove();
                $review = null;
            }
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            INVOICES = [];
            pageNo = 1;
            loadAttention();
            loadDuplicates();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            closeReview();
            $root.remove();
        };
    };

    VIS.InvoicesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay a fired value (e.g. open-in-new-mode params) to the registered widget host. */
    VIS.InvoicesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (Scenario 1). */
    VIS.InvoicesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VIS.InvoicesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.InvoicesWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.InvoicesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
