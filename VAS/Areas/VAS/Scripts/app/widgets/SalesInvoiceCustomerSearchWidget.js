/**
 * Sales Invoice Customer Search Widget
 * Purpose - Full-width (1x9) type-ahead search that finds sales invoices by customer, document
 *           number, document type, amount or status and zooms the chosen record. Selecting a
 *           suggestion fires widgetFirevalueChanged with a TabWhereClause so the host window's
 *           invoice tab opens that record (TabLayout 'Y' = single/form view), the same host
 *           mechanism the other dashboard widgets use.
 *
 * Search surface:
 *   - Free text (OR'ed server-side): document number, customer name, TARGET DOCUMENT TYPE
 *     (C_DocTypeTarget_ID - by C_DocType.Name "Credit Memo" or DocBaseType code "ARC"), SALES ORDER
 *     document number (an order's number lists every invoice raised against it - header
 *     C_Invoice.C_Order_ID and line C_InvoiceLine.C_OrderLine_ID both count), document status
 *     keyword, paid / unpaid, and grand-total amount when numeric.
 *   - Filters (funnel button, AND'ed on top of the text): Invoice Date (C_Invoice.DateInvoiced),
 *     Due Date (C_InvoicePaySchedule.DueDate), a grand-total Amount band, and a Currency
 *     restriction (C_Invoice.C_Currency_ID). The amounts use the framework VAmountTextBox and the
 *     currency the framework VTextBoxButton lookup on C_Currency_ID, so the decimal separator and
 *     the lookup behave exactly as in a standard window. A filter on its own is a valid search -
 *     the text box may stay empty.
 *
 * Data flow:
 *   - Client types / applies a filter -> Invoices/SearchInvoices (debounced, 25 per scroll page).
 *   - Selecting a row -> widgetFirevalueChanged({ TabWhereClause, TabLayout, TabIndex }) -> host zoom.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                  | Message Key            | MsgText
 * ----+-----------------------------------------------+------------------------+----------------------------
 *  1  | Find invoices by customer, number, amount…    | VAS_063_InvoiceSearchPlaceholder | Find invoices by customer, number, document type, amount or status…
 *  2  | {n} matches / {n} match                       | VAS_063_Matches / VAS_063_Match | matches / match
 *  3  | for                                           | VAS_063_For            | for
 *  4  | No invoices match                             | VAS_063_NoInvoicesMatch | No invoices match
 *  5  | Searching…                                    | VAS_063_Searching      | Searching…
 *  6  | Clear                                         | VAS_063_Clear          | Clear
 *  7  | Filters                                       | VAS_063_Filters        | Filters
 *  8  | Invoice Date                                  | VAS_063_InvoiceDate    | Invoice Date
 *  9  | Due Date                                      | VAS_063_DueDate        | Due Date
 * 10  | From / To                                     | VAS_063_From / VAS_063_To | From / To
 * 11  | Apply                                         | VAS_063_Apply          | Apply
 * 12  | Clear all                                     | VAS_063_ClearAll       | Clear all
 * 13  | "From" date must be on or before "To"         | VAS_063_InvalidRange   | "From" date must be on or before "To"
 * 14  | the selected date range                       | VAS_063_SelectedRange  | the selected filters
 * 15  | Amount                                        | VAS_063_Amount         | Amount
 * 16  | Currency                                      | VAS_063_Currency       | Currency
 * 17  | "From" amount must be <= "To"                 | VAS_063_InvalidAmountRange | "From" amount must be less than or equal to "To"
 * 18  | SO (sales order prefix on a row)              | VAS_063_SalesOrder     | SO
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN) | VAS_063_StatusDraft etc. | Draft / In Progress / ...
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
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

    VIS.SalesInvoiceCustomerSearchWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-sics-root">');

        var $input;
        var $clear;
        var $filterBtn;
        /* The dropdown is mounted on <body> (fixed-positioned, anchored to the input) so the dashboard
           cell's overflow can never clip it. */
        var $suggest = null;
        /* Filter popover — mounted on <body> for the same reason as the dropdown. */
        var $filters = null;
        /* Applied filters. Dates are ISO yyyy-MM-dd, amounts are plain numbers, currency is a
           C_Currency_ID; '' / 0 means "no bound". These NARROW the text search; when the text box is
           empty they ARE the search. */
        var filterState = {
            invFrom: '', invTo: '', dueFrom: '', dueTo: '',
            amtFrom: '', amtTo: '', currencyId: 0, currencyName: ''
        };
        /* Framework controls inside the popover (built once, in ensureFilters): two VAmountTextBox
           for the amount band and a VTextBoxButton lookup on C_Currency_ID. Null when VIS.Controls
           isn't available — the popover then falls back to plain inputs / hides the currency row. */
        var amtFromCtrl = null, amtToCtrl = null, currencyCtrl = null;
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

        var ZOOM_WINDOW_NAME_NEW = 'VAS_ARInvoice';
        var windowId = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Document-status chip colours (codes, not the attention buckets). */
        var STATUS = {
            DR: { label: lbl('VAS_063_StatusDraft', 'Draft'), bg: '#EDEDED', color: '#505050' },
            IP: { label: lbl('VAS_063_StatusInProgress', 'In Progress'), bg: '#FFF3CD', color: '#9A6500' },
            CO: { label: lbl('VAS_063_StatusCompleted', 'Completed'), bg: '#CCEFDD', color: '#0C5D38' },
            CL: { label: lbl('VAS_063_StatusClosed', 'Closed'), bg: '#DFF1FF', color: '#0E5DA8' },
            AP: { label: lbl('VAS_063_StatusApproved', 'Approved'), bg: '#CCEFDD', color: '#0C5D38' },
            NA: { label: lbl('VAS_063_StatusNotApproved', 'Not Approved'), bg: '#FFE8E8', color: '#C0392B' },
            WP: { label: lbl('VAS_063_StatusWaitingPayment', 'Waiting Payment'), bg: '#FFF3CD', color: '#9A6500' },
            WC: { label: lbl('VAS_063_StatusWaitingConfirm', 'Waiting Confirm'), bg: '#FFF3CD', color: '#9A6500' },
            RE: { label: lbl('VAS_063_StatusReversed', 'Reversed'), bg: '#FFE8E8', color: '#C0392B' },
            VO: { label: lbl('VAS_063_StatusVoided', 'Voided'), bg: '#FFE8E8', color: '#C0392B' },
            IN: { label: lbl('VAS_063_StatusInvalid', 'Invalid'), bg: '#FFE8E8', color: '#C0392B' }
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

        /* Amount markup: invoice-currency symbol before the locale-formatted number. */
        function formatAmount(value, symbol) {
            var p = VIS.Env.getCtx().getStdPrecision();
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: p, maximumFractionDigits: p });
            var sym = symbol ? '<span class="vas-sics-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + num;
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
        /* Saturated status colour for the meta dot (the chip backgrounds are too pale for a dot). */
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
            var $zone = $('<div class="vas-sics-zone">');

            var searchSvg =
                '<svg class="vas-sics-search-icon" width="19" height="19" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>';
            var clearSvg =
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>';
            /* Funnel — opens the Invoice Date / Due Date range popover. */
            var filterSvg =
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M22 3H2l8 9.46V19l4 2v-8.54L22 3z"/></svg>';

            var $pill = $('<div class="vas-sics-input">');
            $pill.append(searchSvg);

            $input = $(
                '<input type="text" class="vas-sics-field" autocomplete="off" ' +
                'role="combobox" aria-autocomplete="list" aria-expanded="false" ' +
                'placeholder="' + escapeHtml(lbl('VAS_063_InvoiceSearchPlaceholder', 'Find invoices by Customer, Document No., Document Type, Amount or Document Status…')) + '">'
            );

            $clear = $('<button type="button" class="vas-sics-clear" aria-label="' + escapeHtml(lbl('VAS_063_Clear', 'Clear')) + '">' + clearSvg + '</button>');

            /* The dot badge lights up while any range is applied, so the narrowing is never invisible. */
            $filterBtn = $('<button type="button" class="vas-sics-filter" aria-haspopup="dialog" aria-expanded="false" ' +
                'title="' + escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '" ' +
                'aria-label="' + escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '">' +
                filterSvg + '<span class="vas-sics-filter-dot"></span></button>');

            $pill.append($input).append($clear).append($filterBtn);
            $zone.append($pill);

            /* Gutter band: the pill floats centered with equal empty space left & right (the search
               pill IS the widget — no card wrapper), per the attached design and design.md
               "Full-Width Dashboard Search Widget". */
            var $esw = $('<div class="vas-sics-esw">');
            $esw.append($zone);
            $root.append($esw);
        }

        /* ── Suggest dropdown (mounted on <body>) ── */
        function ensureSuggest() {
            if ($suggest) return;
            $suggest = $('<div class="vas-sics-suggest" role="listbox">');
            $('body').append($suggest);
            /* Clicks inside the dropdown act on its rows; outside clicks close it. */
            $suggest.on('click', '.vas-sics-line', function () {
                var idx = parseInt($(this).attr('data-index'), 10);
                if (matches[idx]) zoomInvoice(matches[idx].cInvoiceId);
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

        /* Anchor the dropdown to the input pill (fixed positioning). The results always sit directly
           under the pill; the filter popover OVERLAYS them (higher layer) rather than pushing them
           down, so the list keeps its place while filters are being set. */
        function positionSuggest() {
            if (!$suggest) return;
            var el = $input.closest('.vas-sics-input')[0];
            if (!el) return;
            var r = el.getBoundingClientRect();
            $suggest.css({ left: r.left + 'px', top: (r.bottom + 8) + 'px', width: r.width + 'px' });
        }

        /* Both layers are position:fixed and anchored to the pill, so they must be re-anchored on
           every scroll. The dashboard scrolls its OWN container (not the window) and scroll events
           do not bubble, so a $(window).on('scroll') binding never fires for it — the listener is
           registered on document in the CAPTURE phase instead, which sees scrolls from any element
           on the way down. */
        function repositionLayers() {
            if ($suggest && $suggest.hasClass('is-open')) { positionSuggest(); }
            if (filtersOpen()) { positionFilters(); }
        }

        /* Both layers are mounted on <body>, so they outlive the dashboard being hidden — moving to
           another window would leave them floating over it. The shared watchdog closes them as soon
           as the pill itself stops being laid out (see VAS_OverlayWatch). */
        var overlayWatch = VAS.OverlayWatch({
            anchor: function () { return $input ? $input.closest('.vas-sics-input')[0] : null; },
            isOpen: function () { return !!($suggest && $suggest.hasClass('is-open')) || filtersOpen(); },
            onHidden: function () { closeSuggest(); closeFilters(); }
        });

        function bindReposition() {
            document.addEventListener('scroll', repositionLayers, true);
            window.addEventListener('resize', repositionLayers);
            overlayWatch.start();
        }

        /* Unbound only once BOTH layers are down — either one still open needs the listener. */
        function unbindReposition() {
            if ((($suggest && $suggest.hasClass('is-open'))) || filtersOpen()) { return; }
            document.removeEventListener('scroll', repositionLayers, true);
            window.removeEventListener('resize', repositionLayers);
            overlayWatch.stop();
        }

        function openSuggest() {
            ensureSuggest();
            positionSuggest();
            $suggest.addClass('is-open');
            $input.attr('aria-expanded', 'true');
            bindReposition();
        }

        function closeSuggest() {
            if ($suggest) $suggest.removeClass('is-open');
            $input.attr('aria-expanded', 'false');
            cursor = -1;
            unbindReposition();
        }

        /* ── Filter popover ──
           Two date ranges (Invoice Date -> C_Invoice.DateInvoiced, Due Date ->
           C_InvoicePaySchedule.DueDate), a grand-total amount band, and a currency restriction.
           Every bound is optional and open-endable. Dates are native <input type="date"> so they are
           always ISO yyyy-MM-dd on the wire; the amounts and the currency use the framework's own
           controls (VAmountTextBox / VTextBoxButton) so the decimal separator and the C_Currency_ID
           lookup behave exactly as they do in a standard window. */
        function hasFilters() {
            return !!(filterState.invFrom || filterState.invTo || filterState.dueFrom || filterState.dueTo ||
                filterState.amtFrom !== '' || filterState.amtTo !== '' || filterState.currencyId);
        }

        /* Human-readable "Invoice Date 2026-01-01 → 2026-01-31" pieces, used by the count line and
           the empty state so a filter-only search still says what it searched for. */
        function filterSummary() {
            var parts = [];
            if (filterState.invFrom || filterState.invTo) {
                parts.push(lbl('VAS_063_InvoiceDate', 'Invoice Date') + ' ' +
                    (filterState.invFrom || '…') + ' → ' + (filterState.invTo || '…'));
            }
            if (filterState.dueFrom || filterState.dueTo) {
                parts.push(lbl('VAS_063_DueDate', 'Due Date') + ' ' +
                    (filterState.dueFrom || '…') + ' → ' + (filterState.dueTo || '…'));
            }
            if (filterState.amtFrom !== '' || filterState.amtTo !== '') {
                parts.push(lbl('VAS_063_Amount', 'Amount') + ' ' +
                    (filterState.amtFrom === '' ? '…' : filterState.amtFrom) + ' → ' +
                    (filterState.amtTo === '' ? '…' : filterState.amtTo));
            }
            if (filterState.currencyId) {
                parts.push(lbl('VAS_063_Currency', 'Currency') + ' ' + (filterState.currencyName || filterState.currencyId));
            }
            return parts.join(' · ');
        }

        function dateRangeHtml(title, fromId, toId) {
            return '<div class="vas-sics-frow">' +
                '<div class="vas-sics-flabel">' + escapeHtml(title) + '</div>' +
                '<div class="vas-sics-fpair">' +
                '<label><span>' + escapeHtml(lbl('VAS_063_From', 'From')) + '</span>' +
                '<input type="date" id="' + fromId + '"></label>' +
                '<label><span>' + escapeHtml(lbl('VAS_063_To', 'To')) + '</span>' +
                '<input type="date" id="' + toId + '"></label>' +
                '</div></div>';
        }

        /* Amount / currency rows carry EMPTY slots; the framework controls are injected into them
           after the popover is in the DOM (a framework control must be built, then appended). */
        function slotRangeHtml(title, fromSlotId, toSlotId) {
            return '<div class="vas-sics-frow">' +
                '<div class="vas-sics-flabel">' + escapeHtml(title) + '</div>' +
                '<div class="vas-sics-fpair">' +
                '<label><span>' + escapeHtml(lbl('VAS_063_From', 'From')) + '</span>' +
                '<span class="vas-sics-fslot" id="' + fromSlotId + '"></span></label>' +
                '<label><span>' + escapeHtml(lbl('VAS_063_To', 'To')) + '</span>' +
                '<span class="vas-sics-fslot" id="' + toSlotId + '"></span></label>' +
                '</div></div>';
        }

        function frameworkCtrlsAvailable() {
            return !!(window.VIS && VIS.Controls && VIS.DisplayType && VIS.Env);
        }

        /* Grand-total bounds. VAmountTextBox.getValue() resolves the user's decimal separator and
           returns a plain number, so the widget never parses amounts itself (and never posts a
           locale-formatted string). Falls back to a plain decimal input when VIS.Controls is absent. */
        function buildAmountControls(uid) {
            var slots = [$filters.find('#' + uid + 'AmtFromSlot'), $filters.find('#' + uid + 'AmtToSlot')];
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VAmountTextBox) {
                slots[0].append('<input type="text" inputmode="decimal" class="vas-sics-fctrl" id="' + uid + 'AmtFrom">');
                slots[1].append('<input type="text" inputmode="decimal" class="vas-sics-fctrl" id="' + uid + 'AmtTo">');
                return;
            }
            try {
                var DT = VIS.DisplayType;
                amtFromCtrl = new VIS.Controls.VAmountTextBox('GrandTotal', false, false, true, 50, 100, DT.Amount, lbl('VAS_063_From', 'From'));
                amtToCtrl = new VIS.Controls.VAmountTextBox('GrandTotal', false, false, true, 50, 100, DT.Amount, lbl('VAS_063_To', 'To'));
                slots[0].append(amtFromCtrl.getControl().addClass('vas-sics-fctrl').css('width', '100%'));
                slots[1].append(amtToCtrl.getControl().addClass('vas-sics-fctrl').css('width', '100%'));
            } catch (e) {
                if (window.console) { console.log(e); }
                amtFromCtrl = null; amtToCtrl = null;
            }
        }

        /* Currency picker: the framework's C_Currency_ID Search lookup (VTextBoxButton + its info
           button), the same control a standard window renders for that column. The where clause is
           inlined and carries no window-context @tokens@, so windowNo 0 is fine for the lookup. */
        function buildCurrencyControl(uid) {
            var $slot = $filters.find('#' + uid + 'CurSlot');
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VTextBoxButton || !VIS.MLookupFactory) {
                $slot.closest('.vas-sics-frow').remove();   // no framework lookup -> no currency row
                return;
            }
            try {
                var DT = VIS.DisplayType;
                var lookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), ($self.windowNo || 0), 0, DT.Search,
                    'C_Currency_ID', 0, false, " C_Currency.IsActive = 'Y' ");
                currencyCtrl = new VIS.Controls.VTextBoxButton('C_Currency_ID', false, false, true, DT.Search, lookup);
                $slot.append(currencyCtrl.getControl().addClass('vas-sics-fctrl').attr('data-hasbtn', ' ').css('width', '100%'));
                var btn = currencyCtrl.getBtn ? currencyCtrl.getBtn(0) : null;
                if (btn) { $slot.append($('<span class="vas-sics-fbtnwrap"></span>').append(btn)); }
                /* Keep the display text for the summary line — getValue() only yields the id. */
                currencyCtrl.fireValueChanged = function () {
                    filterState.currencyName = (currencyCtrl.getDisplay ? currencyCtrl.getDisplay() : '') || '';
                };
            } catch (e) {
                if (window.console) { console.log(e); }
                currencyCtrl = null;
                $slot.closest('.vas-sics-frow').remove();
            }
        }

        function ensureFilters() {
            if ($filters) return;
            /* Ids are suffixed with the widget instance so two copies of the widget on one
               dashboard never share an input id. */
            var uid = 'vasSics' + ($self.AD_UserHomeWidgetID || $self.windowNo || '0');
            $filters = $('<div class="vas-sics-filters" role="dialog" aria-label="' +
                escapeHtml(lbl('VAS_063_Filters', 'Filters')) + '">');
            $filters.html(
                dateRangeHtml(lbl('VAS_063_InvoiceDate', 'Invoice Date'), uid + 'InvFrom', uid + 'InvTo') +
                dateRangeHtml(lbl('VAS_063_DueDate', 'Due Date'), uid + 'DueFrom', uid + 'DueTo') +
                slotRangeHtml(lbl('VAS_063_Amount', 'Amount'), uid + 'AmtFromSlot', uid + 'AmtToSlot') +
                '<div class="vas-sics-frow">' +
                '<div class="vas-sics-flabel">' + escapeHtml(lbl('VAS_063_Currency', 'Currency')) + '</div>' +
                '<div class="vas-sics-fcur" id="' + uid + 'CurSlot"></div></div>' +
                '<p class="vas-sics-ferror" role="alert"></p>' +
                '<div class="vas-sics-factions">' +
                '<button type="button" class="vas-sics-fbtn" data-act="clear">' + escapeHtml(lbl('VAS_063_ClearAll', 'Clear all')) + '</button>' +
                '<button type="button" class="vas-sics-fbtn vas-sics-fbtn--primary" data-act="apply">' + escapeHtml(lbl('VAS_063_Apply', 'Apply')) + '</button>' +
                '</div>');
            $('body').append($filters);

            $filters.data('ids', {
                invFrom: uid + 'InvFrom', invTo: uid + 'InvTo', dueFrom: uid + 'DueFrom', dueTo: uid + 'DueTo',
                amtFrom: uid + 'AmtFrom', amtTo: uid + 'AmtTo'
            });
            buildAmountControls(uid);
            buildCurrencyControl(uid);

            /* Wrapped, not passed straight through: jQuery would hand the event object to
               applyFilters as its keepOpen argument. */
            $filters.on('click', '[data-act=apply]', function () { applyFilters(false); });
            /* "Clear all" blanks the fields and re-runs the search but KEEPS the popover open, so
               the user can immediately dial in a different filter. */
            $filters.on('click', '[data-act=clear]', function () {
                clearFilterInputs();
                applyFilters(true);
            });
            /* Enter in a plain field applies, Escape closes. Both are wired explicitly and stop the
               bubble, because the framework shell swallows those keys on its own global handler. */
            $filters.on('keydown', function (e) {
                if (e.key === 'Escape') { closeFilters(); $input.focus(); return; }
                if (e.key !== 'Enter') return;
                /* A framework control owns its own Enter (the currency lookup runs its search on it),
                   so only the plain fields apply the filters. */
                if ($(e.target).hasClass('vas-sics-fctrl')) return;
                e.preventDefault(); e.stopPropagation();
                applyFilters();
            });
        }

        /* Paint one amount control: a number sets it, '' blanks it. setValue(null) leaves a formatted
           zero behind on VAmountTextBox, so an empty bound clears the underlying input directly. */
        function setAmountCtrl(ctrl, fallbackId, value) {
            if (!ctrl) { $filters.find('#' + fallbackId).val(value === '' ? '' : value); return; }
            try {
                if (value === '') { ctrl.setValue(null); ctrl.getControl().val(''); }
                else { ctrl.setValue(value); }
            } catch (e) { if (window.console) { console.log(e); } }
        }

        /* Blank every control in the popover (used by "Clear all" and by refreshWidget). */
        function clearFilterInputs() {
            var ids = $filters.data('ids');
            $filters.find('input[type=date]').val('');
            setAmountCtrl(amtFromCtrl, ids.amtFrom, '');
            setAmountCtrl(amtToCtrl, ids.amtTo, '');
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
            var raw = ($filters.find('#' + fallbackId).val() || '').trim();
            if (!raw) { return ''; }
            /* isDecimalPoint() false -> the user types "1.234,56": strip dots, comma is the point. */
            var pointed = (VIS.Env && VIS.Env.isDecimalPoint && !VIS.Env.isDecimalPoint())
                ? raw.replace(/\./g, '').replace(',', '.')
                : raw.replace(/,/g, '');
            var n = Number(pointed);
            return isNaN(n) ? '' : n;
        }

        /* Anchor under the pill's right edge (fixed positioning, same as the dropdown). */
        function positionFilters() {
            if (!$filters) return;
            var el = $input.closest('.vas-sics-input')[0];
            if (!el) return;
            var r = el.getBoundingClientRect();
            var w = $filters.outerWidth() || 320;
            var left = Math.max(8, Math.min(r.right - w, window.innerWidth - w - 8));
            $filters.css({ left: left + 'px', top: (r.bottom + 8) + 'px' });
        }

        function openFilters() {
            ensureFilters();
            /* Repaint the controls from the APPLIED state, so a popover closed without applying does
               not leave half-typed values behind. */
            var ids = $filters.data('ids');
            $filters.find('#' + ids.invFrom).val(filterState.invFrom);
            $filters.find('#' + ids.invTo).val(filterState.invTo);
            $filters.find('#' + ids.dueFrom).val(filterState.dueFrom);
            $filters.find('#' + ids.dueTo).val(filterState.dueTo);
            setAmountCtrl(amtFromCtrl, ids.amtFrom, filterState.amtFrom);
            setAmountCtrl(amtToCtrl, ids.amtTo, filterState.amtTo);
            if (currencyCtrl) {
                try { currencyCtrl.setValue(filterState.currencyId || null); } catch (e) { if (window.console) { console.log(e); } }
            }
            $filters.find('.vas-sics-ferror').text('');
            $filters.addClass('is-open');
            positionFilters();
            $filterBtn.attr('aria-expanded', 'true');
            bindReposition();
            $filters.find('input[type=date]').first().focus();
        }

        function closeFilters() {
            if ($filters) $filters.removeClass('is-open');
            if ($filterBtn) $filterBtn.attr('aria-expanded', 'false');
            unbindReposition();
        }

        function filtersOpen() {
            return !!($filters && $filters.hasClass('is-open'));
        }

        /* Read the popover into the applied state and re-run the search. A reversed range is
           rejected in place (the popover stays open) rather than silently returning nothing.
           keepOpen leaves the popover up afterwards (used by "Clear all"). */
        function applyFilters(keepOpen) {
            var ids = $filters.data('ids');
            var next = {
                invFrom: $filters.find('#' + ids.invFrom).val() || '',
                invTo: $filters.find('#' + ids.invTo).val() || '',
                dueFrom: $filters.find('#' + ids.dueFrom).val() || '',
                dueTo: $filters.find('#' + ids.dueTo).val() || '',
                amtFrom: readAmount(amtFromCtrl, ids.amtFrom),
                amtTo: readAmount(amtToCtrl, ids.amtTo),
                currencyId: currencyCtrl ? (parseInt(currencyCtrl.getValue(), 10) || 0) : 0,
                currencyName: filterState.currencyName
            };
            /* 0 in BOTH amount bounds is "no amount filter", not "invoices totalling exactly zero" —
               a zeroed pair is what the controls read back when the user clears them by typing 0.
               (A single 0 bound stays meaningful: 0 → 5000 is a real band.) */
            if (next.amtFrom === 0 && next.amtTo === 0) { next.amtFrom = ''; next.amtTo = ''; }
            if ((next.invFrom && next.invTo && next.invFrom > next.invTo) ||
                (next.dueFrom && next.dueTo && next.dueFrom > next.dueTo)) {
                /* ISO strings compare lexicographically, so a plain > is a correct date compare. */
                $filters.find('.vas-sics-ferror').text(lbl('VAS_063_InvalidRange', '"From" date must be on or before "To"'));
                return;
            }
            if (next.amtFrom !== '' && next.amtTo !== '' && next.amtFrom > next.amtTo) {
                $filters.find('.vas-sics-ferror').text(lbl('VAS_063_InvalidAmountRange', '"From" amount must be less than or equal to "To"'));
                return;
            }
            if (!next.currencyId) { next.currencyName = ''; }
            filterState = next;
            $filterBtn.toggleClass('is-active', hasFilters());
            if (!keepOpen) { closeFilters(); }
            if ($input.val().trim() || hasFilters()) { runSearch(); }
            else { closeSuggest(); }
        }

        /* ── Events ── */
        function bindEvents() {
            $root.on('input', '.vas-sics-field', function () {
                $clear.toggleClass('is-visible', $input.val().length > 0);
                if (searchTimer) clearTimeout(searchTimer);
                var q = $input.val().trim();
                /* Emptying the box does NOT end the search while a date range is applied — the
                   range on its own is a valid search. */
                if (!q && !hasFilters()) { closeSuggest(); return; }
                searchTimer = setTimeout(runSearch, 250);
            });

            $root.on('focus', '.vas-sics-field', function () {
                if ($input.val().trim() || hasFilters()) runSearch();
            });

            $root.on('click', '.vas-sics-clear', function () {
                $input.val('');
                $clear.removeClass('is-visible');
                /* Clears the TEXT only; the date ranges have their own "Clear all". */
                if (hasFilters()) { runSearch(); } else { closeSuggest(); }
                $input.focus();
            });

            $root.on('click', '.vas-sics-filter', function (e) {
                e.preventDefault();
                if (filtersOpen()) { closeFilters(); } else { openFilters(); }
            });

            $root.on('keydown', '.vas-sics-field', function (e) {
                var lines = $suggest ? $suggest.find('.vas-sics-line') : $();
                if (!$suggest || !$suggest.hasClass('is-open') || lines.length === 0) {
                    if (e.key === 'Escape') closeSuggest();
                    return;
                }
                if (e.key === 'ArrowDown') { e.preventDefault(); cursor = Math.min(cursor + 1, lines.length - 1); paintCursor(lines); }
                else if (e.key === 'ArrowUp') { e.preventDefault(); cursor = Math.max(cursor - 1, 0); paintCursor(lines); }
                else if (e.key === 'Enter') { if (cursor >= 0 && matches[cursor]) { e.preventDefault(); zoomInvoice(matches[cursor].cInvoiceId); } }
                else if (e.key === 'Escape') { closeSuggest(); }
            });

            /* Click outside the input zone or the dropdown closes the SUGGEST list. The filter
               popover is deliberately NOT closed here: it holds a framework lookup whose Info
               window lives outside both elements, and losing the half-set filters to a stray click
               is worse than leaving it up. It closes on the funnel toggle, Escape or Apply. */
            $(document).on('mousedown.vasSics-' + ($self.AD_UserHomeWidgetID || ''), function (e) {
                var $t = $(e.target);
                if ($t.closest('.vas-sics-zone').length) return;
                if ($suggest && $t.closest($suggest).length) return;
                if ($filters && $t.closest($filters).length) return;
                closeSuggest();
            });
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
            if (!q && !hasFilters()) { closeSuggest(); return; }

            ensureSuggest();
            $suggest.html('<div class="vas-sics-state">' + escapeHtml(lbl('VAS_063_Searching', 'Searching…')) + '</div>');
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
                url: VIS.Application.contextUrl + 'Invoices/SearchInvoices',
                type: 'GET',
                data: {
                    q: q, max: 25, page: page,
                    /* Empty bounds are sent as '' / 0 and ignored server-side (open-ended range).
                       Amounts go over the wire as plain invariant numbers — VAmountTextBox has
                       already resolved the user's decimal separator. */
                    invFrom: filterState.invFrom, invTo: filterState.invTo,
                    dueFrom: filterState.dueFrom, dueTo: filterState.dueTo,
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
            var word = matches.length === 1 ? lbl('VAS_063_Match', 'match') : lbl('VAS_063_Matches', 'matches');
            var count = matches.length + (hasMore ? '+' : '');
            /* What was searched: the term, the applied ranges, or both. */
            var subject = q ? '"' + escapeHtml(q) + '"' : escapeHtml(filterSummary());
            var extra = (q && hasFilters()) ? ' <span class="vas-sics-metafilter">' + escapeHtml(filterSummary()) + '</span>' : '';
            return '<div class="vas-sics-meta"><strong>' + count + '</strong> ' + word + ' ' +
                lbl('VAS_063_For', 'for') + ' ' + subject + extra + '</div>';
        }

        function updateMeta(q) {
            if (!$suggest) return;
            var $meta = $suggest.find('.vas-sics-meta');
            if ($meta.length) { $meta.replaceWith(metaHtml(q)); }
        }

        /* Build row markup for a slice, using absolute indices (startIndex + j) so
           data-index maps into `matches` for click / keyboard selection. Meta:
           status-coloured dot + "docNo · amount · status · date" (highlighted matches). */
        function rowsHtml(rows, startIndex, q) {
            var html = '';
            $.each(rows, function (j, r) {
                var i = startIndex + j;
                var color = PALETTE[i % PALETTE.length];
                /* Left meta: status dot + document number + document type + status. Document type is
                   highlighted like the other searchable fields (it IS searchable — by name or
                   DocBaseType). Amount and invoice date live in the right-hand column instead. */
                var meta = '<span class="vas-sics-dot" style="background:' + statusColor(r.docStatus) + ';"></span>' +
                    highlight(r.documentNo, q) +
                    (r.docTypeName ? ' · ' + highlight(r.docTypeName, q) : '') +
                    /* Sales order the invoice came from - labelled, and highlighted like the other
                       searchable fields, so a search by order number shows WHY each row matched. */
                    (r.orderDocumentNo ? ' · ' + escapeHtml(lbl('VAS_063_SalesOrder', 'SO')) + ' ' + highlight(r.orderDocumentNo, q) : '') +
                    ' · ' + escapeHtml(statusLabel(r.docStatus));
                /* Right column: amount over invoice date, right-aligned — the money is what the eye
                   scans down, so it gets its own edge instead of trailing the meta line. */
                var side = '<div class="vas-sics-amt">' + formatAmount(r.grandTotal, r.curSymbol) + '</div>' +
                    (r.dateInvoiced ? '<div class="vas-sics-date">' + escapeHtml(r.dateInvoiced) + '</div>' : '');
                html += '<div class="vas-sics-line" data-index="' + i + '" role="option">' +
                    '<div class="vas-sics-avatar" style="background:' + color + ';">' + escapeHtml(initials(r.customerName)) + '</div>' +
                    '<div class="vas-sics-info">' +
                    '<div class="vas-sics-name">' + highlight(r.customerName || '—', q) + '</div>' +
                    '<div class="vas-sics-rowmeta">' + meta + '</div>' +
                    '</div>' +
                    '<div class="vas-sics-side">' + side + '</div>' +
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
                /* Name what actually failed to match: the term, or the range when searching by date alone. */
                var subject = q
                    ? '"<strong>' + escapeHtml(q) + '</strong>"'
                    : '<strong>' + escapeHtml(filterSummary() || lbl('VAS_063_SelectedRange', 'the selected filters')) + '</strong>';
                $suggest.html('<div class="vas-sics-empty">' + lbl('VAS_063_NoInvoicesMatch', 'No invoices match') + ' ' + subject + '.</div>');
                openSuggest();
                return;
            }

            $suggest.html(metaHtml(q) + '<div class="vas-sics-list">' + rowsHtml(matches, 0, q) + '</div>');
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

            var $list = $suggest.find('.vas-sics-list');
            if ($list.length === 0) { renderSuggest(matches, q); return; }

            $list.append(rowsHtml(rows, startIndex, q));
            updateMeta(q);
        }

        /* Show / hide the bottom "Loading more…" indicator during a page fetch. */
        function showLoadMore(show) {
            if (!$suggest) return;
            var $more = $suggest.find('.vas-sics-more');
            if (show) {
                if ($more.length === 0) {
                    $suggest.append('<div class="vas-sics-more">' + escapeHtml(lbl('VAS_063_LoadingMore', 'Loading more…')) + '</div>');
                }
            } else {
                $more.remove();
            }
        }

        /* ── Zoom: fire the value the host listens for to open this invoice in the window's first tab,
           filtered to the single record (single/form layout). ── */
        function zoomInvoice(cInvoiceId) {
            if (!cInvoiceId) return;
            closeSuggest();
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Invoice.C_Invoice_ID=" + cInvoiceId,
                    "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                    "TabIndex": "0"
                });
            }
            else {
                VAS.ZoomUtil.zoomToRecord("C_Invoice_ID", cInvoiceId, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            closeSuggest();
            closeFilters();
            if ($input) { $input.val(''); }
            if ($clear) { $clear.removeClass('is-visible'); }
            /* A refresh resets every filter too, so the widget comes back in its neutral state. */
            filterState = { invFrom: '', invTo: '', dueFrom: '', dueTo: '', amtFrom: '', amtTo: '', currencyId: 0, currencyName: '' };
            if ($filters) { clearFilterInputs(); }
            if ($filterBtn) { $filterBtn.removeClass('is-active'); }
            matches = [];
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('mousedown.vasSics-' + ($self.AD_UserHomeWidgetID || ''));
            /* Unconditional: unbindReposition would bail while a layer is still marked open. */
            document.removeEventListener('scroll', repositionLayers, true);
            window.removeEventListener('resize', repositionLayers);
            overlayWatch.stop();
            if (searchTimer) clearTimeout(searchTimer);
            if ($suggest) { $suggest.remove(); $suggest = null; }
            /* Both body-mounted layers must go with the widget, or they outlive the dashboard cell. */
            if ($filters) { $filters.remove(); $filters = null; }
            $root.remove();
        };
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay the fired value (zoom params) to the registered widget host. */
    VIS.SalesInvoiceCustomerSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (zoom). */
    VIS.SalesInvoiceCustomerSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.SalesInvoiceCustomerSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
