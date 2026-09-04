/************************************************************
 * Module Name    : VAS
 * Purpose        : Bank Balance - a 2x1 KPI card for the Banking dashboard.
 *
 *                  One bank account's current balance, and nothing else:
 *
 *                    ($) Bank Balance   [ HDFC · 12340000009032 v ]
 *
 *                        ₹4.82 Cr
 *
 *                  THE SELECTOR NAMES THE ACCOUNT NUMBER, IN FULL AND UNMASKED, by
 *                  explicit request: two accounts at one bank can be named alike, and
 *                  the pill is the only thing on this card saying whose figure is
 *                  below it. The account's own name ("Current") moves to the card's
 *                  tooltip rather than being dropped.
 *
 *                  THE CARD IS ONE FIGURE. There is no delta, no up/down arrow, no
 *                  "vs last close" and no footer of any kind - all removed by
 *                  requirement, not by omission. No previous balance is read on the
 *                  server either, so there is nothing here that could grow one back.
 *
 *                  THE VALUE IS THE LATEST C_BankAccountLine.EndingBalance for the
 *                  selected account - never a sum of its history, never
 *                  C_BankAccount.CurrentBalance and never a statement balance.
 *
 *                  AN ACCOUNT WITH NO BALANCE LINE SAYS SO. It does not print ₹0: zero
 *                  is a real balance and would be read as one.
 *
 *                  Every figure is in the ACCOUNT's own currency, with that currency's
 *                  symbol and StdPrecision - both resolved server-side from
 *                  C_BankAccount.C_Currency_ID, so no tenant currency and no compact
 *                  unit is assumed here.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, KPI And Summary Widget, Widget Stat Values, Content Fit
 *                  Budget, No Inner Scrollbars) supplies the shell, the header and the
 *                  1.75em SemiBold stat; the supplied widget.html supplies the account
 *                  pill in the header and the single-value body.
 *
 *                  The stat follows design.md rather than the mock's fixed 34px: the
 *                  value is 1.75em on the widget-root lever (28px at the laptop floor,
 *                  35px on a 2K dashboard), which is the same lever every sibling card's
 *                  KPI sits on. A fixed px value would print this card's number larger
 *                  than its neighbours' on a wide dashboard.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Bank Balance                  | VAS_230_BankBalance
 *                   2 | Bank account                  | VAS_230_BankAccountFilter
 *                   3 | No bank balance available     | VAS_230_NoBalance
 *                   4 | No bank accounts available    | VAS_230_NoAccounts
 *                   5 | Balance as of                 | VAS_230_AsOf
 *                   6 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-04
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_230_BankBalanceWidget.css. All classes are
       namespaced `vas-230-` so they never collide with sibling widgets. */

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
       font and a missing glyph leaves an empty box. Explicit width/height as well as a
       viewBox: an SVG with only a viewBox falls back to 300x150px if a stylesheet is
       stale, which would sprawl across the header. */
    var ICONS = {
        currency: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M12 1v22"></path>' +
            '<path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"></path></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>'
    };

    VAS.VAS_230_BankBalanceWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $acctBtn;
        var $value;
        var $state;
        var $busy;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the picker binds document-level handlers. */
        var _ns = '';

        var _accounts = [];
        var _accountId = 0;
        var _accountLabel = '';
        var _accountName = '';
        var _balance = 0;
        var _hasBalance = false;
        var _currencyCode = '';
        var _currencySymbol = '';
        var _precision = 2;
        var _asOf = '';
        var _statementDate = '';

        var _loading = false;
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;

        /* Monotonic request id. Only the newest read may paint - an account the user
           moved off must never overwrite the one they landed on, however the two
           responses happen to arrive. */
        var _reqSeq = 0;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas230_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button and an account change. Created
           visible so it is already up from the moment the widget mounts, which is also why
           no placeholder amount is drawn: the card never shows a stale or mocked figure. */
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

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root,
           which is the first variable the card's font-size clamp reads (the dashboard
           width is only the fallback). Every sibling widget does exactly this - without it
           the card measures itself against the whole dashboard and renders a size larger
           than its neighbours. */
        function setupRootObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _rootObserver = new ResizeObserver(function (entries) {
                    if (!$root || !$root[0]) { return; }

                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                _rootObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        this.intialLoad = function () {
            fetchBalance(_accountId);
        };

        /* The dashboard's Refresh button calls this. The chosen ACCOUNT is kept - that is a
           selection the user made, not a position in a list - and only the figure is
           re-read. */
        this.refreshWidget = function () {
            fetchBalance(_accountId);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-230-root" id="vas-230-root-' + widgetID + '"></div>');

            var title = label('VAS_230_BankBalance', 'Bank Balance');

            /* Header, then ONE value. No footer element exists in this markup at all -
               there is nothing for a delta or a comparison caption to be bound into. */
            $card = $(
                '<div class="vas-230-card">' +
                    '<div class="vas-230-header">' +
                        '<span class="vas-230-icon">' + ICONS.currency + '</span>' +
                        '<div class="vas-230-title"></div>' +
                        '<button type="button" class="vas-230-acct" aria-haspopup="listbox">' +
                            '<span class="vas-230-acct-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-230-body">' +
                        '<div class="vas-230-value"></div>' +
                    '</div>' +
                    '<div class="vas-230-state vas-230-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-230-title').text(title).attr('title', title);

            $acctBtn = $card.find('.vas-230-acct');
            $value = $card.find('.vas-230-value');
            $state = $card.find('.vas-230-state');

            paintAccountLabel();

            $acctBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchBalance(bankAccountId) {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            _reqSeq++;
            var seq = _reqSeq;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_230_BankBalanceWidget/GetBankBalance',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { bankAccountId: bankAccountId },
                success: function (raw) {
                    _loading = false;
                    if (_disposed || seq !== _reqSeq) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* The account list comes back with every read, so an account added or
                       deactivated since the last load is reflected on the next one. */
                    _accounts = data.Accounts || [];

                    if (data.NoAccounts === true) {
                        paintAccountLabel();
                        renderState(label('VAS_230_NoAccounts', 'No bank accounts available'));
                        return;
                    }

                    /* The SERVER's account is the one painted - it validated the id and may
                       have fallen back to another, so echoing its answer is what keeps the
                       pill and the figure describing the same account. */
                    _accountId = Number(data.C_BankAccount_ID) || 0;
                    _accountLabel = data.AccountLabel || '';
                    _accountName = data.AccountName || '';
                    _currencyCode = data.CurrencyCode || '';
                    _currencySymbol = data.CurrencySymbol || '';
                    _precision = precisionOf(data.Precision);
                    _asOf = data.AsOfDate || '';
                    _statementDate = data.StatementDate || '';
                    _hasBalance = data.HasBalance === true;
                    _balance = Number(data.EndingBalance) || 0;

                    paintAccountLabel();
                    paintValue();
                },
                error: function () {
                    _loading = false;
                    if (_disposed || seq !== _reqSeq) { return; }
                    /* The overlay comes down on failure too - a spinner left running over
                       an error the user cannot see is the worst of both. */
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
        /* Render                                                       */
        /* ------------------------------------------------------------ */

        /* A load failure, or a tenant with no accessible account, takes the card over -
           there is no figure to show and no account to name. */
        function renderState(text) {
            $card.find('.vas-230-body').addClass('vas-230-hidden');
            $state.removeClass('vas-230-hidden').text(text);
        }

        function paintAccountLabel() {
            var text = _accountLabel || label('VAS_230_BankAccountFilter', 'Bank account');

            $acctBtn.find('.vas-230-acct-label').text(text);
            $acctBtn.attr('title', label('VAS_230_BankAccountFilter', 'Bank account') + ': ' + text);
        }

        /* The card's one figure: the account's currency symbol at a supporting size, then
           the compact amount. An account with no balance line shows the message instead -
           never a zero it did not read. */
        function paintValue() {
            $state.addClass('vas-230-hidden');
            $card.find('.vas-230-body').removeClass('vas-230-hidden');

            if (!_hasBalance) {
                $value.attr('class', 'vas-230-value vas-230-nil')
                    .attr('title', label('VAS_230_NoBalance', 'No bank balance available'))
                    .text(label('VAS_230_NoBalance', 'No bank balance available'));
                return;
            }

            var negative = _balance < 0;

            $value.attr('class', 'vas-230-value' + (negative ? ' vas-230-neg' : ''))
                .attr('title', valueTooltip())
                .html(
                    (negative ? '<span class="vas-230-sign">−</span>' : '') +
                    '<span class="vas-230-cur">' + escapeHtml(_currencySymbol) + '</span>' +
                    escapeHtml(compactAmount(_balance))
                );
        }

        /* The exact figure behind the compact one, plus which account it belongs to and
           which line it came from. */
        function valueTooltip() {
            var lines = [];

            if (_accountLabel) { lines.push(_accountLabel); }

            /* The account's own name - "Current", "Payroll" - which the pill gives up to the
               account number. Only when it adds something the label does not already say. */
            if (_accountName && _accountName !== _accountLabel) { lines.push(_accountName); }

            lines.push(fullAmount(_balance));

            /* The line's own StatementDate, falling back to the as-of date - the reader
               needs to know how current the figure is, and that is the honest answer. */
            var dated = _statementDate || _asOf;
            if (dated) {
                lines.push(label('VAS_230_AsOf', 'Balance as of') + ' ' + formatDate(dated));
            }

            return lines.join('\n');
        }

        /* ------------------------------------------------------------ */
        /* Bank account picker - anchored under the pill, on <body>      */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-230-pp vas-230-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_230_BankAccountFilter', 'Bank account')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-230-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectAccount(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-230-pp-h">' +
                escapeHtml(label('VAS_230_BankAccountFilter', 'Bank account')) + '</div>';

            /* No "All accounts" option: this card reports ONE account's balance, and
               balances in different currencies cannot be added into an "all" figure. */
            if (_accounts.length === 0) {
                html += '<div class="vas-230-pp-empty">' +
                    escapeHtml(label('VAS_230_NoAccounts', 'No bank accounts available')) + '</div>';
            }

            for (var i = 0; i < _accounts.length; i++) {
                var a = _accounts[i];
                html += optionHtml(Number(a.C_BankAccount_ID) || 0, a.Name || '');
            }

            $picker.html(html);
        }

        function optionHtml(id, text) {
            var selected = id === _accountId;

            return '<button type="button" class="vas-230-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-230-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-230-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is the
           only one that sees every scroll. Scrolling is not a dismissal: the panel travels
           with the pill and closes only on a pick, an outside click or Escape. */
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
            $picker.removeClass('vas-230-hidden');
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
            if ($picker) { $picker.addClass('vas-230-hidden'); }

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

        /* Changing the account re-reads only this widget - the dashboard around it is not
           touched. The label is NOT painted optimistically here: the server validates the
           id and answers with the account it actually reported on, and painting before
           that could name one account over another's figure. */
        function selectAccount(id) {
            if (id === _accountId || id <= 0) { return; }
            fetchBalance(id);
        }

        /* ------------------------------------------------------------ */
        /* Formatting - the ACCOUNT's currency                          */
        /* ------------------------------------------------------------ */
        function precisionOf(value) {
            var p = Number(value);
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        /* Compact magnitude only - the sign and symbol are composed on separately so each
           can carry its own type treatment. The ACCOUNT's ISO code drives the scale, which
           is what decides whether it steps in lakh/crore or million/billion, so no compact
           unit is written into this file. */
        function compactAmount(value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, _currencyCode, _precision);
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }
            return magnitude;
        }

        /* Full, non-compact amount for the tooltip: the exact figure behind the compact
           value. Grouping and the decimal separator come from the browser locale, the
           decimals from the account currency's precision. */
        function fullAmount(value) {
            var v = Number(value) || 0;
            var text = Math.abs(v).toLocaleString(window.navigator.language,
                { minimumFractionDigits: _precision, maximumFractionDigits: _precision });

            return (v < 0 ? '−' : '') + _currencySymbol + text;
        }

        /* Dates arrive as yyyy-MM-dd and are rendered in the browser's locale. Parsed part
           by part rather than through Date(string), which reads a bare ISO date as UTC and
           shifts it a day back for anyone west of Greenwich. */
        function formatDate(isoDate) {
            if (!isoDate) { return ''; }

            var parts = String(isoDate).split('-');
            if (parts.length !== 3) { return String(isoDate); }

            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return String(isoDate); }

            try {
                return d.toLocaleDateString(window.navigator.language,
                    { day: '2-digit', month: 'short', year: 'numeric' });
            }
            catch (e) { return String(isoDate); }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* Every database-sourced string - account names and currency symbols included -
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

        /* Release everything that outlives the card: the body-mounted picker, the document
           and window listeners it registers, and the observer - a ResizeObserver left
           running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($acctBtn) { $acctBtn.off(_ns); }

            _accounts = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_230_BankBalanceWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_230_BankBalanceWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_230_BankBalanceWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
