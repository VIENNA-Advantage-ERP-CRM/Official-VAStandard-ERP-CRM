/************************************************************
 * Module Name    : VAS
 * Purpose        : Aging of Unreconciled Items - a row-based aging report for the
 *                  Banking dashboard, with a per-bucket drill-down modal.
 *
 *                  How long the tenant's unreconciled PAYMENTS have been sitting.
 *                  C_Payment is the only transaction source, for the summary and the
 *                  detail alike - no bank-statement or allocation tables anywhere.
 *
 *                    [clock] Aging of Unreconciled Items   [ All accounts v ]
 *                            ₹62.4L across 194 open lines · ₹12.7L (20%) past 30 days
 *
 *                    Age        ■Receipts ■Payments        Value    Count
 *                    0-7 days   ███████████████▌            ₹21.6L      64
 *                    8-15 days  ██████████▌                 ₹15.8L      48
 *                    16-30 days ████████▌                   ₹12.3L      41
 *                    - - - - - Policy threshold · 30 days - - - - -
 *                    31-60 days █████▊                       ₹8.9L      27
 *                    60+ days   ██▌                          ₹3.8L      14
 *
 *                  AGING READS TOP TO BOTTOM, OLDEST LAST, the way an ERP aging report
 *                  prints - not as vertical bars.
 *
 *                  EACH TRACK IS ITS OWN BUCKET'S MIX. The two segments are shares of
 *                  the row's OWN total, so every track fills completely and what it
 *                  shows is the receipts-against-payments split inside that bucket.
 *                  Comparing one bucket with another is the Value column's job - the
 *                  track answers "what is this bucket made of", which is the question
 *                  a reconciler actually acts on. Hue encodes DIRECTION only; the
 *                  dashed policy line is what carries severity, because "past 30 days"
 *                  is a rule the business set, not something a colour ramp can imply.
 *
 *                  THE COUNT IS A BUTTON. Clicking it opens the detail modal for that
 *                  bucket - the payments behind the number, one server page at a time.
 *                  The client sends only a bucket KEY ("b1".."b5"); the server maps it
 *                  onto dates it computed itself, so no aging condition is ever built
 *                  in the browser.
 *
 *                  THE BANK ACCOUNT FILTER scopes the whole card: the buckets, the
 *                  subtitle totals and the modal all read the same selection, so a
 *                  drill-down can only ever hold the payments behind the number that
 *                  was clicked. "All accounts" is the default and is the ABSENCE of a
 *                  filter, not a row in the list - which is why it is the client's
 *                  option rather than something the server returns. The account list
 *                  travels with the figures, so the card is one round trip on load.
 *
 *                  TWO CURRENCIES, ON PURPOSE. The widget aggregates across accounts,
 *                  so its Value column is in the tenant's base (accounting-schema)
 *                  currency. The modal shows each payment in its OWN currency with
 *                  that currency's precision, because its job is to show the original
 *                  transaction. Both go through the shared
 *                  VIS.Util.formatCompactAmount helper
 *                  (Scripts/app/util/CurrencyFormat.js) or a locale-aware exact
 *                  format, never a hand-rolled one.
 *
 *                  Direction always comes from C_Payment.IsReceipt, never from the
 *                  sign of the stored amount.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Grid Data Rows, No Inner Scrollbars) supplies the shell,
 *                  the header typography and the row grid; the widget specification
 *                  supplies the track anatomy, the policy line and the modal.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Aging of Unreconciled Items   | VAS_235_AgingUnreconciled
 *                   2 | across                        | VAS_235_Across
 *                   3 | open lines                    | VAS_235_OpenLines
 *                   4 | past                          | VAS_235_Past
 *                   5 | days                          | VAS_235_Days
 *                   6 | Age                           | VAS_235_Age
 *                   7 | Value                         | VAS_235_Value
 *                   8 | Count                         | VAS_235_Count
 *                   9 | Receipts                      | VAS_235_Receipts
 *                  10 | Payments                      | VAS_235_Payments
 *                  11 | All accounts                  | VAS_235_AllAccounts
 *                  12 | Bank account                  | VAS_235_BankAccountFilter
 *                  13 | Policy threshold              | VAS_235_PolicyThreshold
 *                  13 | 0-7 days                      | VAS_235_Bucket1
 *                  14 | 8-15 days                     | VAS_235_Bucket2
 *                  15 | 16-30 days                    | VAS_235_Bucket3
 *                  16 | 31-60 days                    | VAS_235_Bucket4
 *                  17 | 60+ days                      | VAS_235_Bucket5
 *                  18 | Nothing unreconciled          | VAS_235_NothingOpen
 *                  19 | view detail                   | VAS_235_ViewDetail
 *                  20 | Unreconciled items            | VAS_235_DlgTitle
 *                  21 | as on                         | VAS_235_AsOn
 *                  22 | Account Date                  | VAS_235_AcctDate
 *                  23 | Document Type                 | VAS_235_DocumentType
 *                  24 | Payment No.                   | VAS_235_PaymentNo
 *                  25 | Vendor                        | VAS_235_Vendor
 *                  26 | Bank Account                  | VAS_235_BankAccount
 *                  27 | Currency                      | VAS_235_PaymentCurrency
 *                  28 | Days (column heading)         | VAS_235_DaysCol
 *                  29 | Amount                        | VAS_235_Amount
 *                  28 | lines                         | VAS_235_Lines
 *                  29 | No unreconciled payments      | VAS_235_NoDetail
 *                     |   found for this aging bucket |
 *                  31 | Close                         | VAS_235_Close
 *                  32 | Showing                       | VAS_020_Showing     (reuse)
 *                  33 | of                            | VAS_020_Of          (reuse)
 *                  34 | Previous                      | VAS_020_Prev        (reuse)
 *                  35 | Next                          | VAS_020_Next        (reuse)
 *                  36 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-03
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_235_AgingUnreconciledWidget.css. All classes
       are namespaced `vas-235-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root
       equal to the dashboard container's current pixel width so the header clamps
       resolve against the dashboard's visible content area, not the viewport. One
       document-level observer serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon
       font and a missing glyph leaves an empty box. Every one is decorative and carries
       aria-hidden; the meaning is always in the text beside it.

       Each carries explicit width / height ATTRIBUTES as well as a viewBox. CSS still
       decides the real size wherever a rule applies - a presentation attribute loses to
       any stylesheet - but an SVG with only a viewBox and no intrinsic size falls back to
       300x150px, so a stale or not-yet-rebuilt bundle would render a giant clock across
       the dialog header instead of nothing. The attribute makes that failure mode
       impossible. */
    var ICONS = {
        clock: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<circle cx="12" cy="12" r="9"></circle>' +
            '<polyline points="12 7 12 12 15.5 14"></polyline></svg>',
        close: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" ' +
            'stroke-linecap="round" aria-hidden="true" focusable="false">' +
            '<path d="M18 6 6 18M6 6l12 12"></path></svg>',
        chevron: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="m15 6-6 6 6 6"></path></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="m9 6 6 6-6 6"></path></svg>'
    };

    /* The five buckets in report order, youngest first. The KEY is what the client sends
       back to open a bucket - never a date, never a condition. */
    var BUCKETS = [
        { ordinal: 1, key: 'b1', msg: 'VAS_235_Bucket1', text: '0-7 days' },
        { ordinal: 2, key: 'b2', msg: 'VAS_235_Bucket2', text: '8-15 days' },
        { ordinal: 3, key: 'b3', msg: 'VAS_235_Bucket3', text: '16-30 days' },
        { ordinal: 4, key: 'b4', msg: 'VAS_235_Bucket4', text: '31-60 days' },
        { ordinal: 5, key: 'b5', msg: 'VAS_235_Bucket5', text: '60+ days' }
    ];

    /* The policy line is drawn ABOVE this bucket - between 16-30 and 31-60. */
    var POLICY_BREACH_ORDINAL = 4;

    /* The document number zooms to the payment itself, and WHICH window depends on the
       direction: a receipt lives in the AR Receipt window, a payment in the AP Payment
       window. Both are resolved by name up front - a row only becomes a link once its own
       side has resolved, so a dead link is impossible. */
    var PAYMENT_ZOOM_COLUMN = 'C_Payment_ID';
    var RECEIPT_WINDOW_NAME_NEW = 'VAS_ARReceipt';
    var RECEIPT_WINDOW_NAME_OLD = '';
    var PAYMENT_WINDOW_NAME_NEW = 'VAS_APPayment';
    var PAYMENT_WINDOW_NAME_OLD = 'Payment';

    var DETAIL_PAGE_SIZE = 10;

    VAS.VAS_235_AgingUnreconciledWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $subtitle;
        var $acctBtn;
        var $list;
        var $state;
        var $busy;
        var $dlg;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the modal binds document-level handlers. */
        var _ns = '';

        var _buckets = [];
        var _currency = null;
        var _totalCount = 0;
        var _totalAmt = 0;
        var _pastAmt = 0;
        var _pastPct = 0;
        var _hasPastPct = false;
        var _policyDays = 30;
        var _asOfDate = '';

        /* The bank account filter. 0 is "All accounts" - the absence of a filter, and the
           default. It scopes the buckets, the subtitle and the modal alike. */
        var _accounts = [];
        var _accountId = 0;
        var _pickerOpen = false;

        /* Modal state. */
        var _dlgBucket = '';
        var _dlgPage = 1;
        var _dlgTotalRows = 0;
        var _dlgTotalPages = 0;
        var _dlgLoading = false;
        var _dlgTrigger = null;

        /* Document-number zoom targets, resolved once. A row links only if its own side
           resolved - receipts and payments live in different windows. */
        var _receiptWindowId = 0;
        var _paymentWindowId = 0;

        var _disposed = false;
        var _rootObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas235_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
            resolveZoomWindow();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads, so the
           dashboard shows one spinner style throughout.

           It covers EVERY read, not just the first: the initial load, the Refresh button
           and a change of bank account all replace the whole card, and each one deserves
           to say so. Created visible so it is already up from the moment the widget mounts,
           before the first request has even been sent. */
        function createBusyIndicator() {
            $busy = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
            '</div>');
            $busy[0].style.visibility = 'visible';
            $root.append($busy);
        }

        function showBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'visible'; }
        }

        function hideBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'hidden'; }
        }

        /* Publishes THIS widget's own measured size on its root, as the two variables the
           card's font-size clamp reads.

           --widget-inline-size is the width, as every sibling widget publishes.
           --widget-block-size is the HEIGHT, and it is this card's own addition: unlike a
           KPI card, this one has a FIXED number of rows to show - five buckets and a
           policy divider, all of which must be visible, none of which may be paged or
           scrolled away. Width alone cannot say whether they fit, so a wide but short cell
           would take the full 16-20px base and push the oldest bucket out of the bottom of
           the cell. The clamp takes whichever of the two is smaller (see the CSS), so the
           card shrinks to its height when height is what is scarce.

           One observer covers resizing, a dashboard re-layout and browser zoom alike -
           zoom changes the element's CSS-pixel size, so it fires here just like a drag. */
        function setupRootObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _rootObserver = new ResizeObserver(function (entries) {
                    if (!$root || !$root[0]) { return; }

                    for (var i = 0; i < entries.length; i++) {
                        var box = entries[i].contentRect;

                        if (box.width > 0) {
                            $root[0].style.setProperty('--widget-inline-size', box.width + 'px');
                        }
                        if (box.height > 0) {
                            $root[0].style.setProperty('--widget-block-size', box.height + 'px');
                        }
                    }
                });
                _rootObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        this.intialLoad = function () {
            loadData();
        };

        /* The dashboard's Refresh button calls this. The chosen ACCOUNT is kept - it is a
           filter the user set, not a position in the data. */
        this.refreshWidget = function () {
            loadData();
        };

        /* The two document windows, resolved up front so a rendered row already knows
           whether its number is a link. Both are cached per page load by ZoomUtil, so this
           costs one request each for the whole session. */
        function resolveZoomWindow() {
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.getWindowId !== 'function') { return; }

            VAS.ZoomUtil.getWindowId(RECEIPT_WINDOW_NAME_NEW, RECEIPT_WINDOW_NAME_OLD)
                .then(function (id) {
                    if (_disposed) { return; }
                    _receiptWindowId = Number(id) || 0;
                });

            VAS.ZoomUtil.getWindowId(PAYMENT_WINDOW_NAME_NEW, PAYMENT_WINDOW_NAME_OLD)
                .then(function (id) {
                    if (_disposed) { return; }
                    _paymentWindowId = Number(id) || 0;
                });
        }

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-235-root" id="vas-235-root-' + widgetID + '"></div>');

            var title = label('VAS_235_AgingUnreconciled', 'Aging of Unreconciled Items');

            $card = $(
                '<div class="vas-235-card">' +
                    '<div class="vas-235-header">' +
                        '<span class="vas-235-icon">' + ICONS.clock + '</span>' +
                        '<div class="vas-235-head-text">' +
                            '<div class="vas-235-title"></div>' +
                            '<div class="vas-235-subtitle"></div>' +
                        '</div>' +
                        /* Bank account filter: the card's only control. A pill plus a
                           body-anchored popover, the same shape the sibling Banking cards
                           use for their period and sort filters. */
                        '<button type="button" class="vas-235-acct" aria-haspopup="listbox">' +
                            '<span class="vas-235-acct-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-235-body" role="table">' +
                        '<div class="vas-235-ghead vas-235-row" role="row"></div>' +
                        '<div class="vas-235-list" role="rowgroup"></div>' +
                    '</div>' +
                    '<div class="vas-235-state vas-235-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-235-title').text(title).attr('title', title);
            $card.find('.vas-235-body').attr('aria-label', title);

            $subtitle = $card.find('.vas-235-subtitle');
            $acctBtn = $card.find('.vas-235-acct');
            $list = $card.find('.vas-235-list');
            $state = $card.find('.vas-235-state');

            paintHead();
            paintAccountLabel();

            $acctBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated, so a repaint never has to rebind. */
            $list.on('click' + _ns, '.vas-235-count', function () {
                openDialog($(this).attr('data-bucket'), this);
            });

            $root.append($card);
            buildDialog();
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function loadData() {
            /* Raised on every read - load, Refresh and account change alike - and lowered
               again when the response paints, success or failure. */
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_235_AgingUnreconciledWidget/GetAging',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { bankAccountId: _accountId },
                success: function (raw) {
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* The account list comes back with every read, so a newly added
                       account appears on the next refresh without a second endpoint. */
                    _accounts = data.Accounts || [];
                    paintAccountLabel();

                    _buckets = data.Buckets || [];
                    _currency = data.Currency || null;
                    _totalCount = Number(data.TotalCount) || 0;
                    _totalAmt = Number(data.TotalAmt) || 0;
                    _pastAmt = Number(data.PastPolicyAmt) || 0;
                    _pastPct = Number(data.PastPolicyPct) || 0;
                    _hasPastPct = !!data.HasPastPolicyPct;
                    _policyDays = Number(data.PolicyThresholdDays) || 30;
                    _asOfDate = data.AsOfDate || '';

                    paintSubtitle();
                    paintRows();
                },
                error: function () {
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - leaving a spinner spinning
                       over an error the user cannot see is the worst of both. */
                    hideBusyIndicator();
                    renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* The controller returns a JSON string inside a JSON response. */
        function parseResponse(raw) {
            try {
                return (typeof raw === 'string') ? (raw ? JSON.parse(raw) : null) : raw;
            }
            catch (e) { if (window.console) { console.log(e); } return null; }
        }

        /* ------------------------------------------------------------ */
        /* Render - the widget                                          */
        /* ------------------------------------------------------------ */

        /* A load failure takes the card over. Having nothing unreconciled does NOT - that
           is handled inside the list, so the header and its totals stay put. */
        function renderState(text) {
            $card.find('.vas-235-body').addClass('vas-235-hidden');
            $state.removeClass('vas-235-hidden').text(text);
        }

        /* "₹62.4L across 194 open lines · ₹12.7L (20%) past 30 days" - the whole book,
           then the part of it that has broken the policy, which is what an aging report
           is actually for. */
        function paintSubtitle() {
            var text = money(_totalAmt) + ' ' + label('VAS_235_Across', 'across') +
                ' ' + _totalCount + ' ' + label('VAS_235_OpenLines', 'open lines');

            /* The breach clause is dropped entirely when there is no exposure - "0% of
               nothing" is noise, not information. */
            if (_totalAmt !== 0) {
                text += ' · ' + money(_pastAmt) +
                    (_hasPastPct ? ' (' + Math.round(_pastPct) + '%)' : '') +
                    ' ' + label('VAS_235_Past', 'past') + ' ' + _policyDays +
                    ' ' + label('VAS_235_Days', 'days');
            }

            $subtitle.text(text).attr('title', text);
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider
           under it. The legend sits in the track column, where the colours it names are. */
        function paintHead() {
            $card.find('.vas-235-ghead').html(
                '<span class="vas-235-h-age" role="columnheader">' +
                    escapeHtml(label('VAS_235_Age', 'Age')) + '</span>' +
                '<span class="vas-235-legend" role="columnheader">' +
                    '<span class="vas-235-lg">' +
                        '<span class="vas-235-sw vas-235-sw-r"></span>' +
                        escapeHtml(label('VAS_235_Receipts', 'Receipts')) +
                    '</span>' +
                    '<span class="vas-235-lg">' +
                        '<span class="vas-235-sw vas-235-sw-p"></span>' +
                        escapeHtml(label('VAS_235_Payments', 'Payments')) +
                    '</span>' +
                '</span>' +
                '<span class="vas-235-h-val" role="columnheader">' +
                    escapeHtml(label('VAS_235_Value', 'Value')) + '</span>' +
                '<span class="vas-235-h-cnt" role="columnheader">' +
                    escapeHtml(label('VAS_235_Count', 'Count')) + '</span>'
            );
        }

        function paintAccountLabel() {
            var text = accountNameOf(_accountId);
            $acctBtn.find('.vas-235-acct-label').text(text);
            $acctBtn.attr('title', label('VAS_235_BankAccountFilter', 'Bank account') + ': ' + text);
        }

        /* 0 is "All accounts" - the absence of a filter. An id that is no longer in the
           list (an account deactivated since the selection was made) also falls back to
           All, so the pill can never name something the figures are not filtered by. */
        function accountNameOf(id) {
            if (id > 0) {
                for (var i = 0; i < _accounts.length; i++) {
                    if (Number(_accounts[i].C_BankAccount_ID) === id) {
                        return _accounts[i].Name || '';
                    }
                }
            }
            return label('VAS_235_AllAccounts', 'All accounts');
        }

        function paintRows() {
            $state.addClass('vas-235-hidden');
            $card.find('.vas-235-body').removeClass('vas-235-hidden');

            /* Nothing outstanding at all is good news, not an error - it is said once,
               across the list, and the header keeps its (zero) totals. */
            if (_totalCount === 0) {
                $list.html('<div class="vas-235-empty">' +
                    escapeHtml(label('VAS_235_NothingOpen', 'Nothing unreconciled')) + '</div>');
                return;
            }

            var html = '';

            for (var i = 0; i < BUCKETS.length; i++) {
                var meta = BUCKETS[i];

                /* The policy line goes ABOVE the first bucket past the threshold - it is a
                   divider between the routine and the overdue, not a row of its own. */
                if (meta.ordinal === POLICY_BREACH_ORDINAL) {
                    html += '<div class="vas-235-threshold" aria-hidden="true"><span>' +
                        escapeHtml(label('VAS_235_PolicyThreshold', 'Policy threshold') +
                            ' · ' + _policyDays + ' ' + label('VAS_235_Days', 'days')) +
                    '</span></div>';
                }

                html += rowHtml(meta, bucketAt(meta.ordinal));
            }

            $list.html(html);
        }

        /* The server returns the buckets in order and always all five, but the list is
           driven off its own BUCKETS array so a short or reordered payload can never shift
           a label onto the wrong row. */
        function bucketAt(ordinal) {
            for (var i = 0; i < _buckets.length; i++) {
                if (Number(_buckets[i].Bucket) === ordinal) { return _buckets[i]; }
            }
            return null;
        }

        function sideValue(bucket, side) {
            if (!bucket) { return 0; }
            return Number(side === 'r' ? bucket.ReceiptAmt : bucket.PaymentAmt) || 0;
        }

        function rowHtml(meta, bucket) {
            var bucketLabel = label(meta.msg, meta.text);

            var rValue = sideValue(bucket, 'r');
            var pValue = sideValue(bucket, 'p');

            /* Each segment is its side's share of ITS OWN bucket, so every row's track
               fills completely and shows the receipts / payments MIX inside that bucket.
               Comparing bucket against bucket is the Value column's job, not the track's.

               The payment side is taken as the remainder rather than computed separately,
               so the two can never round to 99.9% or 100.1% and leave a sliver of groove
               showing at the end of a full track. A bucket holding nothing draws no
               segments at all - just the empty groove. */
            var own = rValue + pValue;
            var rPct = own > 0 ? (rValue / own * 100) : 0;
            var pPct = own > 0 ? (100 - rPct) : 0;

            var count = bucket ? (Number(bucket.TotalCount) || 0) : 0;
            var amount = bucket ? (Number(bucket.TotalAmt) || 0) : 0;

            /* The count is a BUTTON - it opens the detail modal. A bucket holding nothing
               has nothing to open, so it renders as plain text rather than a dead control. */
            var countCell = count > 0
                ? '<button type="button" class="vas-235-count" data-bucket="' + escapeHtml(meta.key) + '" ' +
                        'aria-label="' + escapeHtml(count + ' ' +
                            label('VAS_235_OpenLines', 'open lines') + ' · ' + bucketLabel +
                            ' — ' + label('VAS_235_ViewDetail', 'view detail')) + '">' +
                    count +
                  '</button>'
                : '<span class="vas-235-count-nil">' + count + '</span>';

            return '<div class="vas-235-brow vas-235-row" role="row" ' +
                        'title="' + escapeHtml(rowTooltip(bucketLabel, bucket)) + '">' +
                '<div class="vas-235-bucket" role="cell">' + escapeHtml(bucketLabel) + '</div>' +
                '<div class="vas-235-trackcell" role="cell">' +
                    '<div class="vas-235-track" aria-hidden="true">' +
                        (rPct > 0 ? '<i class="vas-235-r" style="width:' + rPct.toFixed(1) + '%"></i>' : '') +
                        (pPct > 0 ? '<i class="vas-235-p" style="width:' + pPct.toFixed(1) + '%"></i>' : '') +
                    '</div>' +
                '</div>' +
                '<div class="vas-235-val" role="cell">' + escapeHtml(money(amount)) + '</div>' +
                '<div class="vas-235-cnt" role="cell">' + countCell + '</div>' +
            '</div>';
        }

        /* The exact split behind a track that is only ever drawn as a proportion. */
        function rowTooltip(bucketLabel, bucket) {
            var lines = [bucketLabel];

            var rCount = bucket ? (Number(bucket.ReceiptCount) || 0) : 0;
            var pCount = bucket ? (Number(bucket.PaymentCount) || 0) : 0;

            lines.push(label('VAS_235_Receipts', 'Receipts') + ': ' +
                amountText(bucket ? bucket.ReceiptAmt : 0) + ' (' + rCount + ')');
            lines.push(label('VAS_235_Payments', 'Payments') + ': ' +
                amountText(bucket ? bucket.PaymentAmt : 0) + ' (' + pCount + ')');

            return lines.join('\n');
        }

        /* ------------------------------------------------------------ */
        /* Bank account picker - anchored under the pill, on <body>      */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-235-pp vas-235-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_235_BankAccountFilter', 'Bank account')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-235-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectAccount(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-235-pp-h">' +
                escapeHtml(label('VAS_235_BankAccountFilter', 'Bank account')) + '</div>';

            /* "All accounts" leads the list - it is the default and the way back to it. */
            html += optionHtml(0, label('VAS_235_AllAccounts', 'All accounts'));

            for (var i = 0; i < _accounts.length; i++) {
                var a = _accounts[i];
                html += optionHtml(Number(a.C_BankAccount_ID) || 0, a.Name || '');
            }

            $picker.html(html);
        }

        function optionHtml(id, text) {
            var selected = id === _accountId;

            return '<button type="button" class="vas-235-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-235-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-235-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is the
           only one that sees every scroll, whichever container moved. Scrolling is not a
           dismissal: the panel travels with the pill and closes only on a pick, an outside
           click or Escape. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            if (!$picker || !$acctBtn || !$acctBtn[0]) { return; }

            var rect = $acctBtn[0].getBoundingClientRect();
            var gap = 6;
            var edge = 8;

            var roomBelow = window.innerHeight - rect.bottom - gap - edge;
            var roomAbove = rect.top - gap - edge;

            var below = _pickerH <= roomBelow || roomBelow >= roomAbove;
            var room = below ? roomBelow : roomAbove;

            var ph = _pickerH;
            if (ph > room) {
                ph = Math.max(140, room);
                $picker.css('max-height', ph + 'px');
            } else {
                $picker.css('max-height', '');
            }

            var top = below ? rect.bottom + gap : rect.top - ph - gap;
            /* Right-aligned to the pill: it sits at the card's trailing edge, so a
               left-aligned panel would hang off the dashboard. */
            var left = Math.min(rect.right - _pickerW, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker() {
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-235-hidden');
            _pickerOpen = true;
            measurePicker();

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns, positionPicker);
            document.addEventListener('scroll', onAnchorScroll, true);

            positionPicker();
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-235-hidden'); }

            $(document).off('click' + _ns);
            $(document).off('keydown' + _ns);
            $(window).off('resize' + _ns);
            document.removeEventListener('scroll', onAnchorScroll, true);
        }

        function togglePicker() {
            if (_pickerOpen) { closePicker(); } else { openPicker(); }
        }

        function onDocumentClick(e) {
            if (!$picker) { return; }
            if ($picker[0].contains(e.target)) { return; }
            if ($acctBtn[0] && $acctBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Changing the account re-reads the whole card - unlike a display toggle, this
           changes which payments are counted, so the figures have to come from the server.
           Any modal still open is closed first: its rows belong to the old filter. */
        function selectAccount(id) {
            if (id === _accountId) { return; }

            _accountId = id;
            paintAccountLabel();

            closeDialog();
            loadData();
        }

        /* ------------------------------------------------------------ */
        /* Detail modal                                                 */
        /* ------------------------------------------------------------ */
        function buildDialog() {
            $dlg = $(
                '<div class="vas-235-dlg-wrap vas-235-hidden">' +
                    '<div class="vas-235-dlg-backdrop"></div>' +
                    '<div class="vas-235-dlg" role="dialog" aria-modal="true" ' +
                            'aria-labelledby="vas-235-dlg-title-' + widgetID + '">' +
                        '<div class="vas-235-dlg-head">' +
                            /* The same icon well the card carries, so the modal reads as
                               this widget's own rather than as a generic dialog. */
                            '<span class="vas-235-dlg-icon">' + ICONS.clock + '</span>' +
                            '<div class="vas-235-dlg-titles">' +
                                '<div class="vas-235-dlg-title" id="vas-235-dlg-title-' + widgetID + '"></div>' +
                                '<div class="vas-235-dlg-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-235-x" aria-label="' +
                                escapeHtml(label('VAS_235_Close', 'Close')) + '">' + ICONS.close + '</button>' +
                        '</div>' +
                        /* The body sits in a positioned wrapper so the busy overlay can
                           cover the list without scrolling away with it. */
                        '<div class="vas-235-dlg-bodywrap">' +
                            '<div class="vas-235-dlg-body">' +
                                '<div class="vas-235-dhead vas-235-drow"></div>' +
                                '<div class="vas-235-drows"></div>' +
                            '</div>' +
                            '<div class="vas-235-dlg-busy vis-busyindicatorouterwrap vas-235-hidden">' +
                                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                            '</div>' +
                        '</div>' +
                        /* The footer is the row count and the pager, nothing else. The
                           modal is a read-only drill-down: it opens documents through the
                           number in each row, so it needs no action of its own. */
                        '<div class="vas-235-dlg-foot">' +
                            '<span class="vas-235-dlg-note"></span>' +
                            '<div class="vas-235-dpager"></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($dlg);

            paintDialogHead();

            $dlg.find('.vas-235-x').on('click', closeDialog);

            /* The backdrop deliberately does NOT dismiss. This modal is a working list -
               the operator reads across a row, pages through it and clicks out to a
               document - and a stray click on the surround throwing all of that away is a
               worse failure than the extra click it would have saved. Escape and the close
               button remain. */

            /* Delegated: the rows are repainted on every page, so the link handler is bound
               once here rather than per row. */
            $dlg.on('click', '.vas-235-dnolink', function (e) {
                e.preventDefault();
                e.stopPropagation();
                zoomToPayment(this);
            });

            /* An <a> without an href gets no implicit key activation, so Enter and Space
               are wired by hand - the link has to work from the keyboard as well. */
            $dlg.on('keydown', '.vas-235-dnolink', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar' ||
                    e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    zoomToPayment(this);
                }
            });

        }

        /* Eight columns: the specification's seven plus Document Type, which sits beside
           the number it qualifies rather than at the end. */
        function paintDialogHead() {
            $dlg.find('.vas-235-dhead').html(
                '<span>' + escapeHtml(label('VAS_235_AcctDate', 'Account Date')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_235_DocumentType', 'Document Type')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_235_PaymentNo', 'Payment No.')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_235_Vendor', 'Vendor')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_235_BankAccount', 'Bank Account')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_235_PaymentCurrency', 'Currency')) + '</span>' +
                /* A column heading, not the subtitle's "past 30 days" - a separate key so
                   a translator can capitalise one without breaking the other. */
                '<span class="vas-235-dnum">' + escapeHtml(label('VAS_235_DaysCol', 'Days')) + '</span>' +
                '<span class="vas-235-dnum">' + escapeHtml(label('VAS_235_Amount', 'Amount')) + '</span>'
            );
        }

        function openDialog(bucketKey, trigger) {
            if (!bucketKey) { return; }

            _dlgBucket = bucketKey;
            _dlgPage = 1;
            _dlgTrigger = trigger || null;

            /* The title is known before the rows arrive, so the modal opens named rather
               than blank. */
            $dlg.find('.vas-235-dlg-title').text(
                label('VAS_235_DlgTitle', 'Unreconciled items') + ' · ' + bucketLabelOf(bucketKey));
            $dlg.find('.vas-235-dlg-sub').text('');

            $dlg.removeClass('vas-235-hidden');
            $(document).on('keydown' + _ns + '_dlg', onDialogKeyDown);

            fetchDetail(1);
            $dlg.find('.vas-235-x').focus();
        }

        function closeDialog() {
            if (!$dlg) { return; }

            $dlg.addClass('vas-235-hidden');
            $(document).off('keydown' + _ns + '_dlg');

            _dlgBucket = '';
            _dlgLoading = false;

            /* Focus returns to the count that opened it, so keyboard users are not dropped
               back at the top of the document. */
            if (_dlgTrigger) {
                try { _dlgTrigger.focus(); } catch (e) { /* ignore */ }
                _dlgTrigger = null;
            }
        }

        function onDialogKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeDialog(); }
        }

        function bucketLabelOf(bucketKey) {
            for (var i = 0; i < BUCKETS.length; i++) {
                if (BUCKETS[i].key === bucketKey) { return label(BUCKETS[i].msg, BUCKETS[i].text); }
            }
            return '';
        }

        function fetchDetail(pageNo) {
            if (_dlgLoading) { return; }
            _dlgLoading = true;

            var bucket = _dlgBucket;

            showDetailBusy();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_235_AgingUnreconciledWidget/GetBucketDetail',
                type: 'GET',
                dataType: 'json',
                async: true,
                /* The SAME account filter the widget is showing, so the list can only hold
                   the payments behind the number that was clicked. */
                data: {
                    bucket: bucket,
                    bankAccountId: _accountId,
                    pageNo: pageNo,
                    pageSize: DETAIL_PAGE_SIZE
                },
                success: function (raw) {
                    _dlgLoading = false;
                    if (_disposed) { return; }
                    /* A late response for a bucket the user has already moved away from
                       must not overwrite what is on screen. */
                    if (bucket !== _dlgBucket) { return; }

                    hideDetailBusy();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _dlgPage = Number(data.Page) || 1;
                    _dlgTotalRows = Number(data.TotalRows) || 0;
                    _dlgTotalPages = Number(data.TotalPages) || 0;

                    paintDialogSubtitle(data.AsOfDate || _asOfDate);
                    paintDetailRows(data.Rows || []);
                    paintDetailFooter();
                },
                error: function () {
                    _dlgLoading = false;
                    if (_disposed || bucket !== _dlgBucket) { return; }
                    /* The overlay comes down on failure too - a spinner left running over
                       an error the user cannot see is the worst of both. */
                    hideDetailBusy();
                    renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* "27 open lines · as on 30 Sep 2026" - no "book vs bank statement" wording: this
           modal is built from C_Payment alone and must not claim otherwise. */
        function paintDialogSubtitle(asOf) {
            var text = _dlgTotalRows + ' ' + label('VAS_235_OpenLines', 'open lines');
            if (asOf) { text += ' · ' + label('VAS_235_AsOn', 'as on') + ' ' + formatDate(asOf); }

            $dlg.find('.vas-235-dlg-sub').text(text);
        }

        /* The SAME framework loader the card uses, over the modal's list - so a page turn
           and a widget refresh look like one another rather than like two different
           products. The rows are cleared first: §Loading State forbids leaving a previous
           bucket's data on screen behind a spinner, where it would read as this one's. */
        function showDetailBusy() {
            $dlg.find('.vas-235-drows').empty();
            $dlg.find('.vas-235-dlg-note').text('');
            $dlg.find('.vas-235-dlg-busy').removeClass('vas-235-hidden');

            /* Paging is disabled while a page is in flight, so a double click cannot skip
               a page or race two responses. */
            $dlg.find('.vas-235-dpager button').prop('disabled', true);
        }

        function hideDetailBusy() {
            $dlg.find('.vas-235-dlg-busy').addClass('vas-235-hidden');
        }

        function renderDetailMessage(text) {
            $dlg.find('.vas-235-drows').html('<div class="vas-235-dempty">' +
                escapeHtml(text) + '</div>');
            $dlg.find('.vas-235-dpager').empty();
            $dlg.find('.vas-235-dlg-note').text('');
        }

        function paintDetailRows(rows) {
            if (!rows || rows.length === 0) {
                renderDetailMessage(label('VAS_235_NoDetail',
                    'No unreconciled payments found for this aging bucket.'));
                return;
            }

            var html = '';
            for (var i = 0; i < rows.length; i++) { html += detailRowHtml(rows[i]); }
            $dlg.find('.vas-235-drows').html(html);
        }

        function detailRowHtml(item) {
            var amount = Number(item.Amount) || 0;
            /* Direction is the server's, from IsReceipt - the colour follows the sign it
               already applied, and the sign itself is printed so the meaning never rests
               on colour alone. */
            var cls = item.IsReceipt ? 'vas-235-pos' : 'vas-235-neg';

            var vendor = item.Vendor ? item.Vendor : '—';
            var account = item.BankAccount ? item.BankAccount : '—';
            var docType = item.DocumentType ? item.DocumentType : '—';

            return '<div class="vas-235-drow vas-235-ditem">' +
                '<div class="vas-235-ddate">' + escapeHtml(formatDate(item.AcctDate)) + '</div>' +
                '<div class="vas-235-ddoctype" title="' + escapeHtml(docType) + '">' +
                    escapeHtml(docType) + '</div>' +
                '<div class="vas-235-dno">' + docNoHtml(item) + '</div>' +
                '<div class="vas-235-dvendor" title="' + escapeHtml(vendor) + '">' +
                    escapeHtml(vendor) + '</div>' +
                '<div class="vas-235-dacct" title="' + escapeHtml(account) + '">' +
                    escapeHtml(account) + '</div>' +
                '<div class="vas-235-dcur">' + escapeHtml(item.CurrencyCode || '') + '</div>' +
                '<div class="vas-235-dnum">' + (Number(item.Days) || 0) + '</div>' +
                '<div class="vas-235-dnum ' + cls + '">' +
                    escapeHtml(signedRowAmount(item, amount)) + '</div>' +
            '</div>';
        }

        /* The document number opens the payment itself. WHICH window depends on the
           direction: a receipt goes to the AR Receipt window, a payment to the AP Payment
           window - so the target id is chosen per row, not per widget. A row whose side has
           not resolved renders as plain text; there is never a link that goes nowhere. */
        function docNoHtml(item) {
            var no = escapeHtml(item.PaymentNo || '');
            var id = Number(item.C_Payment_ID) || 0;
            var windowId = item.IsReceipt ? _receiptWindowId : _paymentWindowId;

            if (!no || id <= 0 || windowId <= 0) {
                return '<span title="' + no + '">' + no + '</span>';
            }

            return '<a class="vas-235-dnolink" role="link" tabindex="0" title="' + no + '" ' +
                    'data-id="' + id + '" data-window="' + windowId + '" ' +
                    'data-receipt="' + (item.IsReceipt ? 'Y' : 'N') + '">' +
                no +
            '</a>';
        }

        /* Zooms to the payment behind a clicked document number. The window id was chosen
           when the row was rendered, so nothing is decided here beyond reading it back. */
        function zoomToPayment(el) {
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }

            var $el = $(el);
            var id = parseInt($el.attr('data-id'), 10) || 0;
            var windowId = parseInt($el.attr('data-window'), 10) || 0;
            if (id <= 0 || windowId <= 0) { return; }

            var isReceipt = $el.attr('data-receipt') === 'Y';

            VAS.ZoomUtil.zoomToRecord(PAYMENT_ZOOM_COLUMN, id, windowId,
                isReceipt ? RECEIPT_WINDOW_NAME_NEW : PAYMENT_WINDOW_NAME_NEW,
                isReceipt ? RECEIPT_WINDOW_NAME_OLD : PAYMENT_WINDOW_NAME_OLD);
        }

        function paintDetailFooter() {
            var $pager = $dlg.find('.vas-235-dpager');

            /* A bucket with no rows gets no footer at all - an empty pager over an empty
               list says nothing. */
            if (_dlgTotalRows <= 0) {
                $dlg.find('.vas-235-dlg-note').text('');
                $pager.empty();
                return;
            }

            var from = (_dlgPage - 1) * DETAIL_PAGE_SIZE + 1;
            var to = Math.min(_dlgPage * DETAIL_PAGE_SIZE, _dlgTotalRows);

            $dlg.find('.vas-235-dlg-note').text(
                label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _dlgTotalRows + ' ' +
                label('VAS_235_Lines', 'lines'));

            /* The pager is rendered even on a single page - "Page 1 of 1" with both arrows
               disabled. It states where the list ends rather than leaving the reader to
               infer it from a missing control, and the footer keeps the same shape from
               one bucket to the next instead of gaining a pager as soon as a bucket grows
               past ten rows. */
            var totalPages = _dlgTotalPages > 0 ? _dlgTotalPages : 1;

            var prevDis = _dlgPage <= 1 ? ' disabled' : '';
            var nextDis = _dlgPage >= totalPages ? ' disabled' : '';

            $pager.html(
                '<button type="button" class="vas-235-dpg vas-235-dprev" aria-label="' +
                    escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                '<span class="vas-235-dpage">' + _dlgPage + ' ' +
                    escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _dlgTotalPages + '</span>' +
                '<button type="button" class="vas-235-dpg vas-235-dnext" aria-label="' +
                    escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>'
            );

            /* Paging keeps the bucket and the as-of date - only the page number moves. */
            $pager.find('.vas-235-dprev').on('click', function () {
                if (!_dlgLoading && _dlgPage > 1) { fetchDetail(_dlgPage - 1); }
            });
            $pager.find('.vas-235-dnext').on('click', function () {
                if (!_dlgLoading && _dlgPage < _dlgTotalPages) { fetchDetail(_dlgPage + 1); }
            });
        }

        /* ------------------------------------------------------------ */
        /* Amount and date formatting                                   */
        /* ------------------------------------------------------------ */
        function symbol() { return (_currency && _currency.Symbol) ? _currency.Symbol : ''; }
        function iso() { return (_currency && _currency.Iso) ? _currency.Iso : ''; }
        function precision() {
            var p = _currency ? Number(_currency.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        /* WIDGET amounts - base currency, compact, from the shared util. */
        function money(value) {
            return symbol() + compact(value);
        }

        function compact(value) {
            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    return VIS.Util.formatCompactAmount(value, iso(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }
            return String(Math.abs(Number(value) || 0));
        }

        /* Full, non-compact base-currency amount for the row tooltips. */
        function amountText(value) {
            var abs = Math.abs(Number(value) || 0);
            var p = precision();
            return symbol() + abs.toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });
        }

        /* MODAL amounts - the PAYMENT's own currency and precision, never converted and
           never the widget's. Exact, not compact: the modal is where the real figure
           lives. A true minus sign, and an explicit plus on a receipt. */
        function signedRowAmount(item, value) {
            var p = Number(item.Precision);
            if (isNaN(p) || p < 0 || p > 6) { p = 2; }

            var sym = item.CurrencySymbol ? item.CurrencySymbol : (item.CurrencyCode || '');
            var abs = Math.abs(Number(value) || 0);
            var text = abs.toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });

            return (value < 0 ? '−' : '+') + sym + text;
        }

        /* Dates arrive as yyyy-MM-dd and are rendered in the browser's locale. Parsed part
           by part rather than through Date(string), which would read a bare ISO date as
           UTC and shift it a day back for anyone west of Greenwich. */
        function formatDate(iso) {
            if (!iso) { return ''; }

            var parts = String(iso).split('-');
            if (parts.length !== 3) { return String(iso); }

            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return String(iso); }

            try {
                return d.toLocaleDateString(window.navigator.language,
                    { day: '2-digit', month: 'short', year: 'numeric' });
            }
            catch (e) { return String(iso); }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* Every database-sourced string - vendor, payment number, bank account, currency -
           goes through here before it reaches the DOM. */
        function escapeHtml(s) {
            var v = (s === null || s === undefined) ? '' : String(s);
            return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        /* Every user-facing string goes through AD_Message; the fallback keeps the card
           readable when a key has not been seeded yet. */
        function label(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === 'function') {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== '[') { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        this.getRoot = function () { return $root; };

        /* Release everything that outlives the card: the body-mounted dialog, the document
           listener it registers, and the observer - a ResizeObserver left running keeps the
           whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;

            closePicker();
            $(document).off('keydown' + _ns + '_dlg');

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($dlg) { $dlg.off(); $dlg.find('*').off(); $dlg.remove(); $dlg = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($acctBtn) { $acctBtn.off(_ns); }
            if ($list) { $list.off(_ns); }

            _buckets = [];
            _accounts = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_235_AgingUnreconciledWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method,
       which shadows anything on the prototype. A prototype version calling
       this.refreshWidget() would be unreachable at best and infinite recursion the day
       the instance one is removed. */

    VAS.VAS_235_AgingUnreconciledWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_235_AgingUnreconciledWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
