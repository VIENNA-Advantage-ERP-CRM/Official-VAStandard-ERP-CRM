/**
 * VAS Receipt Search Widget
 * Purpose - Full-width (1x9) type-ahead search that finds AR receipts by
 *           customer, document number, amount, status, reconciled / allocated
 *           flag, bank, bank account, currency, or linked invoice document
 *           number. Selecting a row fires widgetFirevalueChanged with a
 *           TabWhereClause so the host window's payment tab opens that
 *           receipt (TabLayout 'Y' = single/form view), the same host
 *           mechanism the other dashboard widgets use.
 *
 * Data flow:
 *   - Client types -> Receipts/SearchReceipts (debounced, top 10) returns matching receipts.
 *   - Selecting a row -> widgetFirevalueChanged({ TabWhereClause, TabLayout, TabIndex }) -> host zoom.
 *
 * Structure mirrors VIS.SalesInvoiceCustomerSearchWidget.
 *
 * ── Labels / Message Keys (VAS_ prefix) ───────────────────────────────
 *  #  | Current Text                                  | Message Key
 * ----+-----------------------------------------------+----------------------------
 *  1  | Find receipts by customer, number, amount…    | VAS_ReceiptSearchPlaceholder
 *  2  | {n} matches / {n} match                       | VAS_Matches / VAS_Match
 *  3  | for                                           | VAS_For
 *  4  | No receipts match                             | VAS_NoReceiptsMatch
 *  5  | Searching…                                    | VAS_Searching
 *  6  | Clear                                         | VAS_Clear
 *  7  | Reconciled / Unreconciled                     | VAS_Reconciled / VAS_Unreconciled
 *  8  | Allocated / Unallocated                       | VAS_Allocated / VAS_Unallocated
 *  9  | Inv                                           | VAS_InvShort
 * 10  | Account Date / Bank Account / Amount          | VAS_AcctDate / VAS_BankAccount / VAS_Amount
 * 11  | Filters / Currency / From / To                | VAS_063_Filters / VAS_063_Currency /
 *     |                                               | VAS_063_From / VAS_063_To
 * 12  | Apply / Clear all                             | VAS_063_Apply / VAS_063_ClearAll
 * 13  | Range validation messages                     | VAS_063_InvalidRange /
 *     |                                               | VAS_063_InvalidAmountRange
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN) | VAS_StatusDraft etc.
 * ──────────────────────────────────────────────────────────────────────
 *
 * Row layout: initials avatar, customer name, one dot-separated meta line; the amount and the
 * account date hold the RIGHT-hand column so the figures align down the list (same as the
 * sibling VAS_067 / VAS_068 search widgets).
 *
 * Filters (funnel button in the pill, AND'ed on top of the term): Account Date
 * (C_Payment.DateAcct), Bank Account (C_BankAccount_ID), an amount band on PayAmt and a Currency
 * restriction - the same popover VAS_068 uses. Both lookups are framework VTextBoxButton Search
 * controls and the amounts framework VAmountTextBoxes, so the decimal separator and the lookups
 * behave as in a standard window. A filter on its own is a valid search - the term may stay empty.
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the font
       clamps resolve against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    VAS.VAS_ReceiptSearch = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-rsw-root">');

        var $input;
        var $clear;
        /* Dropdown is mounted on <body> (fixed-positioned, anchored to the input)
           so the dashboard cell's overflow can never clip it. */
        var $suggest = null;
        var searchTimer = null;
        var reqSeq = 0;            /* guards against out-of-order responses */
        var cursor = -1;
        var matches = [];
        /* Scroll paging: the dropdown loads 25 rows per page and appends more as
           the user scrolls to the bottom. */
        var curQuery = '';         /* the active query the paging tracks */
        var curPage = 1;           /* last page loaded */
        var hasMore = false;       /* server signalled another page exists */
        var loadingMore = false;   /* a next-page fetch is in flight */

        var ZOOM_WINDOW_NAME_NEW = 'VAS_ARReceipt';
        var windowId = 0;

        /* ── Filter popover (mounted on <body>, like the suggest dropdown) ── */
        var $filterBtn = null, $filters = null;
        /* Applied filters. Dates are ISO yyyy-MM-dd, amounts plain numbers, bank account a
           C_BankAccount_ID and currency a C_Currency_ID; '' / 0 = no bound. They NARROW the term;
           with an empty term they ARE the search. */
        var filterState = {
            acctFrom: '', acctTo: '', bankAccountId: 0, bankAccountName: '',
            amtFrom: '', amtTo: '', currencyId: 0, currencyName: ''
        };
        /* Framework controls inside the popover; null when VIS.Controls is unavailable (the
           popover then falls back to plain inputs / drops the lookup rows). */
        var amtFromCtrl = null, amtToCtrl = null, bankCtrl = null, currencyCtrl = null;

        /* An unseeded AD_Message can come back bracketed OR as the key itself - both mean
           "missing", so the readable English fallback wins in either case. */
        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t !== key && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Document-status chip colours (codes, not the attention buckets). */
        var STATUS = {
            DR: { label: lbl('VAS_StatusDraft', 'Draft'), bg: '#EDEDED', color: '#505050' },
            IP: { label: lbl('VAS_StatusInProgress', 'In Progress'), bg: '#FFF3CD', color: '#9A6500' },
            CO: { label: lbl('VAS_StatusCompleted', 'Completed'), bg: '#CCEFDD', color: '#0C5D38' },
            CL: { label: lbl('VAS_StatusClosed', 'Closed'), bg: '#DFF1FF', color: '#0E5DA8' },
            AP: { label: lbl('VAS_StatusApproved', 'Approved'), bg: '#CCEFDD', color: '#0C5D38' },
            NA: { label: lbl('VAS_StatusNotApproved', 'Not Approved'), bg: '#FFE8E8', color: '#C0392B' },
            WP: { label: lbl('VAS_StatusWaitingPayment', 'Waiting Payment'), bg: '#FFF3CD', color: '#9A6500' },
            WC: { label: lbl('VAS_StatusWaitingConfirm', 'Waiting Confirm'), bg: '#FFF3CD', color: '#9A6500' },
            RE: { label: lbl('VAS_StatusReversed', 'Reversed'), bg: '#FFE8E8', color: '#C0392B' },
            VO: { label: lbl('VAS_StatusVoided', 'Voided'), bg: '#FFE8E8', color: '#C0392B' },
            IN: { label: lbl('VAS_StatusInvalid', 'Invalid'), bg: '#FFE8E8', color: '#C0392B' }
        };

        /* Avatar palette (cycles per row), matching the search-widget design. */
        var PALETTE = ['#0083DA', '#019D89', '#D78B10', '#5F4AA6', '#A33F3F', '#2084C4'];

        /* Two-letter initials from the customer name. */
        function initials(name) {
            if (!name) return '#';
            var parts = name.trim().split(/\s+/);
            if (parts.length >= 2) return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
            return name.substring(0, 2).toUpperCase();
        }

        /* Amount markup: payment-currency symbol before the locale-formatted number. */
        function formatAmount(value, symbol) {
            var p = 2;
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    p = VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { p = 2; }
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: p, maximumFractionDigits: p });
            var sym = symbol ? '<span class="vas-rsw-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + num;
        }

        /* Render the bank info as "Bank · ****1234" when both parts are known. */
        function formatBank(bankName, accountNo) {
            var bn = (bankName || '').trim();
            var an = (accountNo || '').trim();
            var last4 = an.length > 4 ? an.slice(-4) : an;
            if (bn && last4) { return bn + ' · ****' + last4; }
            if (bn) { return bn; }
            if (last4) { return '****' + last4; }
            return '';
        }

        /* Wrap the first case-insensitive match of q in <mark>. */
        function highlight(text, q) {
            text = text == null ? '' : String(text);
            if (!q) return escapeHtml(text);
            var idx = text.toLowerCase().indexOf(q.toLowerCase());
            if (idx === -1) return escapeHtml(text);
            return escapeHtml(text.slice(0, idx)) +
                '<mark>' + escapeHtml(text.slice(idx, idx + q.length)) + '</mark>' +
                escapeHtml(text.slice(idx + q.length));
        }

        function statusLabel(code) {
            var cfg = STATUS[code];
            return cfg ? cfg.label : (code || '—');
        }
        /* Saturated status colour for the meta dot. */
        function statusColor(code) {
            var cfg = STATUS[code];
            return cfg ? cfg.color : '#748494';
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            bindEvents();
        };

        /* ── Build DOM ── */
        function createWidget() {
            var $zone = $('<div class="vas-rsw-zone">');

            var searchSvg =
                '<svg class="vas-rsw-search-icon" width="19" height="19" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>';
            var clearSvg =
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>';

            var $pill = $('<div class="vas-rsw-input">');
            $pill.append(searchSvg);

            $input = $(
                '<input type="text" class="vas-rsw-field" autocomplete="off" ' +
                'role="combobox" aria-autocomplete="list" aria-expanded="false" ' +
                'placeholder="' + escapeHtml(lbl('VAS_ReceiptSearchPlaceholder', 'Find receipts by customer, number, amount, bank, status…')) + '">'
            );

            $clear = $('<button type="button" class="vas-rsw-clear" aria-label="' + escapeHtml(lbl('VAS_Clear', 'Clear')) + '">' + clearSvg + '</button>');

            /* Funnel — opens the filter popover. The dot badge lights up while any filter is
               applied, so the narrowing is never invisible. */
            $filterBtn = $('<button type="button" class="vas-rsw-filter" ' +
                'title="' + escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '" ' +
                'aria-label="' + escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '">' +
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M22 3H2l8 9.46V19l4 2v-8.54L22 3z"/></svg>' +
                '<span class="vas-rsw-filter-dot"></span></button>');

            $pill.append($input).append($clear).append($filterBtn);
            $zone.append($pill);

            /* Gutter band — the pill floats centered with equal empty space left & right
               (the search pill IS the widget, no card wrapper), per design.md
               "Full-Width Dashboard Search Widget". */
            var $esw = $('<div class="vas-rsw-esw">');
            $esw.append($zone);
            $root.append($esw);
        }

        /* ── Suggest dropdown (mounted on <body>) ── */
        function ensureSuggest() {
            if ($suggest) return;
            $suggest = $('<div class="vas-rsw-suggest" role="listbox">');
            $('body').append($suggest);
            $suggest.on('click', '.vas-rsw-line', function () {
                var idx = parseInt($(this).attr('data-index'), 10);
                if (matches[idx]) zoomReceipt(matches[idx].cPaymentId);
            });

            /* Infinite scroll: once near the bottom, pull the next 25-row page. */
            $suggest.on('scroll', function () {
                if (!hasMore || loadingMore) { return; }
                var el = this;
                if (el.scrollTop + el.clientHeight >= el.scrollHeight - 40) {
                    fetchPage(curPage + 1, true);
                }
            });
        }

        /* The search pill both layers are anchored to. */
        function anchorRect() {
            var el = $input.closest('.vas-rsw-input')[0];
            return el ? el.getBoundingClientRect() : null;
        }

        /* Anchor the dropdown to the input pill (fixed positioning). */
        function positionSuggest() {
            if (!$suggest) return;
            var r = anchorRect();
            if (!r) return;
            $suggest.css({ left: r.left + 'px', top: (r.bottom + 8) + 'px', width: r.width + 'px' });
        }

        /* Both layers are position:fixed against the pill, so they must re-anchor on EVERY scroll.
           The dashboard scrolls its OWN container and scroll events do not bubble, so a plain
           window handler never fires and the panels stay behind at stale viewport coordinates -
           hence the capture-phase listeners registered in bindEvents(). Once the pill itself is
           scrolled out of view there is nothing left to anchor to, so both layers close rather
           than float over unrelated dashboard content. */
        function reflowLayers() {
            var suggestOpen = !!($suggest && $suggest.hasClass('is-open'));
            if (!suggestOpen && !filtersOpen()) { return; }
            var r = anchorRect();
            if (!r || r.bottom <= 0 || r.top >= (window.innerHeight || document.documentElement.clientHeight)) {
                closeSuggest();
                closeFilters();
                return;
            }
            if (suggestOpen) { positionSuggest(); }
            if (filtersOpen()) { positionFilters(); }
        }

        function openSuggest() {
            ensureSuggest();
            positionSuggest();
            $suggest.addClass('is-open');
            $input.attr('aria-expanded', 'true');
        }

        function closeSuggest() {
            if ($suggest) $suggest.removeClass('is-open');
            $input.attr('aria-expanded', 'false');
            cursor = -1;
        }

        /* ══════════════ Filter popover ══════════════
           An Account Date range, a bank account, an amount band and a currency restriction; every
           bound optional and open-endable. Dates are native <input type="date"> (always ISO on the
           wire); the amounts, the bank account and the currency use the framework's own controls
           so the decimal separator and the lookups behave exactly as in a standard window.
           Mirrors the VAS_068 AP Payment search popover. */
        function hasFilters() {
            return !!(filterState.acctFrom || filterState.acctTo || filterState.bankAccountId ||
                filterState.amtFrom !== '' || filterState.amtTo !== '' || filterState.currencyId);
        }

        /* "Account Date 2026-01-01 → 2026-01-31" pieces for the count line, so a filter-only
           search still says what it searched for. */
        function filterSummary() {
            var parts = [];
            if (filterState.acctFrom || filterState.acctTo) {
                parts.push(lbl('VAS_AcctDate', 'Account Date') + ' ' +
                    (filterState.acctFrom || '…') + ' → ' + (filterState.acctTo || '…'));
            }
            if (filterState.bankAccountId) {
                parts.push(lbl('VAS_BankAccount', 'Bank Account') + ' ' +
                    (filterState.bankAccountName || filterState.bankAccountId));
            }
            if (filterState.amtFrom !== '' || filterState.amtTo !== '') {
                parts.push(lbl('VAS_Amount', 'Amount') + ' ' +
                    (filterState.amtFrom === '' ? '…' : filterState.amtFrom) + ' → ' +
                    (filterState.amtTo === '' ? '…' : filterState.amtTo));
            }
            if (filterState.currencyId) {
                parts.push(lbl('VAS_063_Currency', 'Currency') + ' ' + (filterState.currencyName || filterState.currencyId));
            }
            return parts.join(' · ');
        }

        function dateRangeHtml(title, fromId, toId) {
            return '<div class="vas-rsw-frow"><div class="vas-rsw-flabel">' + escapeHtml(title) + '</div>' +
                '<div class="vas-rsw-fpair">' +
                '<label><span>' + escapeHtml(lbl('VAS_063_From', 'From')) + '</span><input type="date" id="' + fromId + '"></label>' +
                '<label><span>' + escapeHtml(lbl('VAS_063_To', 'To')) + '</span><input type="date" id="' + toId + '"></label>' +
                '</div></div>';
        }

        /* Amount row carries EMPTY slots; the framework controls are injected once the popover is
           in the DOM (a framework control must be built, then appended). */
        function slotRangeHtml(title, fromSlotId, toSlotId) {
            return '<div class="vas-rsw-frow"><div class="vas-rsw-flabel">' + escapeHtml(title) + '</div>' +
                '<div class="vas-rsw-fpair">' +
                '<label><span>' + escapeHtml(lbl('VAS_063_From', 'From')) + '</span><span class="vas-rsw-fslot" id="' + fromSlotId + '"></span></label>' +
                '<label><span>' + escapeHtml(lbl('VAS_063_To', 'To')) + '</span><span class="vas-rsw-fslot" id="' + toSlotId + '"></span></label>' +
                '</div></div>';
        }

        /* Single full-width lookup row (bank account, currency) - one underline spanning the
           control and its button. */
        function lookupRowHtml(title, slotId) {
            return '<div class="vas-rsw-frow"><div class="vas-rsw-flabel">' + escapeHtml(title) + '</div>' +
                '<div class="vas-rsw-flookup" id="' + slotId + '"></div></div>';
        }

        function frameworkCtrlsAvailable() {
            return !!(window.VIS && VIS.Controls && VIS.DisplayType && VIS.Env);
        }

        function buildAmountControls(uid) {
            var slots = [$filters.find('#' + uid + 'AmtFromSlot'), $filters.find('#' + uid + 'AmtToSlot')];
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VAmountTextBox) {
                slots[0].append('<input type="text" inputmode="decimal" class="vas-rsw-fctrl" id="' + uid + 'AmtFrom">');
                slots[1].append('<input type="text" inputmode="decimal" class="vas-rsw-fctrl" id="' + uid + 'AmtTo">');
                return;
            }
            try {
                var DT = VIS.DisplayType;
                amtFromCtrl = new VIS.Controls.VAmountTextBox('PayAmt', false, false, true, 50, 100, DT.Amount, lbl('VAS_063_From', 'From'));
                amtToCtrl = new VIS.Controls.VAmountTextBox('PayAmt', false, false, true, 50, 100, DT.Amount, lbl('VAS_063_To', 'To'));
                slots[0].append(amtFromCtrl.getControl().addClass('vas-rsw-fctrl').css('width', '100%'));
                slots[1].append(amtToCtrl.getControl().addClass('vas-rsw-fctrl').css('width', '100%'));
            } catch (e) {
                if (window.console) { console.log(e); }
                amtFromCtrl = null; amtToCtrl = null;
            }
        }

        /* One Search lookup (VTextBoxButton + its info button) built into the given slot. The
           where clause is inlined and carries no window-context @tokens@, so windowNo 0 is fine. */
        function buildLookupControl(slotId, columnName, whereClause, onPicked) {
            var $slot = $filters.find('#' + slotId);
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VTextBoxButton || !VIS.MLookupFactory) {
                $slot.closest('.vas-rsw-frow').remove();
                return null;
            }
            try {
                var DT = VIS.DisplayType;
                var lookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), ($self.windowNo > 0 ? $self.windowNo : 0), 0, DT.Search,
                    columnName, 0, false, whereClause);
                var ctrl = new VIS.Controls.VTextBoxButton(columnName, false, false, true, DT.Search, lookup);
                $slot.append(ctrl.getControl().addClass('vas-rsw-fctrl').attr('data-hasbtn', ' ').css('width', '100%'));
                var btn = ctrl.getBtn ? ctrl.getBtn(0) : null;
                if (btn) { $slot.append($('<span class="vas-rsw-fbtnwrap"></span>').append(btn)); }
                /* getValue() only yields the id — keep the display text for the summary line. */
                ctrl.fireValueChanged = function () {
                    onPicked((ctrl.getDisplay ? ctrl.getDisplay() : '') || '');
                };
                return ctrl;
            } catch (e) {
                if (window.console) { console.log(e); }
                $slot.closest('.vas-rsw-frow').remove();
                return null;
            }
        }

        function ctrlDisplay(ctrl) {
            if (!ctrl) { return ''; }
            try { return (ctrl.getDisplay ? ctrl.getDisplay() : '') || ''; }
            catch (e) { return ''; }
        }

        function ensureFilters() {
            if ($filters) { return; }
            /* Ids carry the widget instance so two copies on one dashboard never collide. */
            var uid = 'vasRsw' + ($self.AD_UserHomeWidgetID || 0);
            $filters = $('<div class="vas-rsw-filters" role="dialog" aria-label="' + escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '">');
            $filters.html(
                dateRangeHtml(lbl('VAS_AcctDate', 'Account Date'), uid + 'AcctFrom', uid + 'AcctTo') +
                lookupRowHtml(lbl('VAS_BankAccount', 'Bank Account'), uid + 'BankSlot') +
                slotRangeHtml(lbl('VAS_Amount', 'Amount'), uid + 'AmtFromSlot', uid + 'AmtToSlot') +
                lookupRowHtml(lbl('VAS_063_Currency', 'Currency'), uid + 'CurSlot') +
                '<p class="vas-rsw-ferror" role="alert"></p>' +
                '<div class="vas-rsw-factions">' +
                '<button type="button" class="vas-rsw-fbtn" data-act="clear">' + escapeHtml(lbl('VAS_063_ClearAll', 'Clear all')) + '</button>' +
                '<button type="button" class="vas-rsw-fbtn is-primary" data-act="apply">' + escapeHtml(lbl('VAS_063_Apply', 'Apply')) + '</button>' +
                '</div>');
            $('body').append($filters);

            $filters.data('ids', {
                acctFrom: uid + 'AcctFrom', acctTo: uid + 'AcctTo',
                amtFrom: uid + 'AmtFrom', amtTo: uid + 'AmtTo'
            });
            bankCtrl = buildLookupControl(uid + 'BankSlot', 'C_BankAccount_ID', " C_BankAccount.IsActive = 'Y' ",
                function (text) { filterState.bankAccountName = text; });
            buildAmountControls(uid);
            currencyCtrl = buildLookupControl(uid + 'CurSlot', 'C_Currency_ID', " C_Currency.IsActive = 'Y' ",
                function (text) { filterState.currencyName = text; });

            /* Wrapped, not passed straight through: jQuery would hand the event object to
               applyFilters as its keepOpen argument. */
            $filters.on('click', '[data-act=apply]', function () { applyFilters(false); });
            /* "Clear all" blanks the fields and re-runs the search but KEEPS the popover open. */
            $filters.on('click', '[data-act=clear]', function () { clearFilterInputs(); applyFilters(true); });
            $filters.on('keydown', function (e) {
                if (e.key === 'Escape') { closeFilters(); $input.focus(); return; }
                if (e.key !== 'Enter') { return; }
                /* A framework control owns its own Enter (the lookup searches on it). */
                if ($(e.target).hasClass('vas-rsw-fctrl')) { return; }
                e.preventDefault(); e.stopPropagation();
                applyFilters(false);
            });
        }

        /* Paint one amount control: a number sets it, '' blanks it. setValue(null) leaves a
           formatted zero behind on VAmountTextBox, so an empty bound clears the input directly. */
        function setAmountCtrl(ctrl, fallbackId, value) {
            if (!ctrl) { $filters.find('#' + fallbackId).val(value === '' ? '' : value); return; }
            try {
                if (value === '') { ctrl.setValue(null); ctrl.getControl().val(''); }
                else { ctrl.setValue(value); }
            } catch (e) { if (window.console) { console.log(e); } }
        }

        function clearFilterInputs() {
            var ids = $filters.data('ids');
            $filters.find('input[type=date]').val('');
            setAmountCtrl(amtFromCtrl, ids.amtFrom, '');
            setAmountCtrl(amtToCtrl, ids.amtTo, '');
            if (bankCtrl) {
                try { bankCtrl.setValue(null); } catch (e) { if (window.console) { console.log(e); } }
                filterState.bankAccountName = '';
            }
            if (currencyCtrl) {
                try { currencyCtrl.setValue(null); } catch (e) { if (window.console) { console.log(e); } }
                filterState.currencyName = '';
            }
        }

        /* Read one amount bound: the framework control when present, else the fallback input
           (parsed against the user's decimal separator, never a raw parseFloat). */
        function readAmount(ctrl, fallbackId) {
            if (ctrl) {
                var v = ctrl.getValue();
                return (v === null || v === undefined || v === '' || isNaN(v)) ? '' : Number(v);
            }
            var raw = $.trim($filters.find('#' + fallbackId).val() || '');
            if (!raw) { return ''; }
            var pointed = (VIS.Env && VIS.Env.isDecimalPoint && !VIS.Env.isDecimalPoint())
                ? raw.replace(/\./g, '').replace(',', '.')
                : raw.replace(/,/g, '');
            var n = Number(pointed);
            return isNaN(n) ? '' : n;
        }

        function positionFilters() {
            if (!$filters) { return; }
            var el = $input.closest('.vas-rsw-input')[0];
            if (!el) { return; }
            var r = el.getBoundingClientRect();
            var w = $filters.outerWidth() || 320;
            var left = Math.max(8, Math.min(r.right - w, window.innerWidth - w - 8));
            $filters.css({ left: Math.round(left) + 'px', top: Math.round(r.bottom + 8) + 'px' });
        }

        function filtersOpen() { return !!($filters && $filters.hasClass('is-open')); }

        function openFilters() {
            ensureFilters();
            /* Repaint from the APPLIED state, so a popover closed without applying leaves no
               half-typed values behind. */
            var ids = $filters.data('ids');
            $filters.find('#' + ids.acctFrom).val(filterState.acctFrom);
            $filters.find('#' + ids.acctTo).val(filterState.acctTo);
            setAmountCtrl(amtFromCtrl, ids.amtFrom, filterState.amtFrom);
            setAmountCtrl(amtToCtrl, ids.amtTo, filterState.amtTo);
            if (bankCtrl) {
                try { bankCtrl.setValue(filterState.bankAccountId || null); } catch (e) { if (window.console) { console.log(e); } }
            }
            if (currencyCtrl) {
                try { currencyCtrl.setValue(filterState.currencyId || null); } catch (e) { if (window.console) { console.log(e); } }
            }
            $filters.find('.vas-rsw-ferror').text('');
            $filters.addClass('is-open');
            positionFilters();
            $filters.find('input[type=date]').first().focus();
        }

        function closeFilters() {
            if ($filters) { $filters.removeClass('is-open'); }
        }

        /* Read the popover into the applied state and re-run the search. A reversed range is
           rejected in place (the popover stays open) rather than silently returning nothing.
           keepOpen leaves the popover up afterwards (used by "Clear all"). */
        function applyFilters(keepOpen) {
            var ids = $filters.data('ids');
            var next = {
                acctFrom: $filters.find('#' + ids.acctFrom).val() || '',
                acctTo: $filters.find('#' + ids.acctTo).val() || '',
                bankAccountId: bankCtrl ? (parseInt(bankCtrl.getValue(), 10) || 0) : 0,
                /* Read live rather than trusting fireValueChanged - the lookup may have been set
                   without firing it. */
                bankAccountName: ctrlDisplay(bankCtrl) || filterState.bankAccountName,
                amtFrom: readAmount(amtFromCtrl, ids.amtFrom),
                amtTo: readAmount(amtToCtrl, ids.amtTo),
                currencyId: currencyCtrl ? (parseInt(currencyCtrl.getValue(), 10) || 0) : 0,
                currencyName: ctrlDisplay(currencyCtrl) || filterState.currencyName
            };
            /* 0 in BOTH amount bounds is "no amount filter", not "receipts of exactly zero".
               (A single 0 bound stays meaningful: 0 → 5000 is a real band.) */
            if (next.amtFrom === 0 && next.amtTo === 0) { next.amtFrom = ''; next.amtTo = ''; }
            /* ISO strings compare lexicographically, so a plain > is a correct date compare. */
            if (next.acctFrom && next.acctTo && next.acctFrom > next.acctTo) {
                $filters.find('.vas-rsw-ferror').text(lbl('VAS_063_InvalidRange', '"From" date must be on or before "To"'));
                return;
            }
            if (next.amtFrom !== '' && next.amtTo !== '' && next.amtFrom > next.amtTo) {
                $filters.find('.vas-rsw-ferror').text(lbl('VAS_063_InvalidAmountRange', '"From" amount must be less than or equal to "To"'));
                return;
            }
            if (!next.bankAccountId) { next.bankAccountName = ''; }
            if (!next.currencyId) { next.currencyName = ''; }
            filterState = next;
            $filterBtn.toggleClass('is-on', hasFilters());
            if (!keepOpen) { closeFilters(); }
            if ($input.val().trim() || hasFilters()) { runSearch(); }
            else { closeSuggest(); }
        }

        /* ── Events ── */
        function bindEvents() {
            $root.on('input', '.vas-rsw-field', function () {
                $clear.toggleClass('is-visible', $input.val().length > 0);
                if (searchTimer) clearTimeout(searchTimer);
                var q = $input.val().trim();
                /* A filter on its own is a valid search, so an emptied box keeps searching while
                   any filter is applied. */
                if (!q && !hasFilters()) { closeSuggest(); return; }
                searchTimer = setTimeout(runSearch, 250);
            });

            $root.on('focus', '.vas-rsw-field', function () {
                if ($input.val().trim() || hasFilters()) runSearch();
            });

            $root.on('click', '.vas-rsw-clear', function () {
                $input.val('');
                $clear.removeClass('is-visible');
                /* Clears the TERM only; the filters have their own "Clear all". */
                if (hasFilters()) { runSearch(); } else { closeSuggest(); }
                $input.focus();
            });

            $root.on('click', '.vas-rsw-filter', function (e) {
                e.preventDefault();
                if (filtersOpen()) { closeFilters(); } else { openFilters(); }
            });

            $root.on('keydown', '.vas-rsw-field', function (e) {
                var lines = $suggest ? $suggest.find('.vas-rsw-line') : $();
                if (!$suggest || !$suggest.hasClass('is-open') || lines.length === 0) {
                    if (e.key === 'Escape') closeSuggest();
                    return;
                }
                if (e.key === 'ArrowDown') { e.preventDefault(); cursor = Math.min(cursor + 1, lines.length - 1); paintCursor(lines); }
                else if (e.key === 'ArrowUp') { e.preventDefault(); cursor = Math.max(cursor - 1, 0); paintCursor(lines); }
                else if (e.key === 'Enter') { if (cursor >= 0 && matches[cursor]) { e.preventDefault(); zoomReceipt(matches[cursor].cPaymentId); } }
                else if (e.key === 'Escape') { closeSuggest(); }
            });

            /* Click outside the input zone or the dropdown closes it. The filter popover is
               deliberately NOT closed by an outside click: it holds framework lookups whose Info
               window lives outside all of these elements, and losing half-set filters to a stray
               click is worse than leaving it up. It closes on the funnel toggle, Escape or Apply. */
            $(document).on('mousedown.vasRsw-' + ($self.AD_UserHomeWidgetID || ''), function (e) {
                if ($(e.target).closest('.vas-rsw-zone').length) return;
                if ($suggest && $(e.target).closest($suggest).length) return;
                if ($filters && $(e.target).closest($filters).length) return;
                closeSuggest();
            });

            /* Capture phase (the `true`), NOT jQuery: a scroll inside the dashboard's own
               scrolling container never bubbles to window, so a bubble-phase handler would miss
               it and leave the body-mounted layers stranded mid-page. Registered once for the
               widget's lifetime and removed in disposeComponent. */
            $self._onReflow = reflowLayers;
            window.addEventListener('scroll', $self._onReflow, true);
            window.addEventListener('resize', $self._onReflow, true);
        }

        function paintCursor(lines) {
            lines.each(function (i) { $(this).toggleClass('is-cursor', i === cursor); });
            if (cursor >= 0 && lines[cursor]) lines[cursor].scrollIntoView({ block: 'nearest' });
        }

        /* Defensively unwrap the controller payload (may arrive double-encoded). */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* ── Run a fresh search (resets to page 1) and render the dropdown ── */
        function runSearch() {
            var q = $input.val().trim();
            /* An empty term is still a search while a filter is applied. */
            if (!q && !hasFilters()) { closeSuggest(); return; }

            ensureSuggest();
            $suggest.html('<div class="vas-rsw-state">' + escapeHtml(lbl('VAS_Searching', 'Searching…')) + '</div>');
            openSuggest();

            curQuery = q;
            curPage = 1;
            hasMore = false;
            loadingMore = false;
            matches = [];
            fetchPage(1, false);
        }

        /* Fetch one 25-row page. append=false replaces the list (fresh search);
           append=true adds the next page's rows to the bottom (infinite scroll). */
        function fetchPage(page, append) {
            var q = curQuery;
            if (!q && !hasFilters()) { return; }
            if (append) { loadingMore = true; showLoadMore(true); }

            var mySeq = ++reqSeq;
            $.ajax({
                url: VIS.Application.contextUrl + 'Receipts/SearchReceipts',
                type: 'GET',
                /* Empty bounds go over as '' / 0 and are ignored server-side (open-ended).
                   Amounts are plain invariant numbers - VAmountTextBox has already resolved the
                   user's decimal separator. */
                data: {
                    q: q, max: 25, page: page,
                    acctFrom: filterState.acctFrom, acctTo: filterState.acctTo,
                    bankAccountId: filterState.bankAccountId || 0,
                    amtFrom: filterState.amtFrom, amtTo: filterState.amtTo,
                    currencyId: filterState.currencyId || 0
                },
                success: function (res) {
                    /* Ignore stale responses (a newer keystroke / page already fired). */
                    if (mySeq !== reqSeq) return;
                    var data = parseResponse(res);
                    var rows = (data && data.rows) ? data.rows : [];
                    hasMore = !!(data && data.hasMore);
                    curPage = (data && data.page) ? data.page : page;
                    if (append) { appendSuggest(rows, q); loadingMore = false; }
                    else { renderSuggest(rows, q); }
                },
                error: function () {
                    if (mySeq !== reqSeq) return;
                    hasMore = false;
                    if (append) { loadingMore = false; showLoadMore(false); }
                    else { renderSuggest([], q); }
                }
            });
        }

        /* Header count line. Shows "N+" while more pages remain — the exact total is
           not counted (the server signals hasMore via a single look-ahead row). */
        function metaHtml(q) {
            var word = matches.length === 1 ? lbl('VAS_Match', 'match') : lbl('VAS_Matches', 'matches');
            var count = matches.length + (hasMore ? '+' : '');
            var html = '<div class="vas-rsw-meta"><strong>' + count + '</strong> ' + word;
            if (q) { html += ' ' + lbl('VAS_For', 'for') + ' "' + escapeHtml(q) + '"'; }
            /* Echo the applied filters, so a filter-only search says what it searched for. */
            if (hasFilters()) { html += ' · ' + escapeHtml(filterSummary()); }
            return html + '</div>';
        }

        function updateMeta(q) {
            if (!$suggest) return;
            var $meta = $suggest.find('.vas-rsw-meta');
            if ($meta.length) { $meta.replaceWith(metaHtml(q)); }
        }

        /* Build row markup for a slice, using absolute indices (startIndex + j) so
           data-index maps into `matches` for click / keyboard selection.
           Per-row meta order: status-dot + docNo · bank · currency · status · invoice ref ·
           [allocated / reconciled badges] — data first, tags last. Amount and date are NOT in
           that line: they hold the right-hand column so the figures align down the list, the
           same layout as the sibling VAS_067 / VAS_068 search widgets. */
        function rowsHtml(rows, startIndex, q) {
            var html = '';
            $.each(rows, function (j, r) {
                var i = startIndex + j;
                var color = PALETTE[i % PALETTE.length];

                var pieces = [];
                pieces.push('<span class="vas-rsw-dot" style="background:' + statusColor(r.docStatus) + ';"></span>' + highlight(r.documentNo || '', q));

                var bankText = formatBank(r.bankName, r.accountNo);
                if (bankText) {
                    pieces.push(highlight(bankText, q));
                }

                if (r.currencyIso) {
                    pieces.push(escapeHtml(r.currencyIso));
                }

                pieces.push(escapeHtml(statusLabel(r.docStatus)));

                if (r.invoiceDocumentNo) {
                    pieces.push(escapeHtml(lbl('VAS_InvShort', 'Inv')) + ' ' + highlight(r.invoiceDocumentNo, q));
                }

                if (r.isAllocated) {
                    pieces.push('<span class="vas-rsw-flag is-on">' + escapeHtml(lbl('VAS_Allocated', 'Allocated')) + '</span>');
                }
                if (r.isReconciled) {
                    pieces.push('<span class="vas-rsw-flag is-on">' + escapeHtml(lbl('VAS_Reconciled', 'Reconciled')) + '</span>');
                }

                var meta = pieces.join(' · ');

                html += '<div class="vas-rsw-line" data-index="' + i + '" role="option">' +
                    '<div class="vas-rsw-avatar" style="background:' + color + ';">' + escapeHtml(initials(r.customerName)) + '</div>' +
                    '<div class="vas-rsw-info">' +
                    '<div class="vas-rsw-name">' + highlight(r.customerName || '—', q) + '</div>' +
                    '<div class="vas-rsw-rowmeta">' + meta + '</div>' +
                    '</div>' +
                    '<div class="vas-rsw-metacol">' +
                    '<div class="vas-rsw-amount">' + formatAmount(r.payAmount, r.curSymbol) + '</div>' +
                    '<div class="vas-rsw-date">' + escapeHtml(r.dateAcct || '') + '</div>' +
                    '</div>' +
                    '</div>';
            });
            return html;
        }

        /* Fresh render (page 1): replace the whole dropdown body. */
        function renderSuggest(rows, q) {
            matches = rows || [];
            cursor = -1;
            ensureSuggest();

            if (matches.length === 0) {
                hasMore = false;
                /* A filter-only search has no term to quote back. */
                var empty = q
                    ? lbl('VAS_NoReceiptsMatch', 'No receipts match') + ' "<strong>' + escapeHtml(q) + '</strong>".'
                    : lbl('VAS_NoReceiptsMatch', 'No receipts match') + '.';
                $suggest.html('<div class="vas-rsw-empty">' + empty + '</div>');
                openSuggest();
                return;
            }

            $suggest.html(metaHtml(q) + '<div class="vas-rsw-list">' + rowsHtml(matches, 0, q) + '</div>');
            $suggest.scrollTop(0);
            openSuggest();
        }

        /* Append the next page's rows to the bottom (infinite scroll). */
        function appendSuggest(rows, q) {
            ensureSuggest();
            showLoadMore(false);
            if (!rows || rows.length === 0) { updateMeta(q); return; }

            var startIndex = matches.length;
            matches = matches.concat(rows);

            var $list = $suggest.find('.vas-rsw-list');
            if ($list.length === 0) { renderSuggest(matches, q); return; }

            $list.append(rowsHtml(rows, startIndex, q));
            updateMeta(q);
        }

        /* Show / hide the bottom "Loading more…" indicator during a page fetch. */
        function showLoadMore(show) {
            if (!$suggest) return;
            var $more = $suggest.find('.vas-rsw-more');
            if (show) {
                if ($more.length === 0) {
                    $suggest.append('<div class="vas-rsw-more">' + escapeHtml(lbl('VAS_LoadingMore', 'Loading more…')) + '</div>');
                }
            } else {
                $more.remove();
            }
        }

        /* ── Zoom: fire the value the host listens for to open this receipt in the
           window's first tab, filtered to the single record (single/form layout). ── */
        function zoomReceipt(cPaymentId) {
            if (!cPaymentId) return;
            closeSuggest();
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Payment.C_Payment_ID=" + cPaymentId,
                    "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                    "TabIndex": "0"
                });
            }
            else {
                VAS.ZoomUtil.zoomToRecord("C_Payment_ID", cPaymentId, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            closeSuggest();
            if ($input) { $input.val(''); }
            if ($clear) { $clear.removeClass('is-visible'); }
            matches = [];
            /* A refresh resets the filters too, so the widget comes back in its neutral state. */
            closeFilters();
            filterState = {
                acctFrom: '', acctTo: '', bankAccountId: 0, bankAccountName: '',
                amtFrom: '', amtTo: '', currencyId: 0, currencyName: ''
            };
            if ($filterBtn) { $filterBtn.removeClass('is-on'); }
            if ($filters) { clearFilterInputs(); }
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('mousedown.vasRsw-' + ($self.AD_UserHomeWidgetID || ''));
            if ($self._onReflow) {
                window.removeEventListener('scroll', $self._onReflow, true);
                window.removeEventListener('resize', $self._onReflow, true);
                $self._onReflow = null;
            }
            if (searchTimer) clearTimeout(searchTimer);
            if ($suggest) { $suggest.remove(); $suggest = null; }
            /* Both body-mounted layers must go with the widget, or they outlive the dashboard cell. */
            if ($filters) { $filters.remove(); $filters = null; }
            amtFromCtrl = null; amtToCtrl = null; bankCtrl = null; currencyCtrl = null;
            $root.remove();
        };
    };

    VAS.VAS_ReceiptSearch.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay the fired value (zoom params) to the registered widget host. */
    VAS.VAS_ReceiptSearch.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (zoom). */
    VAS.VAS_ReceiptSearch.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_ReceiptSearch.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the font clamps read. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_ReceiptSearch.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_ReceiptSearch.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VAS, jQuery);
