/**
 * Invoices Widget
 * Purpose - Show invoices needing attention on home/finance dashboard
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Invoices needing your attention                           | VIS_InvoicesNeedingAttention   | Invoices needing your attention
 *  2  | INVOICE                                                   | VIS_Invoice                    | INVOICE
 *  3  | CUSTOMER                                                  | VIS_Customer                   | CUSTOMER
 *  4  | DUE                                                       | VIS_Due                        | DUE
 *  5  | STATUS                                                    | VIS_Status                     | STATUS
 *  6  | AMOUNT                                                    | VIS_Amount                     | AMOUNT
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
        var selectedRows = {};

        var INVOICES = [];
        var COL_TMPL = '1fr 1.4fr minmax(60px,0.8fr) minmax(80px,1.1fr) minmax(70px,0.9fr)';

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

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
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetDuplicates',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && data.length > 0) {
                        /* Flatten pairs into rows */
                        INVOICES = [];
                        $.each(data, function (i, dup) {
                            INVOICES.push({
                                id: dup.invoiceA,
                                customer: dup.customer,
                                due: dup.dateA || '—',
                                status: dup.docStatusA,
                                amount: parseFloat(dup.amount)
                            });
                            INVOICES.push({
                                id: dup.invoiceB,
                                customer: dup.customer,
                                due: dup.dateB || '—',
                                status: dup.docStatusB,
                                amount: parseFloat(dup.amount)
                            });
                        });
                        /* Banner — summary of all pairs */
                        var total = data.length;
                        var first = data[0];
                        var amt = parseFloat(first.amount).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                        var title = total === 1
                            ? lbl("VIS_DuplicateSuspected", 'Duplicate suspected:') + ' ' + first.invoiceA + ' ' + lbl("VIS_MatchesAmountCustomer", 'matches') + ' ' + first.invoiceB + ' amount + customer'
                            : total + ' ' + lbl("VIS_DuplicatePairsSuspected", 'duplicate pairs suspected') + ' — ' + total + ' ' + lbl("VIS_CustomersAffected", 'customers affected');
                        var sub = total === 1
                            ? lbl("VIS_SameCustomerSameAmount", 'Same customer, same') + ' ' + amt + ' amount, issued ' + first.daysApart + ' days apart'
                            : lbl("VIS_SameCustomerWithin7Days", 'Same customer and amount ordered within 7 days — review the list below');
                        $alertBanner.find('.vis-inv-dup-title').text(title);
                        $alertBanner.find('.vis-inv-dup-sub').text(sub);
                        $alertBanner.css('display', 'flex');
                    } else {
                        $alertBanner.hide();
                    }
                    renderRows();
                },
                error: function () {
                    $alertBanner.hide();
                    renderRows();
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
                '</div>'
            );

            /* Table wrapper */
            var $tableWrap = $('<div class="vas-inv-table-wrap">');

            /* Table header row */
            var $tableHead = $(
                '<div class="vas-inv-table-head" style="grid-template-columns:' + COL_TMPL + ';">' +
                '<span class="vas-inv-th">' + lbl("VIS_Invoice", 'INVOICE') + '</span>' +
                '<span class="vas-inv-th-customer">' + lbl("VIS_Customer", 'CUSTOMER') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VIS_Due", 'DUE') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VIS_Status", 'STATUS') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VIS_Amount", 'AMOUNT') + '</span>' +
                '</div>'
            );

            /* Table body */
            $tableBody = $('<div id="vis-inv-tbody-' + $self.AD_UserHomeWidgetID + '">');
            renderRows();

            $tableWrap.append($tableHead).append($tableBody);
            $card.append($header).append($alertBanner).append($tableWrap);
            $root.append($card);
        }

        /* ── Render rows ── */
        function renderRows() {
            $tableBody.empty();
            $.each(INVOICES, function (i, inv) {
                var cfg = STATUS_CONFIG[inv.status] || { label: inv.status, bg: '#EDEDED', color: '#505050' };
                var isLast = (i === INVOICES.length - 1);
                var isChk = !!selectedRows[inv.id];
                var rowBg = isChk ? '#F0F8FF' : 'transparent';

                var $row = $(
                    '<div data-invid="' + inv.id + '" ' +
                    'class="vas-inv-row" style="grid-template-columns:' + COL_TMPL + ';' +
                    'background:' + rowBg + ';' +
                    (isLast ? '' : 'border-bottom:1px solid #EDF2F6;') + '">' +
                    '<span class="vas-inv-cell-id">' + inv.id + '</span>' +
                    '<span class="vas-inv-cell-customer">' + inv.customer + '</span>' +
                    '<span class="vas-inv-cell-due">' + inv.due + '</span>' +
                    '<span>' +
                    '<span class="vas-inv-status-chip" style="' +
                    'background:' + cfg.bg + ';color:' + cfg.color + ';">' +
                    cfg.label +
                    '</span>' +
                    '</span>' +
                    '<span class="vas-inv-cell-amount">' + inv.amount.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + '</span>' +
                    '</div>'
                );

                $tableBody.append($row);
            });
        }

        /* ── Events ── */
        function bindEvents() {

            /* Row click — toggle selection */
            $root.on('click', '[data-invid]', function (e) {
                if ($(e.target).is('input[type=checkbox]')) return;
                var id = $(this).data('invid');
                toggleRow(id);
            });

            /* Row checkbox */
            $root.on('change', 'input[data-rowinvid]', function () {
                var id = $(this).data('rowinvid');
                toggleRow(id);
            });

            /* Select-all checkbox */
            $root.on('change', '#vis-inv-chk-all-' + $self.AD_UserHomeWidgetID, function () {
                if ($(this).is(':checked')) {
                    $.each(INVOICES, function (i, inv) { selectedRows[inv.id] = true; });
                } else {
                    selectedRows = {};
                }
                renderRows();
            });

            /* Dismiss alert */
            $root.on('click', '#vis-inv-review-' + $self.AD_UserHomeWidgetID, function () {
                $alertBanner.slideUp(200);
            });

            /* New invoice */
            $root.on('click', '#vis-inv-newbtn-' + $self.AD_UserHomeWidgetID, function () {
                onNewInvoice();
            });
        }

        function toggleRow(id) {
            if (selectedRows[id]) {
                delete selectedRows[id];
            } else {
                selectedRows[id] = true;
            }
            var $row = $tableBody.find('[data-invid="' + id + '"]');
            var isChk = !!selectedRows[id];
            $row.css('background', isChk ? '#F0F8FF' : 'transparent');
            $row.find('input[type=checkbox]').prop('checked', isChk);

            var allChk = Object.keys(selectedRows).length === INVOICES.length;
            $root.find('#vis-inv-chk-all-' + $self.AD_UserHomeWidgetID).prop('checked', allChk);
        }

        function onNewInvoice() {
            // TODO: wire to VIS.viewManager.startWindow(...) when backend is ready
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            selectedRows = {};
            INVOICES = [];
            loadData();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.InvoicesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
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
