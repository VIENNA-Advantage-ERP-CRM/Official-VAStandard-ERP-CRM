/************************************************************
 * Module Name    : VAS
 * Purpose        : AP Invoice Search Widget
 *                  Full-width 9x1 dashboard search bar that searches
 *                  vendor (AP) invoices (C_Invoice, IsSOTrx = 'N') and
 *                  shows the most relevant matches in a dropdown.
 *                  Clicking a result zooms to the invoice record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * AD_Message keys used (add via System Messages):
 *   VAS_067_Placeholder       => "Search Invoices by Vendor, Document No., Reference No., Document Type, Amount, Status, Paid"
 *   VAS_DocSearch_TypeToSearch=> "Type at least 2 characters to search"
 *   VAS_DocSearch_NoResults   => "No matching documents"
 *   VAS_DocSearch_Error       => "Search failed. Please try again."
 *   VAS_DocSearch_Results     => "results"
 *   VAS_067_Paid              => "Paid"
 *   VAS_067_Unpaid            => "Unpaid"
 *   VAS_067_Rep               => "Representative"
 *   VAS_067_Ref               => "Reference No"
 *   VAS_063_Filters           => "Filters"          (filter popover, shared with VAS_063)
 *   VAS_063_InvoiceDate       => "Invoice Date"
 *   VAS_063_DueDate           => "Due Date"
 *   VAS_063_Amount            => "Amount"
 *   VAS_063_Currency          => "Currency"
 *   VAS_063_From / VAS_063_To => "From" / "To"
 *   VAS_063_Apply             => "Apply"
 *   VAS_063_ClearAll          => "Clear all"
 *   VAS_063_InvalidRange      => '"From" date must be on or before "To"'
 *   VAS_063_InvalidAmountRange=> '"From" amount must be less than or equal to "To"'
 *
 * Filters (funnel button, AND'ed on top of the term): Invoice Date (C_Invoice.DateInvoiced),
 * Due Date (C_InvoicePaySchedule.DueDate), a grand-total Amount band and a Currency restriction.
 * The amounts use the framework VAmountTextBox and the currency the framework VTextBoxButton
 * lookup on C_Currency_ID, so the decimal separator and the lookup behave as in a standard window.
 * A filter on its own is a valid search - the term may stay empty.
 *
 * Result row: the vendor's initials avatar leads (as in the sibling VAS_068 payment search) -
 * every hit is an AP invoice, so no document-kind chip - then doc no + status / paid pills,
 * the vendor name, and a muted detail line; amount and date keep the right-hand column.
 *
 * Amounts are NOT converted: each row shows the invoice's GrandTotal in the invoice's own
 * currency, formatted with that currency's symbol (ISO code as fallback) and StdPrecision,
 * both sent per row by the controller.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_067_APInvoiceSearchWidget = function () {
        // ---- Per-widget configuration ----
        var ENDPOINT      = 'VAS/VAS_067_APInvoiceSearchWidget/Search';
        var ZOOM_TABLE    = 'C_Invoice';
        // Zoom target when the widget is NOT hosted inside a window (windowNo < 0).
        // The search payload already carries the resolved AD_Window_ID; these names
        // are the fallback VAS.ZoomUtil resolves from if it ever comes back 0.
        var ZOOM_WINDOW_NAME_NEW = 'VAS_APInvoice';
        var ZOOM_WINDOW_NAME_OLD = 'Invoice (Vendor)';
        var PLACEHOLDER_K = 'VAS_067_Placeholder';
        var PLACEHOLDER_D = 'Search Invoices by Vendor, Document No., Reference No., Document Type, Amount, Status, Paid';

        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-dssrch-root">');
        var $bar, $input, $panel;
        var widgetID = null;

        var windowId = 0;
        var debounceTimer = null;
        var requestSeq = 0;
        var MIN_LEN = 2;
        var PAGE_SIZE = 25;
        var currentTerm = '';
        var loadedCount = 0;
        var hasMore = false;
        var isLoadingMore = false;

        /* ---- Filter popover (mounted on <body>, like the results panel) ---- */
        var $filterBtn = null, $filters = null;
        /* Applied filters. Dates are ISO yyyy-MM-dd, amounts plain numbers, currency a
           C_Currency_ID; '' / 0 = no bound. They NARROW the term; with an empty term they ARE
           the search. */
        var filterState = {
            invFrom: '', invTo: '', dueFrom: '', dueTo: '',
            amtFrom: '', amtTo: '', currencyId: 0, currencyName: ''
        };
        /* Framework controls inside the popover; null when VIS.Controls is unavailable (the
           popover then falls back to plain inputs / drops the currency row). */
        var amtFromCtrl = null, amtToCtrl = null, currencyCtrl = null;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'hidden';
        };

        /* ---- No initial data to load; framework hook just clears busy ---- */
        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = 'hidden';
        };

        function buildShell() {
            var placeholder = msg(PLACEHOLDER_K, PLACEHOLDER_D);

            $bar = $('<div class="vas-dssrch-bar" id="vas_dssrch_bar_' + widgetID + '">');
            $bar.append(
                '<svg class="vas-dssrch-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                    '<circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>' +
                '</svg>' +
                '<span class="vas-dssrch-spin"></span>'
            );
            $input = $('<input type="text" class="vas-dssrch-input" autocomplete="off" spellcheck="false">')
                .attr('placeholder', placeholder);
            $bar.append($input);

            var $clear = $('<button type="button" class="vas-dssrch-clear" tabindex="-1">&#215;</button>');
            $bar.append($clear);

            /* Funnel — opens the filter popover. The dot badge lights up while any filter is
               applied, so the narrowing is never invisible. */
            $filterBtn = $('<button type="button" class="vas-dssrch-filter" tabindex="-1" ' +
                'title="' + dsEsc(msg('VAS_063_Filters', 'Filters')) + '" ' +
                'aria-label="' + dsEsc(msg('VAS_063_Filters', 'Filters')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" ' +
                'stroke-linecap="round" stroke-linejoin="round"><path d="M22 3H2l8 9.46V19l4 2v-8.54L22 3z"/></svg>' +
                '<span class="vas-dssrch-filter-dot"></span></button>');
            $bar.append($filterBtn);
            $root.append($bar);

            // Dropdown lives on <body> so the dashboard cell's overflow:hidden
            // cannot clip it; positioned as a fixed popover under the bar.
            // vas-dssrch-panel--layered drops THIS widget's panel into the widget stacking band
            // (see the CSS) so the filter popover can sit above it while both stay under the
            // framework's lookup layers. The shared .vas-dssrch-panel z-index is left alone.
            $panel = $('<div class="vas-dssrch-panel vas-dssrch-panel--layered" id="vas_dssrch_panel_' + widgetID + '">');
            $('body').append($panel);
            $panel.on('scroll', onPanelScroll);

            wireEvents($clear);
        }

        function wireEvents($clear) {
            $input.on('input', function () {
                var term = $.trim($input.val());
                $bar.toggleClass('vas-dssrch-has-text', term.length > 0);
                scheduleSearch(term);
            });
            $input.on('focus', function () {
                var term = $.trim($input.val());
                if ((term.length >= MIN_LEN || hasFilters()) && $panel.children().length > 0) { openPanel(); }
            });
            $input.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closePanel(); }
            });
            $clear.on('click', function () {
                $input.val('');
                $bar.removeClass('vas-dssrch-has-text');
                /* Clears the TERM only; the filters have their own "Clear all". */
                if (hasFilters()) { setBusy(true); runSearch(''); }
                else { setBusy(false); closePanel(); }
                $input.focus();
            });
            $filterBtn.on('click', function (e) {
                e.preventDefault();
                if (filtersOpen()) { closeFilters(); } else { openFilters(); }
            });

            $self._onDocClick = function (e) {
                /* The filter popover is deliberately NOT closed by an outside click: it holds a
                   framework lookup whose Info window lives outside both elements, and losing
                   half-set filters to a stray click is worse than leaving it up. It closes on the
                   funnel toggle, Escape or Apply. */
                if (!$panel.hasClass('vas-dssrch-open')) { return; }
                if ($bar[0].contains(e.target) || $panel[0].contains(e.target)) { return; }
                if ($filters && $filters[0].contains(e.target)) { return; }
                closePanel();
            };
            /* Both layers are position:fixed against the bar, so they re-anchor on every scroll.
               The dashboard scrolls its OWN container and scroll events do not bubble, hence the
               capture-phase listener on window/document (registered below with `true`). */
            $self._onReflow = function () {
                if ($panel.hasClass('vas-dssrch-open')) { positionPanel(); }
                if (filtersOpen()) { positionFilters(); }
            };
            document.addEventListener('mousedown', $self._onDocClick, true);
            window.addEventListener('resize', $self._onReflow, true);
            window.addEventListener('scroll', $self._onReflow, true);
        }

        /* ================= Filter popover =================
           Two date ranges, an amount band and a currency restriction; every bound optional and
           open-endable. Dates are native <input type="date"> (always ISO on the wire); the amounts
           and the currency use the framework's own controls so the decimal separator and the
           C_Currency_ID lookup behave exactly as in a standard window. */
        function hasFilters() {
            return !!(filterState.invFrom || filterState.invTo || filterState.dueFrom || filterState.dueTo ||
                filterState.amtFrom !== '' || filterState.amtTo !== '' || filterState.currencyId);
        }

        /* "Invoice Date 2026-01-01 → 2026-01-31" pieces for the count line, so a filter-only
           search still says what it searched for. */
        function filterSummary() {
            var parts = [];
            if (filterState.invFrom || filterState.invTo) {
                parts.push(msg('VAS_063_InvoiceDate', 'Invoice Date') + ' ' +
                    (filterState.invFrom || '…') + ' → ' + (filterState.invTo || '…'));
            }
            if (filterState.dueFrom || filterState.dueTo) {
                parts.push(msg('VAS_063_DueDate', 'Due Date') + ' ' +
                    (filterState.dueFrom || '…') + ' → ' + (filterState.dueTo || '…'));
            }
            if (filterState.amtFrom !== '' || filterState.amtTo !== '') {
                parts.push(msg('VAS_063_Amount', 'Amount') + ' ' +
                    (filterState.amtFrom === '' ? '…' : filterState.amtFrom) + ' → ' +
                    (filterState.amtTo === '' ? '…' : filterState.amtTo));
            }
            if (filterState.currencyId) {
                parts.push(msg('VAS_063_Currency', 'Currency') + ' ' + (filterState.currencyName || filterState.currencyId));
            }
            return parts.join(' · ');
        }

        function dateRangeHtml(title, fromId, toId) {
            return '<div class="vas-dssrch-frow"><div class="vas-dssrch-flabel">' + dsEsc(title) + '</div>' +
                '<div class="vas-dssrch-fpair">' +
                '<label><span>' + dsEsc(msg('VAS_063_From', 'From')) + '</span><input type="date" id="' + fromId + '"></label>' +
                '<label><span>' + dsEsc(msg('VAS_063_To', 'To')) + '</span><input type="date" id="' + toId + '"></label>' +
                '</div></div>';
        }

        /* Amount row carries EMPTY slots; the framework controls are injected once the popover is
           in the DOM (a framework control must be built, then appended). */
        function slotRangeHtml(title, fromSlotId, toSlotId) {
            return '<div class="vas-dssrch-frow"><div class="vas-dssrch-flabel">' + dsEsc(title) + '</div>' +
                '<div class="vas-dssrch-fpair">' +
                '<label><span>' + dsEsc(msg('VAS_063_From', 'From')) + '</span><span class="vas-dssrch-fslot" id="' + fromSlotId + '"></span></label>' +
                '<label><span>' + dsEsc(msg('VAS_063_To', 'To')) + '</span><span class="vas-dssrch-fslot" id="' + toSlotId + '"></span></label>' +
                '</div></div>';
        }

        function frameworkCtrlsAvailable() {
            return !!(window.VIS && VIS.Controls && VIS.DisplayType && VIS.Env);
        }

        function buildAmountControls(uid) {
            var slots = [$filters.find('#' + uid + 'AmtFromSlot'), $filters.find('#' + uid + 'AmtToSlot')];
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VAmountTextBox) {
                slots[0].append('<input type="text" inputmode="decimal" class="vas-dssrch-fctrl" id="' + uid + 'AmtFrom">');
                slots[1].append('<input type="text" inputmode="decimal" class="vas-dssrch-fctrl" id="' + uid + 'AmtTo">');
                return;
            }
            try {
                var DT = VIS.DisplayType;
                amtFromCtrl = new VIS.Controls.VAmountTextBox('GrandTotal', false, false, true, 50, 100, DT.Amount, msg('VAS_063_From', 'From'));
                amtToCtrl = new VIS.Controls.VAmountTextBox('GrandTotal', false, false, true, 50, 100, DT.Amount, msg('VAS_063_To', 'To'));
                slots[0].append(amtFromCtrl.getControl().addClass('vas-dssrch-fctrl').css('width', '100%'));
                slots[1].append(amtToCtrl.getControl().addClass('vas-dssrch-fctrl').css('width', '100%'));
            } catch (e) {
                if (window.console) { console.log(e); }
                amtFromCtrl = null; amtToCtrl = null;
            }
        }

        /* Currency picker: the framework's C_Currency_ID Search lookup (VTextBoxButton + its info
           button). The where clause is inlined and carries no window-context @tokens@, so windowNo
           0 is fine for the lookup. */
        function buildCurrencyControl(uid) {
            var $slot = $filters.find('#' + uid + 'CurSlot');
            if (!frameworkCtrlsAvailable() || !VIS.Controls.VTextBoxButton || !VIS.MLookupFactory) {
                $slot.closest('.vas-dssrch-frow').remove();
                return;
            }
            try {
                var DT = VIS.DisplayType;
                var lookup = VIS.MLookupFactory.get(VIS.Env.getCtx(), ($self.windowNo > 0 ? $self.windowNo : 0), 0, DT.Search,
                    'C_Currency_ID', 0, false, " C_Currency.IsActive = 'Y' ");
                currencyCtrl = new VIS.Controls.VTextBoxButton('C_Currency_ID', false, false, true, DT.Search, lookup);
                $slot.append(currencyCtrl.getControl().addClass('vas-dssrch-fctrl').attr('data-hasbtn', ' ').css('width', '100%'));
                var btn = currencyCtrl.getBtn ? currencyCtrl.getBtn(0) : null;
                if (btn) { $slot.append($('<span class="vas-dssrch-fbtnwrap"></span>').append(btn)); }
                /* getValue() only yields the id — keep the display text for the summary line. */
                currencyCtrl.fireValueChanged = function () {
                    filterState.currencyName = (currencyCtrl.getDisplay ? currencyCtrl.getDisplay() : '') || '';
                };
            } catch (e) {
                if (window.console) { console.log(e); }
                currencyCtrl = null;
                $slot.closest('.vas-dssrch-frow').remove();
            }
        }

        function ensureFilters() {
            if ($filters) { return; }
            /* Ids carry the widget instance so two copies on one dashboard never collide. */
            var uid = 'vasAp' + widgetID;
            $filters = $('<div class="vas-dssrch-filters" role="dialog" aria-label="' + dsEsc(msg('VAS_063_Filters', 'Filters')) + '">');
            $filters.html(
                dateRangeHtml(msg('VAS_063_InvoiceDate', 'Invoice Date'), uid + 'InvFrom', uid + 'InvTo') +
                dateRangeHtml(msg('VAS_063_DueDate', 'Due Date'), uid + 'DueFrom', uid + 'DueTo') +
                slotRangeHtml(msg('VAS_063_Amount', 'Amount'), uid + 'AmtFromSlot', uid + 'AmtToSlot') +
                '<div class="vas-dssrch-frow"><div class="vas-dssrch-flabel">' + dsEsc(msg('VAS_063_Currency', 'Currency')) + '</div>' +
                '<div class="vas-dssrch-fcur" id="' + uid + 'CurSlot"></div></div>' +
                '<p class="vas-dssrch-ferror" role="alert"></p>' +
                '<div class="vas-dssrch-factions">' +
                '<button type="button" class="vas-dssrch-fbtn" data-act="clear">' + dsEsc(msg('VAS_063_ClearAll', 'Clear all')) + '</button>' +
                '<button type="button" class="vas-dssrch-fbtn vas-dssrch-fbtn-primary" data-act="apply">' + dsEsc(msg('VAS_063_Apply', 'Apply')) + '</button>' +
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
            /* "Clear all" blanks the fields and re-runs the search but KEEPS the popover open. */
            $filters.on('click', '[data-act=clear]', function () { clearFilterInputs(); applyFilters(true); });
            $filters.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeFilters(); $input.focus(); return; }
                if (e.key !== 'Enter' && e.keyCode !== 13) { return; }
                /* A framework control owns its own Enter (the lookup searches on it). */
                if ($(e.target).hasClass('vas-dssrch-fctrl')) { return; }
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
            if (!$filters || !$bar || !$bar[0]) { return; }
            var r = $bar[0].getBoundingClientRect();
            var w = $filters.outerWidth() || 320;
            var left = Math.max(8, Math.min(r.right - w, window.innerWidth - w - 8));
            $filters.css({ left: Math.round(left) + 'px', top: Math.round(r.bottom + 6) + 'px' });
        }

        function filtersOpen() { return !!($filters && $filters.hasClass('vas-dssrch-open')); }

        function openFilters() {
            ensureFilters();
            /* Repaint from the APPLIED state, so a popover closed without applying leaves no
               half-typed values behind. */
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
            $filters.find('.vas-dssrch-ferror').text('');
            $filters.addClass('vas-dssrch-open');
            positionFilters();
            $filters.find('input[type=date]').first().focus();
        }

        function closeFilters() {
            if ($filters) { $filters.removeClass('vas-dssrch-open'); }
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
            /* 0 in BOTH amount bounds is "no amount filter", not "invoices totalling exactly
               zero". (A single 0 bound stays meaningful: 0 → 5000 is a real band.) */
            if (next.amtFrom === 0 && next.amtTo === 0) { next.amtFrom = ''; next.amtTo = ''; }
            /* ISO strings compare lexicographically, so a plain > is a correct date compare. */
            if ((next.invFrom && next.invTo && next.invFrom > next.invTo) ||
                (next.dueFrom && next.dueTo && next.dueFrom > next.dueTo)) {
                $filters.find('.vas-dssrch-ferror').text(msg('VAS_063_InvalidRange', '"From" date must be on or before "To"'));
                return;
            }
            if (next.amtFrom !== '' && next.amtTo !== '' && next.amtFrom > next.amtTo) {
                $filters.find('.vas-dssrch-ferror').text(msg('VAS_063_InvalidAmountRange', '"From" amount must be less than or equal to "To"'));
                return;
            }
            if (!next.currencyId) { next.currencyName = ''; }
            filterState = next;
            $filterBtn.toggleClass('vas-dssrch-filter-on', hasFilters());
            if (!keepOpen) { closeFilters(); }
            var term = $.trim($input.val());
            if (term.length >= MIN_LEN || hasFilters()) { setBusy(true); runSearch(term); }
            else { setBusy(false); closePanel(); }
        }

        /* ---- Debounced search ---- */
        function scheduleSearch(term) {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            /* A filter on its own is a valid search, so an empty / too-short term keeps searching
               while any filter is applied - the 2-character minimum only guards the bare term. */
            if (term.length < MIN_LEN && hasFilters()) {
                setBusy(true);
                debounceTimer = window.setTimeout(function () { runSearch(term); }, 280);
                return;
            }
            if (term.length === 0) { setBusy(false); closePanel(); return; }
            if (term.length < MIN_LEN) { setBusy(false); renderHint(); return; }
            setBusy(true);
            debounceTimer = window.setTimeout(function () { runSearch(term); }, 280);
        }

        /* The filter values every request carries; empty bounds are sent as '' / 0 and ignored
           server-side (open-ended). Amounts go over the wire as plain invariant numbers -
           VAmountTextBox has already resolved the user's decimal separator. */
        function searchParams(term, offset) {
            return {
                query: term, maxRows: PAGE_SIZE, offset: offset,
                invFrom: filterState.invFrom, invTo: filterState.invTo,
                dueFrom: filterState.dueFrom, dueTo: filterState.dueTo,
                amtFrom: filterState.amtFrom, amtTo: filterState.amtTo,
                currencyId: filterState.currencyId || 0
            };
        }

        function runSearch(term) {
            var mySeq = ++requestSeq;
            currentTerm = term;
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                data: searchParams(term, 0),
                dataType: 'json',
                async: true,
                success: function (res) {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    if (data) {
                        windowId = VIS.Utility.Util.getValueOfInt(data.WindowId);
                    }
                    var items = data ? (data.Items || []) : [];
                    loadedCount = items.length;
                    hasMore = data ? !!data.HasMore : (items.length === PAGE_SIZE);
                    renderResults(items);
                },
                error: function () {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    renderError();
                }
            });
        }

        /* ---- Infinite scroll: fetch the next page and append ---- */
        function onPanelScroll() {
            if (!hasMore || isLoadingMore) { return; }
            if (!$panel.hasClass('vas-dssrch-open')) { return; }
            var el = $panel[0];
            if (el.scrollTop + el.clientHeight >= el.scrollHeight - 56) { loadMore(); }
        }

        function loadMore() {
            if (isLoadingMore || !hasMore) { return; }
            isLoadingMore = true;
            var mySeq = requestSeq;
            var term = currentTerm;
            var offset = loadedCount;
            showMoreSpinner(true);
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                data: searchParams(term, offset),
                dataType: 'json',
                async: true,
                success: function (res) {
                    if (mySeq !== requestSeq) { isLoadingMore = false; return; }
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    var items = data ? (data.Items || []) : [];
                    appendResults(items);
                    loadedCount += items.length;
                    hasMore = data ? !!data.HasMore : (items.length === PAGE_SIZE);
                    isLoadingMore = false;
                    showMoreSpinner(false);
                    updateCount();
                },
                error: function () {
                    if (mySeq !== requestSeq) { isLoadingMore = false; return; }
                    isLoadingMore = false;
                    showMoreSpinner(false);
                }
            });
        }

        /* ---- Rendering ---- */
        function renderHint() {
            $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_TypeToSearch', 'Type at least 2 characters to search'), false));
            openPanel();
        }

        function renderError() {
            $panel.html(stateHtml(alertSvg(), msg('VAS_DocSearch_Error', 'Search failed. Please try again.'), true));
            openPanel();
        }

        function renderResults(items) {
            if (!items || items.length === 0) {
                $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_NoResults', 'No matching documents'), false));
                openPanel();
                return;
            }

            var html = '<div class="vas-dssrch-count"></div><div class="vas-dssrch-list">';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], i);
            }
            html += '</div><div class="vas-dssrch-more"><span class="vas-dssrch-more-spin"></span></div>';
            $panel.html(html);

            bindRowClicks($panel.find('.vas-dssrch-list .vas-dssrch-row'));
            updateCount();
            $panel.scrollTop(0);
            openPanel();
        }

        function appendResults(items) {
            if (!items || items.length === 0) { return; }
            /* loadedCount is still the pre-append total here, so it is the absolute index of the
               first new row - which keeps the avatar palette cycling continuously instead of
               restarting at every page. */
            var startIndex = loadedCount;
            var html = '';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], startIndex + i);
            }
            var $rows = $(html).filter('.vas-dssrch-row');
            $panel.find('.vas-dssrch-list').append($rows);
            bindRowClicks($rows);
        }

        function bindRowClicks($rows) {
            $rows.on('click', function () {
                if ($(this).hasClass('vas-dssrch-nozoom')) { return; }
                zoomTo(VIS.Utility.Util.getValueOfInt($(this).attr('data-id')));
            });
        }

        function updateCount() {
            var text = loadedCount + (hasMore ? '+' : '') + ' ' + msg('VAS_DocSearch_Results', 'results');
            /* Echo the applied filters, so a filter-only search says what it searched for. */
            if (hasFilters()) { text += ' · ' + filterSummary(); }
            $panel.find('.vas-dssrch-count').text(text);
        }

        function showMoreSpinner(on) {
            $panel.find('.vas-dssrch-more').toggleClass('vas-dssrch-more-active', !!on);
        }

        /* The row is led by the business partner's initials avatar (same treatment as the sibling
           VAS_068 payment search) rather than a document-kind chip - every hit here is an AP
           invoice, so the vendor is the useful thing to recognise at a glance. */
        function buildRow(item, index) {
            var hasZoom = item.RecordId > 0;
            var sub = buildSubline(item);
            var color = PALETTE[index % PALETTE.length];
            return (
                '<div class="vas-dssrch-row' + (hasZoom ? '' : ' vas-dssrch-nozoom') + '" data-id="' + VIS.Utility.Util.getValueOfInt(item.RecordId) + '">' +
                    '<div class="vas-dssrch-avatar" style="background:' + color + ';" title="' + dsEsc(item.Title || '') + '">' + dsEsc(initials(item.Title)) + '</div>' +
                    '<div class="vas-dssrch-main">' +
                        '<div class="vas-dssrch-docline">' +
                            '<span class="vas-dssrch-docno">' + dsEsc(item.DocumentNo || '') + '</span>' +
                            statusPill(item.DocStatus) +
                            paidPill(item.IsPaid) +
                        '</div>' +
                        '<div class="vas-dssrch-title">' + dsEsc(item.Title || '') + '</div>' +
                        (sub ? '<div class="vas-dssrch-subline">' + sub + '</div>' : '') +
                    '</div>' +
                    '<div class="vas-dssrch-meta">' +
                        '<div class="vas-dssrch-amount">' + dsEsc(formatAmount(item)) + '</div>' +
                        '<div class="vas-dssrch-date">' + dsEsc(formatDate(item.DocDate)) + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        /* Avatar palette, cycled by row index (matches the sibling search widgets). */
        var PALETTE = ['#0083DA', '#019D89', '#D78B10', '#5F4AA6', '#A33F3F', '#2084C4'];

        function initials(name) {
            if (!name) { return '#'; }
            var parts = $.trim(name).split(/\s+/);
            if (parts.length >= 2) { return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase(); }
            return name.substring(0, 2).toUpperCase();
        }

        /* Muted detail line: document type · sales rep · invoice reference (only present parts). */
        function buildSubline(item) {
            var parts = [];
            if (item.DocType)    { parts.push(dsEsc(item.DocType)); }
            if (item.SalesRep) { parts.push(dsEsc(msg('VAS_067_Rep', 'Representative') + ': ' + item.SalesRep)); }
            if (item.InvoiceRef) { parts.push(dsEsc(msg('VAS_067_Ref', 'Reference No') + ': ' + item.InvoiceRef)); }
            return parts.join(' &middot; ');
        }

        /* Paid / Unpaid pill (green when paid, muted otherwise). */
        function paidPill(isPaid) {
            var paid = (isPaid === true) || String(isPaid).toUpperCase() === 'Y' || String(isPaid) === 'true';
            return '<span class="vas-dssrch-status vas-dssrch-status-' + (paid ? 'ok' : 'muted') + '">' +
                dsEsc(paid ? msg('VAS_067_Paid', 'Paid') : msg('VAS_067_Unpaid', 'Unpaid')) + '</span>';
        }

        function zoomTo(recordId) {
            if (!recordId) { return; }
            closePanel();
            try {
                if ($self.windowNo >= 0) {
                    // Navigate the CURRENT window's grid to the clicked record (no new window).
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + recordId,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                }
                else {
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", recordId, windowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { windowId = id; }
                        });
                }
            } catch (e) { /* zoom is best-effort */ }
        }

        /* ---- Panel helpers ---- */
        function openPanel() { positionPanel(); $panel.addClass('vas-dssrch-open'); $bar.addClass('vas-dssrch-bar-focus'); }
        function closePanel() { $panel.removeClass('vas-dssrch-open'); $bar.removeClass('vas-dssrch-bar-focus'); }
        function positionPanel() {
            if (!$bar || !$bar[0]) { return; }
            var rect = $bar[0].getBoundingClientRect();
            $panel.css({ left: Math.round(rect.left) + 'px', top: Math.round(rect.bottom + 6) + 'px', width: Math.round(rect.width) + 'px' });
        }
        function setBusy(on) { $bar.toggleClass('vas-dssrch-busy', !!on); }

        /* ---- Formatters / helpers ---- */
        /* The amount is NOT converted server-side: it is the invoice's GrandTotal in the invoice's
           OWN currency, so it is formatted with that currency's symbol (ISO code when the currency
           has no symbol) and its own StdPrecision - never the login / schema currency. */
        function formatAmount(item) {
            var raw = item ? item.Amount : null;
            var n = (typeof raw === 'number') ? raw : parseFloat(raw);
            if (isNaN(n)) { return ''; }
            var prec = (item && typeof item.StdPrecision === 'number' && item.StdPrecision >= 0) ? item.StdPrecision : 2;
            var symbol = (item && (item.CurSymbol || item.IsoCode)) || '';
            // Sign BEFORE the currency symbol, and NO space between symbol and amount
            // (e.g. -$28,000.000, not $ -28,000.000).
            var sign = n < 0 ? '-' : '';
            var formatted = Math.abs(n).toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return sign + symbol + formatted;
        }
        function formatDate(iso) {
            if (!iso) { return ''; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }
        function statusPill(code) {
            if (!code) { return ''; }
            var m = statusMeta(code);
            return '<span class="vas-dssrch-status vas-dssrch-status-' + m.tone + '">' + dsEsc(m.label) + '</span>';
        }

        function statusMeta(code) {
            switch (String(code).toUpperCase()) {
                case 'CO': return { label: 'Completed',       tone: 'ok' };
                case 'CL': return { label: 'Closed',          tone: 'ok' };
                case 'AP': return { label: 'Approved',        tone: 'info' };
                case 'DR': return { label: 'Draft',           tone: 'muted' };
                case 'IP': return { label: 'In Process',      tone: 'warn' };
                case 'WC': return { label: 'Waiting Confirm', tone: 'warn' };
                case 'WP': return { label: 'Waiting Payment', tone: 'warn' };
                case 'NA': return { label: 'Not Approved',    tone: 'err' };
                case 'IN': return { label: 'Invalid',         tone: 'err' };
                case 'VO': return { label: 'Voided',          tone: 'err' };
                case 'RE': return { label: 'Reversed',        tone: 'err' };
                default:   return { label: code,              tone: 'muted' };
            }
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function dsEsc(str) {
            return String(str == null ? '' : str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        }
        function searchSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>';
        }
        function alertSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
        }
        function stateHtml(svg, text, isError) {
            return '<div class="vas-dssrch-state' + (isError ? ' vas-dssrch-state-error' : '') + '">' + svg + dsEsc(text) + '</div>';
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'hidden';
            $root.append($bsyDiv);
        }

        this.refreshWidget = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            requestSeq++;
            currentTerm = '';
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $input.val('');
            $bar.removeClass('vas-dssrch-has-text');
            setBusy(false);
            $panel.empty();
            closePanel();
            /* A refresh resets the filters too, so the widget comes back in its neutral state. */
            closeFilters();
            filterState = { invFrom: '', invTo: '', dueFrom: '', dueTo: '', amtFrom: '', amtTo: '', currencyId: 0, currencyName: '' };
            if ($filterBtn) { $filterBtn.removeClass('vas-dssrch-filter-on'); }
            if ($filters) { clearFilterInputs(); }
        };

        this.getRoot = function () { return $root; };

        this._teardown = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); }
            if ($self._onDocClick) { document.removeEventListener('mousedown', $self._onDocClick, true); }
            if ($self._onReflow) {
                window.removeEventListener('resize', $self._onReflow, true);
                window.removeEventListener('scroll', $self._onReflow, true);
            }
            if ($panel) { $panel.remove(); }
            /* Both body-mounted layers must go with the widget, or they outlive the dashboard cell. */
            if ($filters) { $filters.remove(); $filters = null; }
            amtFromCtrl = null; amtToCtrl = null; currencyCtrl = null;
        };
    };

    VAS.VAS_067_APInvoiceSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };
    VAS.VAS_067_APInvoiceSearchWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_067_APInvoiceSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_067_APInvoiceSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_067_APInvoiceSearchWidget.prototype.widgetSizeChange = function (widget) { this.widgetInfo = widget; };
    VAS.VAS_067_APInvoiceSearchWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
